# NodeKit CLI / Recipe Spec — DRAFT (2026-06-21)

> **Status: DRAFT, not adopted.** This is a first pass at the three gaps left
> open by `docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md` Section 9: a `raw_spec`
> schema per recipe variant, a concrete CLI command interface, and the scope
> of the still-empty `src/NodeKit.Cli/` placeholder. Nothing here changes the
> Sprint 6 entry gate or the boundary already recorded in Section 9 — this
> only fills in the detail under it. Treat every section as a proposal to be
> revised, not a decision.

## 1. Design principle

`raw_spec` is opaque to NodeVault — it only computes digests over it and
recognizes `base_image`/`base_image_uri`/`image_uri` keys. The schema below is
entirely NodeKit's to define. It does not introduce new fields where existing
`ToolDefinition` fields already cover the need; most recipe variants are a
thin reinterpretation of `ImageUri`, `EnvironmentSpec`, and `DockerfileContent`
that already exist and are already validated by
`ImageUriValidator`/`PackageVersionValidator`/`DockerfileStructureValidator`.
The new thing is a `recipe_variant` discriminator and a `variant_payload` that
each variant renders into those existing fields before validation runs.

## 2. Common envelope

```json
{
  "schema_version": "draft-1",
  "recipe_variant": "conda",
  "tool_name": "bwa-mem",
  "version": "0.7.17",
  "base_image": "condaforge/miniforge3:24.3.0-0@sha256:<64-hex>",
  "variant_payload": { },
  "script": "bwa mem -t 4 ref.fa reads_1.fq reads_2.fq",
  "command": [],
  "inputs": [],
  "outputs": [],
  "display": {
    "label": "",
    "description": "",
    "category": "",
    "tags": []
  }
}
```

`base_image`, `script`, `command`, `inputs`, `outputs`, `display` map 1:1 onto
existing `ToolDefinition` fields. `recipe_variant` and `variant_payload` are
new and decide how `EnvironmentSpec`/`DockerfileContent`/`ImageUri` get filled
in at render time (Section 4).

## 3. Recipe variants

```text
1. conda / bioconda / conda-forge
2. micromamba
3. existing BioContainer
4. source build
5. local package mirror
6. Dockerfile fallback
```

## 4. Per-variant payload and render target

### 4.1 conda / bioconda / conda-forge

```json
{
  "channels": ["bioconda", "conda-forge"],
  "packages": ["bwa=0.7.17=h5bf99c6_8"]
}
```

Renders into `EnvironmentSpec` as a conda `environment.yml`. Already covered
by `PackageVersionValidator.ValidateConda` (`L1-PKG-001`/`L1-PKG-002`) — no new
validator needed. `base_image` is required and goes through
`ImageUriValidator` unchanged.

### 4.2 micromamba

Same payload shape as 4.1:

```json
{
  "channels": ["bioconda", "conda-forge"],
  "packages": ["bwa=0.7.17=h5bf99c6_8"]
}
```

Renders into a Dockerfile `RUN micromamba install ...` line instead of (or in
addition to) `EnvironmentSpec`. Already covered:
`PackageVersionValidator.IsCondaInstallCommand` already recognizes both
`conda install` and `micromamba install` tokens. No new validator needed.

### 4.3 existing BioContainer

```json
{
  "image_uri": "quay.io/biocontainers/bwa:0.7.17--h5bf99c6_8@sha256:<64-hex>"
}
```

Renders directly into `ToolDefinition.ImageUri`; no `DockerfileContent` or
`EnvironmentSpec` is produced because the referenced image already contains
the tool. Already covered by `ImageUriValidator` (tag + digest required).

