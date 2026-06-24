# NodeKit Legacy-First Sprint Plan

Status: Active Planning  
Created: 2026-06-17  
Updated: 2026-06-24  
Scope: NodeKit work before NodeVault Phase 1 / PLATFORM_SCHEDULE.md Phase 6

## 0. Resume Note For Agents

Read this document first when resuming NodeKit work.

Current instruction from the NodeVault development boundary:

```text
NodeKit must not implement the new ToolSpecRequest path yet.
Keep the current BuildRequest / BuildAndRegister legacy gRPC path working.
NodeKit migrates only after NodeVault Phase 1 is complete:
  - ResolveToolSpec canonical implementation
  - SubmitToolBuild API
Follow PLATFORM_SCHEDULE.md Phase 6 order.
```

This means the immediate NodeKit focus is:

```text
legacy BuildRequest path stability
+ L1 validation quality
+ CI / lint / test / coverage discipline
```

Do not add a production `ToolSpecRequest`, `ResolveToolSpec`, or `SubmitToolBuild` client path in NodeKit until NodeVault exposes and stabilizes the corresponding APIs.

## 1. Current Boundary

NodeKit currently owns:

```text
- user-facing authoring in the existing C# / Avalonia app
- local L1 validation before submission
- BuildRequest creation
- BuildAndRegister gRPC client behavior
- local policy/check feedback quality
```

NodeKit does not yet own:

```text
- ToolSpecRequest production submission
- ResolveToolSpec production integration
- SubmitToolBuild integration
- canonical digest calculation
- ResolvedToolSpec generation
- image build
- certification / promotion
- NodeVault Index mutation
```

Future direction remains CLI-first and reactive, but implementation waits for the platform phase that makes the new API real.

## 2. Reactive Implementation Policy

Reactive means event/state oriented C# implementation, not the React JavaScript framework.

When new NodeKit application state is introduced, prefer the same library direction used by DagEdit:

```text
- ReactiveUI
- System.Reactive
- DynamicData where collection change streams are useful
```

For the current legacy phase, use this carefully:

```text
- Do not rewrite the UI just to introduce reactive libraries.
- Prefer focused state objects around validation, submission progress, and result reporting.
- Keep pure validation and serialization code plain and deterministic.
- Do not duplicate validation rules between UI handlers and service classes.
```

## 3. CI, Lint, And Coverage Gate

NodeKit should follow the DagEdit operational bar:

```text
All GitHub Actions CI must be green.
Lint/analyzer checks must be green.
No warning regression is allowed.
NuGet dependency graph must be locked and reproducible.
Known vulnerable packages fail CI.
Security workflows must cover dependency review and CodeQL.
Coverage must not fall below the committed baseline gate.
```

Coverage policy for this phase:

```text
- L1 validation, BuildRequest mapping, and gRPC client mapping should target 90%+ focused coverage.
- Existing UI rendering code is not the first coverage target.
- Overall repository coverage can be lower while legacy UI remains, but changed non-UI logic must be covered.
- Coverage must not decrease for the touched validation/mapping/client areas after the baseline is established.
```

Use Microsoft.Testing.Platform coverage through the xUnit v3 test project:

```bash
dotnet test --solution NodeKit.sln --coverage --coverage-output coverage.cobertura.xml --coverage-output-format cobertura
```

## 4. Sprint Schedule

### Sprint 0. Baseline And Guardrails

Goal:

```text
Make the current legacy path measurable and keep it green.
```

Tasks:

```text
1. Confirm current BuildRequest / BuildAndRegister path compiles and tests pass.
2. Add or repair GitHub Actions verify workflow if missing.
3. Establish local commands for restore, build, test, coverage.
4. Document that ToolSpecRequest migration is blocked on NodeVault Phase 1.
5. Identify current analyzer warnings and decide a no-regression approach.
```

Done when:

```text
- dotnet build NodeKit.sln succeeds.
- dotnet test NodeKit.sln succeeds.
- Coverage artifact is generated.
- Sprint document clearly blocks new ToolSpecRequest path until NodeVault Phase 1.
```

