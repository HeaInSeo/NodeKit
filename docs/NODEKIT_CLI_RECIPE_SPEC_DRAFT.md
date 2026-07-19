# NodeKit CLI / Recipe Spec — DRAFT (2026-06-22)

> **Superseded (2026-07-19).** This entire draft predates NodeVault Phase 1
> (gate opened 2026-07-02) and describes `BuildAndRegister` as "current" and
> `ToolSpecRequest`/`ResolveToolSpec`/`SubmitToolBuild` as a "future" path —
> that is no longer true. `nodekit submit` exists and is the only build
> submission path (CLI and Avalonia GUI both), `BuildAndRegister`/
> `IBuildClient`/`GrpcBuildClient` are fully removed from this repo, and the
> vendored proto has carried `ToolSpecRequest`/`ResolveToolSpec`/
> `SubmitToolBuild` since Phase 1 opened. The `RecipeDocument` model,
> `RecipeValidator`, and `RecipeRenderer` design described below (sections
> 1-7) is still accurate — only the "current legacy path vs. future path"
> framing in section 0 and the 한국어 요약 (section 8) is wrong. For current
> state, see `CLAUDE.md` §0 and `docs/NODEKIT_CLI_USAGE.md`. Kept below,
> unmodified, as historical design-decision record.

> **Original status note (2026-06-22, now stale — see above):**
> partially implemented, design still under review. The recipe
> model, recipe-level validator, and renderer described below exist in code
> (`src/Authoring/Recipes/`, `src/Validation/Recipes/`,
> `tests/NodeKit.Tests/Recipes/`) and are covered by tests. The CLI executable
> itself (`src/NodeKit.Cli/`) is still not built — only the headless library
> layer underneath it. Treat this document as the current state of that
> library plus the remaining open questions, not a finished design.

## 0. Current path vs. future path

**Superseded (2026-07-19)**: the "future" path below is now the only path.
`ToolSpecRequest`/`ResolveToolSpec`/`SubmitToolBuild` have been in the
vendored proto since NodeVault Phase 1 opened (2026-07-02), `nodekit submit`
implements the full chain, and `RecipeRenderer` output additionally feeds
`ToolSpecRawSpecFactory.Build()` to produce `raw_spec` (see
`docs/NODEKIT_CLI_USAGE.md` §3). `BuildAndRegister` is fully removed from
this repo (both CLI and Avalonia GUI). Kept below, unmodified, as the
original design record of what this draft was scoped to when written.

This draft only targets the **current legacy path** (at time of writing,
2026-06-22):

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
`bioconda` has no `=version` in it and would otherwise be misread as an
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

**Build string 정책 (2026-06-28 확정)**: `PackageVersionValidator`는
`=version` 형식(버전 고정)까지만 요구한다. `=version=build` 형식의
conda build string 고정은 NodeKit L1의 요구사항이 아니다. build string
결정은 NodeVault `ResolveRecipe` RPC가 Harbor 조회를 통해 담당한다
(PLATFORM_MASTER_DESIGN.md §4.9 참조). 사용자는 `bwa=0.7.17`처럼
버전까지만 입력하면 되고, build string 후보는 NodeVault가 반환한다.
후보가 복수이면 NodeKit이 목록을 표시하고 사용자가 선택한 뒤
BuildRequest에 고정된다.

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

**ResolveRecipe 연동 (2026-06-28 확정)**: BioContainer도 BuildRequest 생성 전
NodeVault `ResolveRecipe` RPC를 통해 사전 조회한다. Harbor에 동일
tool+version 이미지가 있으면 그 이미지를 재사용하고, 없으면 열린망에서
BioContainers registry (quay.io/biocontainers)에서 후보를 조회하여 사용자에게
표시한다. 폐쇄망에서 Harbor에 없으면 `InvalidArgument` 반환
(PLATFORM_MASTER_DESIGN.md §4.9 참조).

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

Implemented in `src/NodeKit.Cli/` (`Program.cs` / `CliApp.cs`), registered in
`NodeKit.sln`. Two commands only — the earlier four-command draft below was
collapsed once it was clear `recipe select` (skeleton generation) and the
split `spec render` / `request export` / `validate --input` surface added
ceremony without adding safety: both real commands already have to render
and validate internally before they can do anything useful, so there is no
path where rendering or validating alone is the end goal.

```text
nodekit validate <recipe.json>
  Runs RecipeValidator on the recipe, then RecipeRenderer.Render to get a
  ToolDefinition, then the full existing L1 validator chain
  (RequiredFieldsValidator, ImageUriValidator, DockerfileStructureValidator,
  PackageVersionValidator) on that ToolDefinition. Prints "OK" on success;
  prints every violation as "<RuleId> (<Field>): <Message>" to stderr on
  failure.
  Exit 0 zero violations. Exit 1 one or more violations.
  Exit 2 missing argument, unreadable file, or malformed recipe JSON.

nodekit render <recipe.json> --out <build-request.json>
  Runs the same validate step internally first and refuses to write the
  output file if it fails (fail-closed — a render must never carry an
  unvalidated definition). On success, maps ToolDefinition -> BuildRequest
  via the existing BuildRequestFactory and writes it as indented JSON
  (the legacy BuildRequest POCO shape, PascalCase fields). `--out -` writes
  to stdout instead of a file. No network call.
  Exit 0 success. Exit 1 validation failed, nothing written.
  Exit 2 missing argument, unreadable file, or malformed recipe JSON.
```

