# NodeKit CLI / Recipe Spec — DRAFT (2026-06-22)

> **Status: partially implemented, design still under review.** The recipe
> model, recipe-level validator, and renderer described below exist in code
> (`src/Authoring/Recipes/`, `src/Validation/Recipes/`,
> `tests/NodeKit.Tests/Recipes/`) and are covered by tests. The CLI executable
> itself (`src/NodeKit.Cli/`) is still not built — only the headless library
> layer underneath it. Treat this document as the current state of that
> library plus the remaining open questions, not a finished design.

## 0. Current path vs. future path

This draft only targets the **current legacy path**:

```text
RecipeDocument -> ToolDefinition -> BuildRequest -> BuildAndRegister (gRPC)
```

The vendored proto in this repo (`protos/nodevault/v1/nodevault.proto`) has no
`ToolSpecRequest`, `raw_spec`, `ResolveToolSpec`, or `SubmitToolBuild` — only
`BuildService.BuildAndRegister(BuildRequest)`. Those names describe a
**future** path that opens only after Sprint 6 entry criteria are met:

```text
RecipeDocument -> ToolSpecRequest.raw_spec -> ResolveToolSpec / SubmitToolBuild
```

Nothing in this document or in `src/Authoring/Recipes/` implements that future
path. `RecipeRenderer` renders to `ToolDefinition` only.

## 1. Design principle

The schema below is entirely NodeKit's to define — once the future path opens,
`raw_spec` will be opaque to NodeVault, which only computes digests over it
and recognizes `base_image`/`base_image_uri`/`image_uri` keys. For the current
path, a `RecipeDocument` renders into the existing `ToolDefinition` fields
(`ImageUri`, `DockerfileContent`, `Script`, `Command`, `Inputs`, `Outputs`,
display fields) so the existing, already-tested L1 validators apply unchanged
after rendering.

The implemented model is a **flat POCO** (`RecipeDocument`), matching every
other model in this codebase (`ToolDefinition`, `BuildRequest`,
`DataDefinition`) — not the nested `variant_payload` JSON object an earlier
version of this draft sketched. A flat shape needs no custom/polymorphic JSON
converter and serializes with `System.Text.Json` the same way every other
model here already does.

## 2. Two-tier validation

Validation runs in two tiers, because some recipe fields (e.g.
`SourceChecksum`) have no equivalent on `ToolDefinition` at all:

```text
1. RecipeValidator.Validate(RecipeDocument)
   - per-variant completeness (e.g. BioContainer needs BioContainerImageUri)
   - SourceChecksum format (L1-SRC-001 / L1-SRC-002)
   - does NOT duplicate checks the rendered ToolDefinition will already get

2. RecipeRenderer.Render(RecipeDocument) -> ToolDefinition

3. Existing ToolDefinition-level validators (unchanged):
   RequiredFieldsValidator, ImageUriValidator,
   DockerfileStructureValidator, PackageVersionValidator
```

`RecipeValidator` intentionally does not re-check `ToolName`/`Version`/digest
pinning/Dockerfile structure — those are already caught after render by the
existing chain, so duplicating them would just be redundant rule IDs for the
same violation.

## 3. Recipe variants

```text
1. conda / bioconda / conda-forge
2. micromamba
3. existing BioContainer
4. source build
5. local package mirror
6. Dockerfile fallback
```

## 4. RecipeDocument fields and per-variant render

`RecipeDocument` (`src/Authoring/Recipes/RecipeDocument.cs`):

```text
Variant              RecipeVariant enum (discriminator)
ToolName, Version
BaseImage             used by Conda/Micromamba/PackageMirror/SourceBuild/DockerfileFallback
Channels, Packages    used by Conda/Micromamba/PackageMirror
PackageMirrorUri      used by PackageMirror only
BioContainerImageUri  used by BioContainer only
SourceUri, SourceChecksum, SourceBuildCommands   used by SourceBuild only
DockerfileContent     used by DockerfileFallback only
Script, Command, Inputs, Outputs, Display*       same as ToolDefinition
```

All variant-specific fields are simply unused (left at their default) when
not relevant to the chosen `Variant` — the same pattern `ToolDefinition`
already uses for optional fields like `Command`.