### Sprint 1. L1 Validation Hardening

Goal:

```text
Improve user-side validation without changing the NodeVault API boundary.
```

Tasks:

```text
1. Inventory current L1 validation rules.
2. Add missing required-field validation for existing ToolDefinition inputs.
3. Add validation around Dockerfile/build context fields currently mapped into BuildRequest.
4. Ensure validation messages are actionable and stable.
5. Add focused tests for pass/fail validation cases.
```

Done when:

```text
- Invalid authoring input is rejected before BuildAndRegister.
- Tests cover validation success and failure cases.
- No production ToolSpecRequest path exists.
```

Progress:

```text
- 2026-06-18: validation execution/state moved from MainWindow code-behind into
  UI/ViewModels/ValidationViewModel using ReactiveUI ReactiveObject.
- MainWindow still owns legacy form collection and visual panel updates; new
  validation state should continue moving into reactive ViewModel code.
- 2026-06-18: RequiredFieldsValidator now rejects missing version, missing I/O
  role/format values, invalid I/O shape/class values, and empty command entries
  before legacy BuildAndRegister submission.
- 2026-06-18: DockerfileStructureValidator added for first-instruction FROM,
  dangling line continuation, COPY/ADD arity, build context escape, and remote
  ADD checks before legacy BuildAndRegister submission.
```

### Sprint 2. BuildRequest Mapping Quality

Goal:

```text
Make ToolDefinition -> BuildRequest mapping explicit and well tested.
```

Tasks:

```text
1. Review BuildRequestFactory mapping field by field.
2. Add tests for every field NodeVault currently expects.
3. Confirm environment/spec/context fields are not silently dropped.
4. Keep proto compatibility with the current legacy NodeVault endpoint.
```

Done when:

```text
- BuildRequestFactory has focused coverage.
- Serialization/mapping regressions fail tests.
- BuildAndRegister remains the only production build submission path.
```

### Sprint 3. gRPC Client Resilience

Goal:

```text
Improve BuildAndRegister client behavior and diagnostics.
```

Tasks:

```text
1. Review GrpcBuildClient timeout/cancellation behavior.
2. Improve error messages for connection, auth, and stream failures.
3. Add tests around mapping from gRPC stream events to NodeKit state.
4. Keep live NodeVault integration opt-in.
```

Done when:

```text
- Cancellation and failure paths are deterministic.
- Local tests do not require a live NodeVault.
- Remote NodeVault assumptions are not hard-coded.
```

### Sprint 4. Remote Environment Readiness

Goal:

```text
Prepare for NodeVault running as a remote Kubernetes data-plane app.
```

Tasks:

```text
1. Document that live connection details must be discovered from ~/.config/infra-lab.
2. Add opt-in integration-test configuration.
3. Do not assume NodeVault is localhost.
4. Do not require Kubernetes API access from NodeKit.
5. Treat NodeVault and related platform services as remote-infrastructure-tested by default.
```

Done when:

```text
- Live integration is configurable and skipped by default.
- NodeKit remains only a gRPC client.
```

### Sprint 5. Legacy UX Quality

Goal:

```text
Improve the existing authoring experience without changing the protocol.
```

Tasks:

```text
1. Surface L1 validation results clearly.
2. Prevent submission while local validation is failing.
3. Improve build event/progress rendering.
4. Keep UI changes small and covered where logic is extracted.
```

Done when:

```text
- The current UI sends fewer malformed BuildRequests.
- Build progress/error output is easier to diagnose.
```

### Sprint 6. ToolSpecRequest Migration Gate

Goal:

```text
Start migration only after NodeVault Phase 1 is actually complete.
```

Entry criteria:

```text
- NodeVault has canonical ResolveToolSpec implementation.
- NodeVault has SubmitToolBuild API.
- PLATFORM_SCHEDULE.md Phase 6 has begun.
- NodeVault proto/API is stable enough to vendor.
```

Tasks after entry criteria are met:

```text
1. Vendor the stable NodeVault proto.
2. Add ToolSpecRequest authoring models.
3. Add CLI-first path if still planned.
4. Add ResolveToolSpec client.
5. Add SubmitToolBuild client.
6. Keep legacy BuildRequest path until migration is proven.
```

Done when:

```text
- New path is explicitly enabled by platform phase.
- Legacy path remains available during migration.
```

### Sprint 7. Post-Migration Hardening

Goal:

```text
Make the new path reliable enough to replace legacy usage.
```

Tasks:

```text
1. Compare legacy and new behavior on representative tools.
2. Add migration documentation.
3. Keep runtime image selection authority in NodeVault Certified*Record.
4. Remove or deprecate legacy only after usage reaches zero and an ADR approves it.
```

Done when:

```text
- New path is covered by CI/lint/test/coverage.
- Legacy removal has an explicit ADR, not an implicit refactor.
```

## 5. Immediate First Slice

Start here:

```text
1. Do not add new ToolSpecRequest production code.
2. Verify existing BuildRequest / BuildAndRegister build and tests.
3. Add or repair CI workflow.
4. Improve L1 validation tests.
5. Improve BuildRequestFactory tests.
6. Add coverage collection and document the baseline.
```

## 6. Baseline Snapshot

Captured: 2026-06-18

NodeVault observation:

```text
- NodeVault is the upstream Kubernetes data-plane app.
- Current proto still exposes BuildService.BuildAndRegister(BuildRequest).
- Current proto also exposes BuildService.ResolveToolSpec(ToolSpecRequest).
- SubmitToolBuild / WatchToolBuild / CancelToolBuild are not present yet.
- Therefore NodeKit must keep the legacy BuildRequest / BuildAndRegister path.
```

Local CI-equivalent commands:

```bash
dotnet restore NodeKit.sln --locked-mode
./scripts/ci-audit-packages.sh
dotnet format NodeKit.sln --no-restore --verify-no-changes --verbosity minimal
dotnet build NodeKit.sln --no-restore --configuration Release /p:TreatWarningsAsErrors=true /p:EnforceCodeStyleInBuild=true
dotnet test --solution NodeKit.sln --no-build --configuration Release --results-directory TestResults --coverage --coverage-output coverage.cobertura.xml --coverage-output-format cobertura
./scripts/ci-check-coverage.sh
```

Latest local result:

```text
- Locked restore: pass
- Package audit: pass
- Format: pass
- Build: pass, 0 warnings, 0 errors
- Tests: pass, 82 passed, 0 failed, 0 skipped
- Coverage threshold: pass, line >= 0.1400 and branch >= 0.0900
- Coverage artifact generated under TestResults/coverage.cobertura.xml
```

CI workflow:

```text
.github/workflows/verify.yml runs locked restore, NuGet package audit, format,
warnings-as-errors build, coverage test, and coverage threshold on main branch
pushes / pull requests.
.github/workflows/dependency-review.yml blocks vulnerable or denied-license
dependency changes on pull requests.
.github/workflows/codeql.yml runs CodeQL C# security-and-quality analysis on
pushes, pull requests, and a weekly schedule.
```

## 7. Non-Goals Until NodeVault Phase 1 Completes

```text
- No production ToolSpecRequest path.
- No ResolveToolSpec client path.
- No SubmitToolBuild client path.
- No local authoritative canonical digest calculation.
- No NodeKit image build logic.
- No Kubernetes API calls from NodeKit.
- No rootless/Buildah handling in NodeKit.
- No full UI rewrite.
```

## 8. Handoff Note

The correct instruction for a NodeKit agent is:

```text
NodeKit은 지금 당장 새 ToolSpecRequest 경로를 구현하지 말 것.
현재 BuildRequest / BuildAndRegister legacy gRPC 경로를 유지하고 동작하게 두는 것이 맞다.
NodeVault Phase 1 (ResolveToolSpec canonical 구현 + SubmitToolBuild API)이 완료된 후 NodeKit이 migration한다.
PLATFORM_SCHEDULE.md Phase 6 순서대로 진행한다.
```