There is no separate `recipe select` (skeleton generation), `spec render`
(ToolDefinition-only export), or `request export` (validate-only-then-export)
command — `validate` and `render` each do the full
recipe-validate -> render -> L1-validate pipeline in one step.

`nodekit submit` is **not specified here** — see Section 6. Reserving the name
in prose without a stub avoids implying it is close to working.

File naming convention (suggested default for `--out`, not enforced):
`recipe.<tool-name>.<version>.json`, `build-request.<tool-name>.<version>.json`.

## 6. `src/NodeKit.Cli/` scope

**Implemented now**, no NodeVault dependency:

- `src/Authoring/Recipes/RecipeVariant.cs`, `RecipeDocument.cs`,
  `RecipeRenderer.cs`
- `src/Validation/Recipes/RecipeValidator.cs`
- `tests/NodeKit.Tests/Recipes/RecipeValidatorTests.cs`,
  `RecipeRendererTests.cs` — including end-to-end checks that every variant's
  rendered `ToolDefinition` passes the full existing L1 validator chain with
  zero violations.
- `src/NodeKit.Cli/NodeKit.Cli.csproj`, `Program.cs`, `CliApp.cs` — the
  `validate`/`render` commands described in Section 5, registered in
  `NodeKit.sln`. Source-links the relevant pure-logic files instead of taking
  a `ProjectReference` on `NodeKit.csproj`, so it carries none of that
  project's Avalonia/Grpc.Net.Client/Google.Protobuf/Wasmtime/ReactiveUI
  dependencies — confirmed by inspecting the published `bin/` output.
- `tests/NodeKit.Cli.Tests/` — validate/render success and failure paths,
  including the fail-closed "no output file on validation failure" behavior.

**Still not started:**

- `nodekit submit` in any form — not even a hard-fail stub. A stub command
  risks reading as "almost ready"; the cleanest signal that submission isn't
  available yet is for the command to simply not exist.