### 4.1 / 4.2 / 4.5 — conda, micromamba, package mirror

All three render to the same shape: a `FROM <BaseImage>` line, one `RUN
<installer> config ...` line per channel (or the mirror URI as a single
pseudo-channel for package mirror), then one `RUN <installer> install -y
<packages...>` line.

Channel/mirror configuration is deliberately rendered on its **own** `RUN`
line, never combined with the `install` line. `PackageVersionValidator`
scans every token after `install` as a package pin; a channel name like
`bioconda` has no `=version=build` in it and would otherwise be misread as an
unpinned package and rejected. `conda config --add channels ...` /
`micromamba config append channels ...` lines don't start with `install`, so
the validator's `IsCondaInstallCommand` check skips them entirely — already
true of the existing validator, not a new carve-out.

`PackageVersionValidator.IsCondaInstallCommand` already recognizes both
`conda install` and `micromamba install` tokens, so no new validator was
needed for any of these three variants. Verified by
`RecipeRendererTests.Render_Conda_PassesFullL1ValidatorChain`,
`Render_Micromamba_UsesMicromambaInstallCommand`, and
`Render_PackageMirror_UsesMirrorUriAsChannel`, which run the rendered
`ToolDefinition` through the full existing L1 chain and assert zero
violations.

### 4.3 — existing BioContainer

**Revised from the previous version of this draft.** The earlier version
proposed rendering BioContainer with `ImageUri` set and no
`DockerfileContent` at all. That breaks immediately:
`RequiredFieldsValidator` (`L1-REQ-002`) requires non-empty
`DockerfileContent` unconditionally, for every variant, because
`BuildRequest.dockerfile_content` is a required proto field on the current
legacy path regardless of how the image's contents were chosen.

The fix: BioContainer renders a **minimal wrapper Dockerfile** —
`FROM <BioContainerImageUri>\n`, nothing else — instead of treating the
external image as needing no Dockerfile at all. `ImageUri` is also set to the
same pinned URI. This satisfies every existing validator unchanged
(`RequiredFieldsValidator`, `ImageUriValidator`,
`DockerfileStructureValidator`) without inventing a "no Dockerfile" code path
that the current `BuildRequest` shape can't actually carry.

**Resolved open question:** does registering from an already-published
external image need a real NodeVault-side build, or only a registry
mirror/copy into Harbor? On the current legacy path the answer no longer
matters to NodeKit — NodeKit always supplies a buildable wrapper Dockerfile,
so NodeVault's `BuildService` runs the same way for BioContainer as for every
other variant. What NodeVault chooses to optimize internally (e.g.
short-circuiting a single-`FROM` Dockerfile into a tag/copy instead of a real
build) is NodeVault's call and out of scope here.

### 4.4 — source build

Renders `FROM <BaseImage>`, then one `RUN` line that downloads `SourceUri`,
verifies it against `SourceChecksum` via `sha256sum -c`, extracts it, and runs
`SourceBuildCommands` joined with `&&`. `SourceChecksum` is stored as
`sha256:<64-hex>` (self-describing) but the renderer strips the `sha256:`
prefix before embedding it in the `sha256sum -c` line, which expects a bare
hex digest.

`RecipeValidator` rejects a missing checksum (`L1-SRC-001`) or a malformed one
(`L1-SRC-002`, must match `^sha256:[0-9a-fA-F]{64}$`) — implemented and
tested, not just proposed as in the previous draft revision. This sits at the
recipe tier (Section 2) because `ToolDefinition` has no field to check this
against once rendered.

### 4.6 — Dockerfile fallback

`ImageUri = BaseImage`, `DockerfileContent` passed through verbatim. This is
exactly today's existing authoring path — unchanged, fully covered by the
existing validator chain. The known `L1-DOCKER-009` weakness (FROM-line
digest check is a substring `Contains` rather than the stricter format check
`L1-IMG-005` uses) is a pre-existing issue, untouched by this draft.

## 5. CLI command interface

Library-level (`RecipeValidator` / `RecipeRenderer`) is implemented. The CLI
commands below describe the intended thin wrapper over that library; the
executable itself is not yet built (Section 6).