## 9. Recipe Authoring Boundary And CLI Command Naming (2026-06-21 design intent)

A first-pass draft filling in the `raw_spec` schema per variant, the concrete
CLI command interface, and the `src/NodeKit.Cli/` scope left open below is in
[`docs/NODEKIT_CLI_RECIPE_SPEC_DRAFT.md`](NODEKIT_CLI_RECIPE_SPEC_DRAFT.md) —
marked DRAFT, pending review, not yet adopted.

"NodeKit does not build images" does not mean "NodeKit does not choose what
recipe a tool uses." This section records a design decision about what NodeKit
is allowed to own. It does not move the Sprint 6 entry criteria in Section 4.

Boundary, restated:

```text
NodeKit owns:
- recipe variant selection (the user's choice of how a tool gets built)
- collecting the inputs that variant needs (e.g. a Dockerfile, a conda env spec)
- normalizing/validating that input as a raw_spec payload
- rendering a ToolSpecRequest / BuildRequest from it
- L1 validation
- exporting the rendered request to a file
- submitting the request once Sprint 6 entry criteria are met

NodeKit does not own:
- executing docker / buildah / buildkit build
- pushing to a registry
- recording a canonical image digest
- ResolvedToolSpec generation
- NodeVault index/catalog mutation
```

This matches the live NodeVault contract: `ToolSpecRequest.raw_spec`
(`protos/nodevault/v1/nodevault.proto`) is an opaque string. NodeVault's
`pkg/resolve` only computes digests from it and recognizes
`base_image`/`base_image_uri`/`image_uri` keys for pin checking. No
recipe-variant schema exists on the NodeVault side — defining the variant list
is NodeKit authoring-layer scope.

Recipe variants under consideration:

```text
1. conda / bioconda / conda-forge
2. micromamba
3. existing BioContainer
4. source build
5. local package mirror
6. Dockerfile fallback
```

Command naming, chosen to avoid implying NodeKit executes builds or owns
NodeVault-side resolve:

```text
nodekit recipe select   - choose a recipe variant
nodekit spec render     - render a ToolSpecRequest / BuildRequest from collected input
nodekit request export  - write the rendered request to a file (no network call)
nodekit validate        - run L1 validation
nodekit submit          - submit to NodeVault (Sprint 6 gate only)
```

Avoid `nodekit build`: it reads as "NodeKit builds the image locally," which is
the one thing this layer must never do. If a `build` alias ships later for UX
reasons, its documented meaning must be "submit a build request," not "build
locally."

What can start now vs. what stays gated:

```text
Can start now (touches nothing in NodeVault):
- recipe variant selection UX/CLI scaffolding
- collecting Dockerfile / conda spec / etc. input per variant
- L1 validation of that input
- request export to a local file

Stays gated behind Sprint 6 entry criteria (Section 4):
- nodekit submit, or any network call that sends ToolSpecRequest to NodeVault
- any client-side ResolveToolSpec or SubmitToolBuild call
```

### 9.1 한국어 요약

"NodeKit이 이미지를 직접 빌드하지 않는다"는 말이 "NodeKit이 빌드 레시피 선택도
하지 않는다"는 뜻은 아니다. 이 섹션은 NodeKit이 가질 수 있는 권한 범위에 대한
설계 결정을 기록한 것이며, 4번 섹션의 Sprint 6 진입 조건 자체를 바꾸지 않는다.

경계 재정리:

```text
NodeKit이 담당하는 영역:
- recipe variant 선택 (사용자가 어떤 방식으로 도구를 빌드할지 고르는 것)
- 해당 variant에 필요한 입력 수집 (예: Dockerfile, conda env spec)
- 그 입력을 raw_spec payload로 정규화/검증
- 이를 바탕으로 ToolSpecRequest / BuildRequest 렌더링
- L1 validation
- 렌더링된 request를 파일로 export
- Sprint 6 진입 조건이 충족된 후 request 제출(submit)

NodeKit이 담당하지 않는 영역:
- docker / buildah / buildkit build 실행
- registry push
- canonical image digest 기록
- ResolvedToolSpec 생성
- NodeVault index/catalog 변경
```