`L1-DOCKER-008`/`009`'s pinning check was later widened to cover every `FROM`
instruction (not just the first), and a new `L1-IMG-006` cross-check
(`ImageUri` vs. the Dockerfile's first `FROM`) was added — that change is
owned by `docs/NODEKIT_IMAGEURI_SEMANTICS_REPORT.md`, not this draft, and is
already implemented (see Section 7, item 6). `L1-DOCKER-010` (ARG/ENV
variable reference in COPY/ADD source) remains untouched and out of scope
here.

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
6. ~~`ImageUri` semantics for non-BioContainer variants.~~ **Resolved, scope
   restricted**, see
   [`docs/NODEKIT_IMAGEURI_SEMANTICS_REPORT.md`](NODEKIT_IMAGEURI_SEMANTICS_REPORT.md):
   for **NodeKit authoring-time `ToolDefinition.ImageUri` and the current
   legacy `BuildRequest.ImageUri`**, `ImageUri` must denote the same pinned,
   already-existing input image as the Dockerfile's `FROM` line —
   `ImageUriValidator` requiring a digest *before* any build runs makes this
   the only consistent reading (a not-yet-built result has no digest yet).
   `RecipeRenderer` reusing `BaseImage` / `BioContainerImageUri` for
   `ImageUri` was therefore already correct, not a placeholder. **Not
   resolved here**: post-build `RegisterToolRequest.image_uri` /
   `RegisteredToolDefinition.image_uri` semantics are not redefined by this
   decision — those may denote the registered output image reference, which
   is NodeVault's call. Separate, deliberately unstarted follow-up:
   `DockerfileStructureValidator` never cross-checks its `FROM` line against
   `ToolDefinition.ImageUri`, so the two can silently diverge today (candidate
   rule `L1-IMG-006`); it also only validates `instructions[0]`, so any `FROM`
   in a multistage Dockerfile beyond the first is never checked at all. The
   multistage policy question is now decided (see
   [`docs/NODEKIT_IMAGEURI_SEMANTICS_REPORT.md`](NODEKIT_IMAGEURI_SEMANTICS_REPORT.md)
   §4–§6): multistage Dockerfiles are allowed, but reproducibility rules apply
   uniformly to every `FROM` instruction — no builder-stage exception. The
   validator change itself (widening `L1-DOCKER-008`/`009`'s scope from the
   first instruction to all `FROM` instructions, plus the new `L1-IMG-006`
   cross-check) is implemented and tested (Section 6).
7. **New, still open:** none of the six variants' rendered Dockerfiles have
   been run through an actual `docker build` — only through NodeKit's L1
   static validators. The shell syntax (`curl`/`sha256sum -c`/`conda
   config`/etc.) is reasonable but unverified against a real builder.
8. ~~**build string 강제 여부 / recipe variant별 artifact 사전 결정 방법.**~~
   **Resolved (2026-06-28)**: Dockerfile fallback(사용자 직접 작성)과 source
   build(checksum 고정)를 제외한 4개 variant(conda, micromamba, package mirror,
   BioContainer)는 BuildRequest 생성 전 NodeVault `ResolveRecipe` RPC로 사전 조회한다.
   Harbor 명중 시 후보 1개 자동 선택, 외부 소스 조회 시 복수 후보를 NodeKit이 표시하고
   사용자가 선택한다. 폐쇄망에서 Harbor 미존재 시 `InvalidArgument`.
   NodeKit L1은 버전 고정(`=version`)만 요구하며 build string 강제는 하지 않는다
   (PLATFORM_MASTER_DESIGN.md §4.9 참조).

## 8. 한국어 요약

**Superseded (2026-07-19)**: 아래 두 단락(당시 상태 요약, "현재 경로 vs 미래
경로")은 2026-06-22 시점 기록이며 지금은 맞지 않다. `nodekit submit`은
이미 존재하고(§0 상단 배너, `docs/NODEKIT_CLI_USAGE.md` §3 참조) CLI/GUI
모두 ToolSpec 경로(`ResolveToolSpec → SubmitToolBuild → WatchToolBuild`)만
사용한다. `BuildAndRegister`/`IBuildClient`/`GrpcBuildClient`는 저장소
어디에도 남아있지 않다. 아래는 원문 그대로 보존.

이 문서는 **구현됨** 상태다. 아래에서 설명하는 recipe 모델, recipe-level
validator, renderer는 실제 코드로 존재하며 (`src/Authoring/Recipes/`,
`src/Validation/Recipes/`, `tests/NodeKit.Tests/Recipes/`) 테스트로
커버된다. CLI 실행 파일(`src/NodeKit.Cli/`)도 만들었다 — `nodekit validate
<recipe.json>` / `nodekit render <recipe.json> --out <build-request.json>`
두 명령만 제공하며 (5절), `NodeKit.sln`에 등록되어 있고 `tests/NodeKit.Cli.Tests/`로
커버된다 (6절). `nodekit submit`은 여전히 존재하지 않는다.

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

**`ImageUri` 의미는 해결됨 (범위 한정)**: 별도 보고서
([`docs/NODEKIT_IMAGEURI_SEMANTICS_REPORT.md`](NODEKIT_IMAGEURI_SEMANTICS_REPORT.md))
에서 확정 — 이 보고서는 **NodeKit authoring 단계의 `ToolDefinition.ImageUri`와
현재 legacy `BuildRequest.ImageUri` 의미만 확정한다.** `ImageUri`는
Dockerfile `FROM`과 같은, 빌드 전 이미 존재하는 pinned input 이미지를
가리켜야 한다(`ImageUriValidator`가 빌드 전 시점에 digest를 강제하므로,
아직 만들어지지 않은 결과물을 가리킬 수 없다는 게 근거). `RecipeRenderer`가
`BaseImage`/`BioContainerImageUri`를 그대로 `ImageUri`에 채우는 동작은
placeholder가 아니라 처음부터 맞는 동작이었다. **여기서 재정의하지 않는
것**: 빌드 후 단계의 `RegisterToolRequest.image_uri` /
`RegisteredToolDefinition.image_uri`는 최종 등록 이미지 ref를 의미할 수
있고, 이는 NodeVault의 결정 영역이라 이 보고서가 단정하지 않는다. 별도
후속 작업: `DockerfileStructureValidator`가 `FROM` 라인을 `ImageUri`와
대조하지 않아서 둘이 달라도 지금은 통과하는 gap이 남아있고(후보 규칙
`L1-IMG-006`), 첫 번째 instruction만 검사해서 멀티스테이지의 두 번째 이후
`FROM`은 전혀 검사되지 않는 gap도 있다. 멀티스테이지 처리 방침은 결정됨
([`docs/NODEKIT_IMAGEURI_SEMANTICS_REPORT.md`](NODEKIT_IMAGEURI_SEMANTICS_REPORT.md)
4~6절 참고): 멀티스테이지를 허용하되 builder stage 예외 없이 모든 `FROM`에
재현성 규칙(latest 금지, digest 필수)을 동일하게 적용한다. validator 코드
변경(`L1-DOCKER-008`/`009`의 검사 범위를 첫 instruction에서 모든 `FROM`으로
확장 + 신규 `L1-IMG-006` cross-check 추가)은 실제로 구현했고 테스트도
추가했다 (6절).

**여전히 남은 질문**: 6개 variant가 생성하는 Dockerfile 전부 NodeKit L1
정적 검증만 통과했을 뿐, 실제 `docker build`로 검증된 적은 없다.