```text
nodekit recipe select --variant <id> --tool-name <name> --version <version> --out <recipe.json>
  Writes a recipe.json skeleton for the chosen variant.
  Exit 0 success. Exit 2 unknown --variant or missing required flags.

nodekit spec render --recipe <recipe.json> --out <tool-definition.json>
  Calls RecipeRenderer.Render and writes the resulting ToolDefinition as JSON.
  Exit 0 success. Exit 2 malformed JSON / IO error.

nodekit validate --input <recipe.json|tool-definition.json>
  Runs RecipeValidator (if given a recipe.json) and/or the existing L1
  validator chain (if given a tool-definition.json) and prints violations.
  Exit 0 zero violations. Exit 1 one or more violations. Exit 2 IO/parse error.

nodekit request export --input <tool-definition.json> --out <build-request.json>
  Re-runs validate internally first and refuses to write the file if it
  fails (fail-closed — an export must never carry an unvalidated definition).
  Maps ToolDefinition -> BuildRequest via the existing BuildRequestFactory.
  No network call.
  Exit 0 success. Exit 1 validation failed, nothing written. Exit 2 IO error.
```

`nodekit submit` is **not specified here** — see Section 6. Reserving the name
in prose without a stub avoids implying it is close to working.

Renamed from the previous draft: `toolspec.json` → `tool-definition.json`,
because this repo has no real `ToolSpecRequest` yet (Section 0) and the old
name read as if it did.

File naming convention (suggested default for `--out`, not enforced):
`recipe.<tool-name>.<version>.json`, `tool-definition.<tool-name>.<version>.json`,
`build-request.<tool-name>.<version>.json`.

## 6. `src/NodeKit.Cli/` scope

**Implemented now**, no NodeVault dependency:

- `src/Authoring/Recipes/RecipeVariant.cs`, `RecipeDocument.cs`,
  `RecipeRenderer.cs`
- `src/Validation/Recipes/RecipeValidator.cs`
- `tests/NodeKit.Tests/Recipes/RecipeValidatorTests.cs`,
  `RecipeRendererTests.cs` — including end-to-end checks that every variant's
  rendered `ToolDefinition` passes the full existing L1 validator chain with
  zero violations.

**Still not started:**

- The actual `nodekit` CLI executable. Re-including `src/NodeKit.Cli/` in the
  solution (currently excluded from `NodeKit.csproj`) is a separate task.
- `nodekit submit` in any form — not even a hard-fail stub. A stub command
  risks reading as "almost ready"; the cleanest signal that submission isn't
  available yet is for the command to simply not exist.
- Any change to `L1-DOCKER-009`/`L1-DOCKER-010` (pre-existing, unrelated to
  this draft).

`recipe_variant`/`RecipeVariant` is a **NodeKit authoring-layer term only** —
NodeVault has no equivalent schema today. If NodeVault later introduces an
official `build_strategy`/recipe-kind field, NodeKit may need a migration
layer; nothing here assumes that will happen or what it would look like.

## 7. Open questions for review

1. ~~Does BioContainer need a NodeVault-side build step?~~ **Resolved**
   (Section 4.3): on the current legacy path it always renders a buildable
   Dockerfile, so the question doesn't block NodeKit's side anymore.
2. ~~Source-build checksum rule: design now or later?~~ **Resolved**:
   implemented now (`L1-SRC-001`/`002`, `RecipeValidator`), since source build
   without a checksum breaks reproducibility immediately, not just eventually.
3. ~~Three artifacts or fewer?~~ Kept three (`recipe.json` →
   `tool-definition.json` → `build-request.json`), renamed the middle one.
4. ~~Is `recipe_variant` the right name?~~ Kept, now explicitly documented as
   NodeKit-only (Section 6).
5. ~~Should a `submit` stub ship this sprint?~~ **No** — not specified at all
   (Section 5/6).
6. **New, still open:** `ImageUri` semantics for non-BioContainer variants.
   The renderer currently sets `ToolDefinition.ImageUri = BaseImage` for
   Conda/Micromamba/PackageMirror/SourceBuild/DockerfileFallback — i.e. it
   reuses the pinned input image as the value of a field whose actual product
   meaning (intended push target? same as the Dockerfile's `FROM`? something
   NodeVault overwrites post-build?) was never fully pinned down even before
   this draft existed. This is a pragmatic placeholder, not a confirmed
   semantic — needs a decision before the CLI executable ships.