이는 실제 NodeVault 계약과 일치한다: `ToolSpecRequest.raw_spec`
(`protos/nodevault/v1/nodevault.proto`)은 opaque한 문자열이고, NodeVault의
`pkg/resolve`는 거기서 digest만 계산하며 `base_image`/`base_image_uri`/
`image_uri` 키만 pin 여부 확인용으로 인식한다. NodeVault 쪽에는 recipe-variant
스키마가 존재하지 않으므로, variant 목록을 정의하는 것은 NodeKit authoring
레이어의 범위다.

검토 중인 recipe variant:

```text
1. conda / bioconda / conda-forge
2. micromamba
3. existing BioContainer
4. source build
5. local package mirror
6. Dockerfile fallback
```

명령 이름 (NodeKit이 빌드를 실행하거나 NodeVault의 resolve 권한을 가진 것처럼
보이지 않도록 선택):

```text
nodekit recipe select   - recipe variant 선택
nodekit spec render     - 수집한 입력으로 ToolSpecRequest / BuildRequest 렌더링
nodekit request export  - 렌더링된 request를 파일로 저장 (네트워크 호출 없음)
nodekit validate        - L1 검증 실행
nodekit submit          - NodeVault에 제출 (Sprint 6 게이트 이후에만)
```

`nodekit build`는 피한다: "NodeKit이 로컬에서 이미지를 빌드한다"로 읽혀, 이 계층이
절대 해서는 안 되는 바로 그 일을 암시하기 때문이다. 추후 UX상 `build`라는
별칭(alias)을 쓰더라도, 그 의미는 "빌드 request를 제출한다"여야 하며 "로컬에서
빌드한다"가 되어서는 안 된다.

지금 시작 가능한 것 vs. 게이트가 풀려야 하는 것:

```text
지금 시작 가능 (NodeVault를 전혀 건드리지 않음):
- recipe variant 선택 UX/CLI 골격
- variant별 Dockerfile / conda spec 등 입력 수집
- 그 입력에 대한 L1 검증
- request를 로컬 파일로 export

Sprint 6 진입 조건(4번 섹션)이 충족되기 전까지 보류:
- nodekit submit, 또는 ToolSpecRequest를 NodeVault로 보내는 모든 네트워크 호출
- 클라이언트 측 ResolveToolSpec / SubmitToolBuild 호출
```

## 10. Recipe Authoring Session Implementation Sprints (2026-06-24)

Design source:
[`docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md`](NODEKIT_CLI_RECIPE_AUTHORING_UX_BEGINNER_DESIGN.md)
v0.8 (§32 implementation order, §33 v1 scope, §19.3
authoring-requirement-vs-L1-policy boundary).

This track is the "Can start now" half of Section 9 — it does not move the
Sprint 6 entry criteria and does not touch NodeVault. It is independent of
the BuildRequest/ToolSpecRequest migration gate.

Today's shipped state, for calibration: `src/Authoring/Recipes/` already has
`RecipeDocument`, `RecipeRenderer`, `RecipeVariant`; `src/Validation/Recipes/`
already has `RecipeValidator`; `src/NodeKit.Cli/CliApp.cs` already has
`recipe validate` / `recipe render`. None of `RecipeMethodId`,
`RecipeFieldRequirement`, `RecipeFieldCatalog`, `RecipeMethodCatalog`,
`RecipeMethodRecommender`, `RecipeBuildKindResolver`, or
`RecipeAuthoringSession` exist yet — Sprints R0-R7 are additive on top of the
existing legacy `validate`/`render` path, not a replacement of it.

### Sprint R0. RecipeVariant -> RecipeBuildKind Rename

Goal:

```text
Apply the already-decided rename in code before any new type is added.
```

Tasks:

```text
1. Rename RecipeVariant to RecipeBuildKind across src/Authoring/Recipes,
   src/Validation/Recipes, and all referencing tests.
2. Touch nothing else in the same commit.
```

Done when:

```text
- Build succeeds with 0 new warnings.
- Existing tests pass unchanged (mechanical rename; no new tests required
  per Section 9 of CLAUDE.md's validation-responsibility table).
```

### Sprint R1. Method And Field-Requirement Type Skeleton

Goal:

```text
Add the two foundational enums the rest of the design depends on.
```

Tasks:

```text
1. Add RecipeMethodId (Container, Package, Mirror, Source, Dockerfile).
2. Add RecipeFieldRequirement (Required, Defaulted, Optional, Recommended).
```

Done when:

```text
- Both enums compile; neither is referenced by existing code yet.
- No behavior change to the legacy validate/render path.
```

### Sprint R2. Field Catalog And Shared Validation Pipeline

Goal:

```text
Establish the field-requirement data model and a single validation entry
point shared by validate, render, and the future recipe create.
```

Tasks:

```text
1. Implement RecipeFieldCatalog keyed by RecipeMethodId + RecipeFieldRequirement.
2. Implement the field composition contract: common scalar fields + method
   fields + Inputs/Outputs (Inputs/Outputs are Required for every method).
3. Extract RecipeValidationPipeline as the single L1 gate shared by
   validate/render today and recipe create later.
4. Apply the v0.8 rule directly: any field CLAUDE.md Section 3 blocks at L1
   (unpinned tag, missing digest, unpinned package version) must be
   RecipeFieldRequirement.Required, never Recommended/Optional.
```

Done when:

```text
- RecipeFieldCatalog tests cover FieldsFor ordering and the Inputs/Outputs
  always-Required invariant.
- Existing validate/render tests still pass after the pipeline extraction.
```

### Sprint R3. Method Recommendation Engine

Goal:

```text
Implement the beginner-facing method recommender.
```

Tasks:

```text
1. Implement RecipeMethodCatalog (per-method description/prep/warning text).
2. Implement RecipeMethodQuestionCatalog (fixed question order).
3. Implement RecipeMethodRecommender: the IsRestrictedNetwork top-level gate,
   the priority table, and the Answer tri-state (Yes/No/Unknown) where
   Unknown is neither evidence for nor against a method.
```

Done when:

```text
- All recommender test scenarios from the design's test plan pass, including
  internal-network Yes/Unknown/No combinations and Unknown-heavy answers.
```

### Sprint R4. Build-Kind Resolution, Presets, Normalization

Goal:

```text
Wire method selection to the existing RecipeBuildKind model and add the
input/output convenience layer.
```

Tasks:

```text
1. Implement RecipeBuildKindResolver.Resolve(RecipeMethodId, RecipeDocument),
   guarded to throw if called before Defaulted fields are applied (e.g.
   PackageEngine empty for RecipeMethodId.Package).
2. Implement InputOutputPresetCatalog (FASTQ/BAM/VCF presets).
3. Implement format/role/channel normalization (.gz suffix detection,
   snake_case, choice-first selection).
```

Done when:

```text
- Resolver tests cover both the pre-Build() guard violation and the
  Package -> Conda/Micromamba branch.
- Preset/normalization tests match the design's worked examples.
```

### Sprint R5. RecipeAuthoringSession Core

Goal:

```text
Implement the authoring session state machine and its Snapshot/Build/
ValidateDraft contracts.
```

Tasks:

```text
1. Implement RecipeAuthoringSession: SelectMethod, NextField, SetField,
   AppendListItem, CompleteListField, SkipOptionalField, IsComplete.
2. Implement Snapshot() (works on incomplete sessions, never applies
   Defaulted values, never validates) and Build() (requires IsComplete,
   throws otherwise, sole place Defaulted values are applied).
3. Implement ValidateDraft() as authoring-level only: it must never call
   RecipeBuildKindResolver, RecipeRenderer, or the L1 validator chain.
```

Done when:

```text
- A guard test proves Build() before IsComplete throws.
- A guard test proves ValidateDraft() does not invoke the resolver, renderer,
  or L1 chain even when the draft happens to be field-complete.
- A test proves the Section 19.3 invariant end to end: a recipe that
  RecipeAuthoringSession produces does not fail nodekit validate afterward
  (in particular for the Required ImageDigest field, see the v0.8 patch).
```