**Open question (NodeVault-side, not NodeKit's to decide):** does registering
a tool from an already-published external image require any build step at
all on NodeVault's side, or only a registry mirror/copy into Harbor? This
draft assumes NodeKit only needs to supply a pinned `image_uri`; what NodeVault
does with it is out of scope here.

### 4.4 source build

```json
{
  "source_uri": "https://github.com/lh3/bwa/archive/refs/tags/v0.7.17.tar.gz",
  "source_checksum": "sha256:<64-hex>",
  "build_commands": ["make", "make install"]
}
```

Renders into `DockerfileContent` (`FROM base_image`, fetch + verify checksum,
run `build_commands`). **Not yet covered: no existing validator checks
`source_checksum` is present or well-formed.** This is the one variant that
needs a new L1 rule before it can ship — proposed `L1-SRC-001` (missing
checksum) / `L1-SRC-002` (malformed checksum, mirroring the `L1-IMG-005`
64-hex-digest check added for image digests). Not implemented in this draft.

### 4.5 local package mirror

```json
{
  "mirror_uri": "https://mirror.internal/conda-channel",
  "packages": ["bwa=0.7.17=h5bf99c6_8"]
}
```

Same render path as 4.1, with `mirror_uri` substituted as the channel URL.
Package pinning validation is identical and already covered; `mirror_uri`
itself is not currently validated (e.g. no check that it's a reachable or
internal-only URL) — left as an open question, not blocking.

### 4.6 Dockerfile fallback

```json
{
  "dockerfile_content": "FROM ...\n..."
}
```

Renders directly into `ToolDefinition.DockerfileContent` — this is exactly
today's existing authoring path, unchanged. Fully covered by
`DockerfileStructureValidator`, `PackageVersionValidator.ValidateDockerfile`,
and `ImageUriValidator` (via the known `L1-DOCKER-009` FROM-line check, which
is already flagged in prior review notes as weaker than `L1-IMG-005` — not
fixed here, out of scope for this draft).

## 5. CLI command interface

All commands read/write local files only, except `submit`. No command prints
secrets; all write deterministic, diffable JSON (one key order, stable
formatting) so rendered artifacts are reviewable in a PR the same way a
Dockerfile is.

```text
nodekit recipe select --variant <id> --tool-name <name> --version <version> --out <recipe.json>
  Writes a recipe.json skeleton for the chosen variant: common envelope
  fields blank/templated, variant_payload pre-shaped per Section 4.
  Exit 0 success. Exit 2 unknown --variant or missing required flags.

nodekit spec render --recipe <recipe.json> --out <toolspec.json>
  Fills variant_payload into EnvironmentSpec/DockerfileContent/ImageUri per
  Section 4 and writes a ToolDefinition-shaped JSON.
  Exit 0 success. Exit 1 recipe.json fails its own schema check (missing
  variant_payload fields). Exit 2 malformed JSON / IO error.

nodekit validate --input <toolspec.json>
  Runs the existing L1 validator chain (RequiredFieldsValidator,
  ImageUriValidator, DockerfileStructureValidator, PackageVersionValidator)
  against the rendered ToolDefinition and prints violations.
  Exit 0 zero violations. Exit 1 one or more violations. Exit 2 IO/parse error.

nodekit request export --input <toolspec.json> --out <build-request.json>
  Re-runs validate internally first and refuses to write the file if it
  fails (fail-closed — an export must never carry an unvalidated definition).
  Maps ToolDefinition -> BuildRequest via the existing BuildRequestFactory.
  No network call.
  Exit 0 success. Exit 1 validation failed, nothing written. Exit 2 IO error.

nodekit submit --input <build-request.json>
  Gated behind Sprint 6 entry criteria (Section 4 of the sprint plan). Until
  the gate opens this command must fail loudly with a clear "not available
  until NodeVault Phase 1 / Sprint 6" message and a distinct exit code — it
  must never silently no-op, mirroring the existing fail-closed precedent for
  gRPC send failures (CLAUDE.md Section 11).
  Exit 0 success (post-gate only). Exit 1 NodeVault rejected the request.
  Exit 2 gate not open yet. Exit 3 transport/network error.
```

File naming convention (not enforced by the CLI, just a suggested default for
`--out`): `recipe.<tool-name>.<version>.json`,
`toolspec.<tool-name>.<version>.json`,
`build-request.<tool-name>.<version>.json` — so the three stages of one tool
stay greppable and diffable side by side in a PR.

## 6. `src/NodeKit.Cli/` scope

**Can start now**, no NodeVault dependency:

- `recipe select`, `spec render`, `validate`, `request export` — thin
  argument-parsing + JSON I/O over the existing `src/Authoring`,
  `src/Validation`, and `src/Grpc` (`BuildRequestFactory`) types. The new
  recipe/variant model types belong in `src/Authoring/Recipes/`, not inside
  `src/NodeKit.Cli/` itself — core logic stays outside the CLI project and
  testable headlessly, consistent with the existing UI/core split.
- Re-including `src/NodeKit.Cli/` in the solution (it is currently excluded
  from `NodeKit.csproj`) is itself a small separate task, not bundled into
  this draft.

**Stays out of scope for now:**

- `submit` beyond the hard-fail stub described in Section 5.
- The `L1-SRC-001`/`L1-SRC-002` checksum validator for the source-build
  variant (Section 4.4) — needs its own review before implementation.
- Any change to `L1-DOCKER-009` or `L1-DOCKER-010` (carried over as open items
  from the prior Low-severity review round, unrelated to this draft).

## 7. Open questions for review

1. Does the BioContainer variant (4.3) need any NodeVault-side build step, or
   only a registry mirror/copy? This is a NodeVault question this draft
   cannot answer from the NodeKit side alone.
2. Should the source-build checksum rule (`L1-SRC-001`/`002`) be designed now
   or deferred until a source-build variant is actually requested?
3. Should `recipe.json` / `toolspec.json` / `build-request.json` stay three
   separate artifacts, or collapse into fewer files for a simpler first cut?
4. Is `recipe_variant` the right discriminator name, or should it track any
   NodeVault-side terminology once one exists?
5. Should the `submit` stub (Section 5) ship in this sprint at all, or should
   the command not exist yet — to avoid implying it's close to working?

## 8. 한국어 요약

이 문서는 **초안(DRAFT)**이며 아직 채택되지 않았다. `NODEKIT_CLI_FIRST_SPRINT_PLAN.md`
9번 섹션에서 남겨둔 세 가지 공백 — recipe variant별 `raw_spec` 스키마, CLI 명령 인터페이스
상세, 비어 있는 `src/NodeKit.Cli/` 골격의 범위 — 에 대한 1차 시도다. Sprint 6 진입 조건이나
9번 섹션에 기록된 경계 자체는 바꾸지 않으며, 그 아래 디테일만 채운다.

**스키마 원칙**: `raw_spec`은 NodeVault에 opaque하므로 스키마는 전적으로 NodeKit이 정의한다.
기존 `ToolDefinition`의 `ImageUri`/`EnvironmentSpec`/`DockerfileContent` 필드가 이미 다루는
영역에는 새 필드를 만들지 않는다. `recipe_variant` 판별자와 `variant_payload`만 새로 추가되고,
렌더링 시점에 이 payload가 기존 필드로 펼쳐진 뒤 기존 validator가 그대로 검증한다.

**Variant별 정리**: conda/bioconda/conda-forge와 micromamba는 `PackageVersionValidator`가
이미 두 설치 명령을 모두 인식하므로 새 validator 불필요. existing BioContainer는
`ImageUriValidator`로 이미 커버되지만, "이미 공개된 이미지를 등록할 때 NodeVault 쪽에 빌드
스텝이 필요한지, 아니면 레지스트리 mirror/copy만으로 충분한지"는 NodeKit이 답할 수 없는
NodeVault 쪽 질문으로 남긴다. source build는 `source_checksum` 필드를 검증하는 validator가
아직 없다 — `L1-IMG-005`와 같은 패턴으로 `L1-SRC-001`/`L1-SRC-002`를 제안하지만 이번 초안에는
구현하지 않았다. local package mirror는 패키지 고정 검증은 동일하게 적용되고 `mirror_uri` 자체
검증은 보류. Dockerfile fallback은 현재 경로 그대로이며 기존 `L1-DOCKER-009` 약점(별도 검토
대상, 이번 초안 범위 아님)을 그대로 안고 간다.

**CLI 명령**: `nodekit recipe select` / `spec render` / `validate` / `request export` /
`submit` 다섯 개. `request export`는 내부적으로 validate를 다시 실행해 실패 시 파일을 쓰지
않는 fail-closed 방식이다. `submit`은 Sprint 6 게이트가 열리기 전까지 "아직 사용 불가"를
명확한 에러와 구분된 종료 코드로 실패해야 하며, 조용히 아무 일도 안 하는 방식은 금지한다 —
CLAUDE.md 11번 섹션의 gRPC 전송 실패 silent failure 금지 원칙과 동일한 이유다.

**`src/NodeKit.Cli/` 범위**: recipe/variant 모델 타입은 CLI 프로젝트가 아니라
`src/Authoring/Recipes/`에 두어, 기존 core/UI 분리 구조를 그대로 따른다. `submit` 실제 구현,
source-build checksum validator, `L1-DOCKER-009`/`010` 수정은 이번 범위 밖이다.

**검토 요청**: 7번 섹션의 5개 질문에 대한 의견을 부탁한다. 특히 BioContainer variant의
NodeVault 쪽 처리 방식과, `submit` stub을 이번 스프린트에 넣을지 여부.