7. **New, still open:** none of the six variants' rendered Dockerfiles have
   been run through an actual `docker build` — only through NodeKit's L1
   static validators. The shell syntax (`curl`/`sha256sum -c`/`conda
   config`/etc.) is reasonable but unverified against a real builder.

## 8. 한국어 요약

이 문서는 **부분 구현됨, 설계는 아직 검토 중** 상태다. 아래에서 설명하는 recipe
모델, recipe-level validator, renderer는 실제 코드로 존재하며
(`src/Authoring/Recipes/`, `src/Validation/Recipes/`,
`tests/NodeKit.Tests/Recipes/`) 테스트로 커버된다. CLI 실행 파일
(`src/NodeKit.Cli/`)은 아직 만들지 않았다 — 그 아래 헤드리스 라이브러리 레이어만
존재한다.

**현재 경로 vs 미래 경로**: 이 초안은 현재 legacy 경로만 대상으로 한다 —
`RecipeDocument → ToolDefinition → BuildRequest → BuildAndRegister`. 이 저장소에
vendoring된 proto에는 `ToolSpecRequest`/`raw_spec`/`ResolveToolSpec`/
`SubmitToolBuild`가 없다 — `BuildService.BuildAndRegister(BuildRequest)`만
있다. 그 이름들은 Sprint 6 진입 조건이 충족된 후에만 열리는 **미래** 경로를
가리킨다.

**모델은 flat POCO**: 이전 초안의 nested `variant_payload` 대신, 이 저장소의
다른 모든 모델(`ToolDefinition`, `BuildRequest`, `DataDefinition`)과 동일한 flat
구조로 구현했다 — 커스텀 polymorphic JSON 컨버터가 필요 없다.

**2단 검증 구조**: `RecipeValidator`(recipe-level: variant 완전성 +
`SourceChecksum` 형식) → `RecipeRenderer`(→ `ToolDefinition`) → 기존
`ToolDefinition`-level validator 체인. `RecipeValidator`는 렌더링 후 이미
잡히는 검증(이름/버전/digest pinning/Dockerfile 구조)을 중복 체크하지 않는다.

**BioContainer 수정**: 이전 초안은 BioContainer를 "Dockerfile 없는" 경로로
그렸는데, 이는 `RequiredFieldsValidator`(`L1-REQ-002`)가 모든 variant에
`DockerfileContent`를 무조건 요구하는 것과 즉시 충돌한다 — 리뷰에서 정확히
지적된 부분. 수정: BioContainer도 `FROM <BioContainerImageUri>`만 있는 최소
wrapper Dockerfile을 렌더링한다. NodeVault가 이걸 실제로 빌드할지, 내부적으로
mirror/copy로 최적화할지는 NodeVault의 선택이고 NodeKit 쪽 질문은 더 이상
막혀 있지 않다.

**source build 체크섬**: `L1-SRC-001`(누락)/`L1-SRC-002`(형식 불일치)을
제안만 하는 게 아니라 `RecipeValidator`에 실제로 구현하고 테스트까지 작성했다.

**이름 변경**: `toolspec.json` → `tool-definition.json` (실제 `ToolSpecRequest`가
아직 없으므로 혼동 방지). `submit` stub은 이번 스프린트에 코드로도, 문서
스펙으로도 넣지 않는다 — 커맨드가 존재하지 않는 것 자체가 "아직 안 됨"을 가장
명확하게 전달하는 방법이다.

**새로 남은 질문**: (1) BioContainer 외 variant들에서 `ImageUri =
BaseImage`로 재사용하는 것이 맞는 product 의미인지 — 이 필드의 실제 의미(빌드
타깃? FROM과 동일해야 함? NodeVault가 빌드 후 덮어씀?)는 이 초안 이전부터도
확정된 적이 없었다. (2) 6개 variant가 생성하는 Dockerfile 전부 NodeKit L1
정적 검증만 통과했을 뿐, 실제 `docker build`로 검증된 적은 없다.