### Sprint R6. ChangeMethod, Recovery, List Editing

Goal:

```text
Implement the parts of the session that handle revision and recovery, not
just forward progress.
```

Tasks:

```text
1. Implement PreviewMethodChange / ChangeMethod: atomically discard
   incompatible method-specific fields, reset method-specific session
   metadata (DockerfileWarningAccepted, ImageTagWarningShown/Accepted), and
   mark shared Inputs/Outputs fields invalidated rather than discarding them.
2. Implement the invalidated-field lifecycle (mark on ChangeMethod, clear on
   re-confirm / edit / preset-reselect / discard).
3. Implement RecipeValidationRecoveryPlan / RecoveryActionKind, built from
   final-validation failures while keeping the session alive.
4. Implement per-item list field editing (edit/add/delete on an existing
   list field, not just append).
```

Done when:

```text
- ChangeMethod tests cover field discard, metadata reset, and the
  invalidated (not discarded) treatment of shared fields.
- RecoveryPlan tests cover at least one Required-field failure and one L1
  policy failure (e.g. unpinned digest) producing distinct recovery actions.
```

### Sprint R7. CLI Wiring, Non-Interactive Mode, Golden Tests

Goal:

```text
Connect the session to a real CLI command and lock in the beginner scenarios.
```

Tasks:

```text
1. Wire nodekit recipe create to RecipeAuthoringSession (interactive wizard).
2. Add non-interactive flags (--method, --engine,
   --accept-dockerfile-warning, etc.) covering the same field set.
3. Add golden transcript tests for the design's beginner scenarios.
4. Keep nodekit validate / nodekit render as-is; recipe create only adds an
   authoring front end that ends in the same RecipeValidationPipeline call.
```

Done when:

```text
- Interactive and non-interactive paths produce identical RecipeDocument
  output for the same logical answers.
- Golden transcript tests pass and are reviewed as fixtures, not snapshots
  that auto-update.
```

### 10.1 Sequencing Note

```text
R0 and R1 are mechanical and can be one sitting each.
R2 is required before R3-R6 (field catalog underlies everything).
R3 and R4 can be reordered relative to each other but both must precede R5.
R5 is the highest-risk sprint: the Snapshot/Build/ValidateDraft contract is
where a quiet regression would silently reopen the ImageDigest-class
conflict this plan exists to prevent (see Section 19.3 of the design doc).
R6 depends on R5. R7 depends on everything above being stable; it is an
integration sprint, not a place to discover new design questions.
There is little parallelism available — the dependency chain is mostly linear.
```

### 10.2 한국어 요약

```text
이 트랙(R0-R7)은 9번 섹션의 "지금 시작 가능" 절반에 해당하며, Sprint 6
진입 조건을 옮기지 않고 NodeVault를 건드리지 않는다.

R0: RecipeVariant -> RecipeBuildKind 리네임 (기계적, 단독 커밋)
R1: RecipeMethodId / RecipeFieldRequirement 타입 골격
R2: RecipeFieldCatalog + RecipeValidationPipeline 공유화
    (CLAUDE.md 3번 섹션 L1 하드룰 대상 필드는 전부 Required로)
R3: RecipeMethodCatalog / RecipeMethodQuestionCatalog / RecipeMethodRecommender
R4: RecipeBuildKindResolver + InputOutputPresetCatalog + 정규화
R5: RecipeAuthoringSession 본체 (Snapshot/Build/ValidateDraft 계약) —
    위험도 가장 높음. ImageDigest류 충돌이 조용히 재발하지 않는지
    end-to-end로 검증해야 함 (설계 문서 19.3절).
R6: ChangeMethod / invalidated field / RecoveryPlan / 리스트 편집
R7: CLI 와이어링(recipe create) + non-interactive 모드 + golden transcript 테스트

순서는 거의 선형이다: R0,R1 -> R2 -> (R3,R4 순서 교환 가능) -> R5 -> R6 -> R7.
```
