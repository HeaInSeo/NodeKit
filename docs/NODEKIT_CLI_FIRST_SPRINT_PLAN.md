# NodeKit Legacy-First Sprint Plan

Status: Sprint 6 완료 / Sprint 7 진행 중  
Created: 2026-06-17  
Updated: 2026-07-03  
Scope: NodeKit work — Sprint 0-6 완료, Sprint 7(Post-Migration Hardening) 진행 중

## 0. Resume Note For Agents

Read this document first when resuming NodeKit work.

**Phase 6 완료 (2026-07-02)**: NodeVault Phase 1 gate가 열렸고 NodeKit CLI는 이미
ToolSpec 경로(`ResolveToolSpec → SubmitToolBuild → WatchToolBuild`)로 전환 완료.
`--legacy` 플래그와 `BuildAndRegister` 경로는 CLI에서 제거됨.
`IBuildClient` / `GrpcBuildClient`는 Avalonia(NodeKit.csproj)에만 남아 있음 — Sprint 7 대상.

현재 NodeKit 초점:

```text
Sprint 7: Avalonia GUI ToolSpec 마이그레이션
+ U5-2: seoy 원격 장비 nodekit submit 수동 테스트 (seoy 준비 후)
+ CI 그린 유지
```

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

### Sprint 6. ToolSpecRequest Migration Gate ✓ (2026-07-02 완료)

Goal:

```text
Start migration only after NodeVault Phase 1 is actually complete.
```

Entry criteria — 모두 충족됨 (2026-07-02):

```text
✓ NodeVault has canonical ResolveToolSpec implementation.
✓ NodeVault has SubmitToolBuild API.
✓ PLATFORM_SCHEDULE.md Phase 6 has begun.
✓ NodeVault proto/API is stable enough to vendor (protos/ 디렉터리에 벤더링됨).
```

Tasks — 모두 완료:

```text
✓ 1. Vendor the stable NodeVault proto. (protos/nodevault/v1/nodevault.proto)
✓ 2. Add ToolSpecRequest authoring models. (GrpcToolSpecClient, IToolSpecBuildClient)
✓ 3. Add CLI-first path. (nodekit submit → ResolveToolSpec → SubmitToolBuild → WatchToolBuild)
✓ 4. Add ResolveToolSpec client. (GrpcToolSpecClient.ResolveAndBuildAsync Step 1)
✓ 5. Add SubmitToolBuild client. (GrpcToolSpecClient.ResolveAndBuildAsync Step 2)
✓ 6. Remove legacy BuildRequest path from CLI. (--legacy 플래그 + BuildAndRegister 제거)
```

### Sprint 7. Post-Migration Hardening (진행 중)

Goal:

```text
Make the new path reliable enough to replace all legacy usage.
```

Tasks:

```text
○ 1. Avalonia GUI(NodeKit.csproj)를 IBuildClient/GrpcBuildClient에서 GrpcToolSpecClient로 전환.
     IBuildClient / GrpcBuildClient는 현재 NodeKit.csproj(Avalonia)에만 남아 있음.
○ 2. U5-2: seoy 원격 장비에서 nodekit submit 수동 테스트 통과.
○ 3. NodeVault 측 BuildAndRegister RPC deprecated 표시 (NodeVault 담당).
```

Done when:

```text
- Avalonia GUI도 ToolSpec 경로로 전환 완료.
- CLI end-to-end 수동 테스트 통과 (seoy 장비).
- IBuildClient / GrpcBuildClient 완전 제거 또는 명시적 ADR 후 유지 결정.
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

Captured: 2026-07-02 (업데이트)

NodeVault observation:

```text
- NodeVault Phase 1 완료: ResolveToolSpec / SubmitToolBuild / WatchToolBuild API 사용 가능.
- BuildAndRegister RPC는 NodeVault에 남아 있지만 NodeKit CLI에서는 제거됨.
- proto는 protos/nodevault/v1/nodevault.proto 로 벤더링됨 (git tracked).
- NodeKit CLI는 GrpcToolSpecClient 경유 3단계 경로만 사용.
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

Latest local result (2026-07-02):

```text
- Locked restore: pass
- Package audit: pass
- Format: pass (dotnet format --verify-no-changes exit 0)
- Build: pass, 0 warnings, 0 errors
- Tests: pass, 461 passed, 0 failed, 0 skipped
- Coverage threshold: pass
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

## 7. Non-Goals (불변 / 영구)

NodeVault Phase 1 완료 후에도 NodeKit이 절대 하지 않는 것:

```text
- No local authoritative canonical digest calculation.
- No NodeKit image build logic (docker/buildah/buildkit 실행 금지).
- No Kubernetes API calls from NodeKit.
- No rootless/Buildah handling in NodeKit.
- No NodeVault index/catalog mutation from NodeKit.
```

Phase 1 이전 non-goal이었으나 현재 완료된 항목 (참고용):

```text
✓ ToolSpecRequest CLI 경로 — 완료 (GrpcToolSpecClient)
✓ ResolveToolSpec 클라이언트 경로 — 완료
✓ SubmitToolBuild 클라이언트 경로 — 완료
```

## 8. Handoff Note

The correct instruction for a NodeKit agent is:

```text
NodeKit CLI는 Phase 6 완료 (2026-07-02)로 ToolSpec 경로로 전환됨.
BuildAndRegister / legacy 경로는 CLI에서 제거됨.
다음 단계: Avalonia GUI(NodeKit.csproj)의 IBuildClient → GrpcToolSpecClient 전환 (Sprint 7).
재현성 규칙(latest 태그 금지, digest 필수, 패키지 버전 고정)은 여전히 불변.
NodeVault 경계(이미지 빌드, K8s API, 인덱스 뮤테이션)도 여전히 불변.
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

## 11. Recipe Authoring UX v0.9.2 Implementation Sprints (2026-06-25)

Design source:
[`docs/NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md`](NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md)
("Freeze Candidate"). This document supersedes the v0.8 beginner design doc
cited in Section 10 for `recipe create`'s first-entry UX. Section 10's R0-R7
sprints are complete and shipped (`recipe create` today drives
`RecipeAuthoringSession` / `RecipeFieldCatalog` / `RecipeMethodRecommender`
end to end; documented in `docs/NODEKIT_CLI_USAGE.md`). This track extends
that base — it does not redo it.

This track stays inside the same boundary as Sections 9/10: no NodeVault
submission, no image build, no MCP server implementation. It changes only
the `recipe create` entry UX, escape-hatch commands, Ctrl+C handling, and
non-interactive documentation accuracy.

### Sprint R8. Naming And Terminology Alignment

Goal:

```text
Make the v0.9.2 design doc's terminology match the shipped code before any
new component is built on top of it.
```

Tasks:

```text
1. In NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md, replace RecipeDraft /
   RecipeSession / RecipeCreateSession references with the actual class name
   RecipeAuthoringSession (Sections 4.5, 24.1, 24.2).
2. Update Section 23.3 / 29.4 from hedged "Command may or may not be a list
   field" language to a confirmed statement: Command is
   RecipeFieldType.StringList in RecipeFieldCatalog today, so
   IsListType(Command) == true.
3. No production code changes in this sprint; documentation only.
```

Done when:

```text
- grep for "RecipeDraft|RecipeCreateSession" in the v0.9.2 doc returns no
  hits.
- Section 23.3 states the Command list-type fact directly, not as an open
  question.
```

Progress:

```text
- 2026-06-25: Completed. All RecipeDraft / RecipeSession / RecipeCreateSession
  references in NODEKIT_CLI_RECIPE_AUTHORING_UX_V0.9.2.md replaced with
  RecipeAuthoringSession (Sections 4.5, 5, 10.4, 10.6, 24.1, 24.2). Confirmed
  via src/Authoring/Recipes/RecipeFieldCatalog.cs that Command is
  RecipeFieldType.StringList (Optional); updated Sections 20.1, 23.3, 29.4,
  the implementation checklist (Section 32), and the conclusion (Section 33)
  from hedged language to a confirmed statement, and added Command to the
  v0.9.2-guaranteed repeatable list-field set alongside Packages, Channels,
  SourceBuildCommands, BuildDependencies. No production code touched.
```

### Sprint R9. Phase 1-A — Prompt Command Escape Hatches

Goal:

```text
Let users exit or inspect an in-progress recipe create session at any major
prompt, without touching method-recommendation or field-catalog logic.
```

Tasks:

```text
1. Implement PromptCommandHandler recognizing /help, /review, /cancel,
   /quit, /exit at every major input prompt (existing /change-method stays
   as-is; do not change its behavior in this sprint).
2. Implement /review: render current RecipeAuthoringSession field state,
   explicitly marking unset fields as not-yet-entered.
3. Implement /cancel (and /quit, /exit as aliases): confirm, then exit
   without writing a file.
4. Add RecipeCreateResultKind.Cancelled (or RecipeCancelledException — pick
   one per Section 18.4 of the design doc) and map it to process exit
   code 130.
```

Done when:

```text
- Cancelling at any prompt produces no recipe.json on disk.
- Exit code is 130 on cancellation, distinct from the existing 0/1/2 codes.
- /review output matches the design doc's "아직 입력 안 함" convention for
  unset fields.
- Existing recipe create interactive tests still pass unchanged.
```

Progress:

```text
- 2026-06-25: Completed. Added /review, /cancel, /quit, /exit checks to the
  same four prompt-loop insertion points where /help and /change-method
  already lived in RecipeCreateInteractiveRunner.cs (PromptScalarField,
  PromptChoiceField, PromptStringListField, PromptPresetListField) — not to
  the recommender Q&A or method-selection prompts, consistent with this
  sprint's "without touching method-recommendation... logic" goal.
  Cancellation is represented by a new RecipeCreateCancelledException
  (src/NodeKit.Cli/RecipeCreateCancelledException.cs), chosen over
  RecipeCreateResultKind.Cancelled because every prompt-loop method here is
  void — an exception propagates cancellation to Run's single catch site
  without threading a new result type through every call site. Run() catches
  it, prints the Section 17.3 cancellation message, and returns 130 (no
  stack trace, no recipe.json write). /review renders RecipeAuthoringSession.
  Snapshot() against RecipeFieldCatalog.FieldsFor(method), marking any field
  absent from Snapshot().Values as "아직 입력 안 함" per Section 17.4. Added 5
  tests to RecipeCreateInteractiveTests.cs (/review display, /cancel
  declined-continues, /cancel + /quit + /exit confirmed-exits-130). Full
  suite: 319/319 passing (36 NodeKit.Cli.Tests + 283 NodeKit.Tests), 0 build
  warnings.
```

### Sprint R10. Phase 1-B — Ctrl+C Signal Cancellation

Goal:

```text
Map process-level Ctrl+C onto the same cancellation result as /cancel,
through a testable abstraction (no real signal in unit tests).
```

Tasks:

```text
1. Introduce an IConsoleCancellation (or equivalent) seam so
   Console.CancelKeyPress is not called directly from logic under test.
2. Wire Console.CancelKeyPress -> cancellation flag/CancellationToken ->
   the same Cancelled result path Sprint R9 introduced.
3. Suppress stack traces on Ctrl+C; print the same cancellation message as
   /cancel.
4. Add tests that inject a fake cancellation source and assert: no file
   written, exit code 130, no stack trace, message matches /cancel's.
```

Done when:

```text
- Ctrl+C and /cancel produce identical observable outcomes (file absence,
  exit code, message) under test.
- No new compiler warnings (CLAUDE.md Section 8).
```

Progress:

```text
- 2026-06-25: Completed. Added IRecipeCreateCancellationSource
  (src/NodeKit.Cli/IRecipeCreateCancellationSource.cs), a single-member
  `bool IsCancellationRequested { get; }` seam per Section 18.5, and its
  production implementation ConsoleCancelKeyCancellationSource
  (src/NodeKit.Cli/ConsoleCancelKeyCancellationSource.cs), which subscribes
  to Console.CancelKeyPress, sets e.Cancel = true so the process survives
  the signal, and latches a volatile flag. RecipeCreateCancelledException
  from Sprint R9 is reused (not replaced) for the Ctrl+C path, keeping a
  single cancellation representation. RecipeCreateInteractiveRunner.Run was
  split into the public 5-arg overload (constructs a real
  ConsoleCancelKeyCancellationSource) and an internal 6-arg overload taking
  IRecipeCreateCancellationSource, reachable from tests via
  InternalsVisibleTo. The cancellation parameter was threaded through
  RunFieldLoop, PromptField, RunRecoveryLoop, ReEditField, and a check
  (`if (cancellation.IsCancellationRequested) throw new
  RecipeCreateCancelledException();`) was added at the top of the same four
  prompt-loop insertion points Sprint R9 used (PromptScalarField,
  PromptChoiceField, PromptStringListField, PromptPresetListField) — not at
  every stdin read site, per CLAUDE.md Section 7 and consistent with R9's
  own scope decision. Known limitation: Ctrl+C during the recommender Q&A,
  method selection, dockerfile-warning confirmation, /change-method's
  internal reads, or the Inputs/Outputs list-review/edit sub-flow is not yet
  covered — those sites would need the same threading if this gap matters
  in practice. Added 2 tests to RecipeCreateInteractiveTests.cs using a fake
  SequencedCancellationSource (returns false for N checks, then true): one
  asserting Ctrl+C-equivalent cancellation produces exit code 130, no file
  written, no stack trace, and the exact /cancel message text; one asserting
  /cancel and simulated-Ctrl+C produce an identical exit code and final
  message lines. Full suite: 321/321 passing (38 NodeKit.Cli.Tests + 283
  NodeKit.Tests), 0 build warnings.
```

### Sprint R11. Phase 2 — Authoring Mode Selector + Fast Questionnaire Enrichment

Goal:

```text
Add the up-front mode choice and enrich the existing 6-question recommender
flow with the explanation/example/impact text the design doc specifies,
without changing recommendation logic.
```

Tasks:

```text
1. Implement AuthoringModeSelector: the three-choice entry screen (쉬운 안내
   모드 / 빠른 설정 모드 / 스크립트-CI 모드 사용법 보기) from Section 7.
   The third choice prints usage and exits without starting recipe create.
2. Wrap each of the existing 6 questions (RecipeMethodQuestionCatalog) with
   the per-question explanation/example/impact text from Sections
   15.3-15.8. Do not change RecipeMethodRecommender's decision logic.
3. Implement MethodRecommendationPresenter: the post-recommendation summary
   screen (Section 16) showing recommended method, reason, upcoming fields,
   and warnings, with a reject-and-pick-manually path.
```

Done when:

```text
- Existing RecipeMethodRecommender tests pass unchanged (logic untouched).
- New presenter/selector tests cover: mode selection, the CI-mode early
  exit, and the recommendation-reject-then-manual-select path.
```

Progress:

```text
- 2026-06-26: Completed. Added AuthoringModeSelector
  (src/NodeKit.Cli/AuthoringModeSelector.cs) — Section 7 entry screen
  with choices [1] 쉬운 안내 모드 / [2] 빠른 설정 모드 / [3] 스크립트/CI 모드.
  Mode 3 prints --non-interactive usage and returns exit code 0 without
  entering the Q&A. Mode 1 is currently a placeholder (routes to the same
  Q&A flow as mode 2 with a "아직 준비 중" notice; actual BeginnerGuideFlow
  is deferred to Sprint R13). Added RecipeMethodQuestionDetail record +
  RecipeMethodQuestionDetailCatalog (src/NodeKit.Cli/) with the
  Section 15.3-15.8 per-question meaning/example/y-effect/n-effect/
  enter-effect text for all 6 questions. Updated AskRecommenderQuestions to
  print the Section 15.2 quick-setup intro screen and enriched question
  detail before each answer prompt (prompt text changed from "[y/n/u]" to
  "선택 [y/n/Enter]:", behavior unchanged). Added MethodRecommendationPresenter
  (src/NodeKit.Cli/MethodRecommendationPresenter.cs) — Section 16 screen
  showing recommended method, reason, effects, upcoming fields (from
  RecipeFieldCatalog.FieldsFor), and cautions; accept [Y/n] path or reject
  → fixed 1-5 manual method menu; internally loops until valid input.
  Extracted TryParseMethodSelection from RecipeCreateInteractiveRunner
  (private) into MethodRecommendationPresenter (internal static), shared
  with TryHandleChangeMethod. Removed DisplayRecommendation, PromptMethodChoice,
  TryParseMethodSelection from RecipeCreateInteractiveRunner. SelectMethod
  outer while-loop removed (presenter handles its own looping).
  All 18 existing interactive test transcripts updated to prepend "2"
  (quick-setup mode selection). Note: "앞으로 입력할 항목" in the presenter
  shows catalog field.Name strings (e.g. "ImageRef") rather than the
  design doc's Section 16 example text ("BaseImage") — this reflects the
  pre-existing naming difference in RecipeFieldCatalog and is not renamed
  here per CLAUDE.md §7. Added 3 new tests: CI-mode early exit (exit 0,
  no Q&A consumed, --non-interactive usage shown), guided-beginner fallback
  (notice printed, Q&A continues, file saved), recommendation-reject→manual
  select (picks [1] container, saves BioContainer recipe). Full suite:
  324/324 passing (41 NodeKit.Cli.Tests + 283 NodeKit.Tests), 0 build
  warnings. RecipeMethodRecommender tests unchanged and passing.
```

### Sprint R12. Phase 3a — InstallCommandParser Spike

Goal:

```text
Resolve the highest-uncertainty contract in the whole design — the
install-command parser's Parsed/PartiallyParsed/Failed boundary — against
real-world install command strings before BeginnerGuideFlow is built on top
of it.
```

Tasks:

```text
1. Build a fixture table of real install commands (conda, micromamba, pip,
   curl|bash, git clone && make, and at least 10 more representative
   bioinformatics-tool install patterns) with their expected
   Parsed/PartiallyParsed/Failed classification per Section 9.2.
2. Implement InstallCommandParser against that fixture table only — no CLI
   wiring yet.
3. Treat any fixture that the parser cannot classify as expected as a design
   gap to resolve before Sprint R13, not a bug to patch silently.
```

Done when:

```text
- InstallCommandParser passes its fixture table.
- Any fixture-table gaps found are written back into the v0.9.2 design doc
  Section 9.2/9.3 as resolved open questions, or explicitly deferred with a
  noted reason.
```

**Progress (Sprint R12 완료):**

```text
구현 완료. 빌드 경고 0건, 359/359 테스트 통과.

신규 파일:
  src/Authoring/Recipes/InstallCommandParseStatus.cs  (internal enum)
  src/Authoring/Recipes/InstallCommandParseResult.cs  (internal sealed record)
  src/Authoring/Recipes/InstallCommandParser.cs       (internal static class)
  tests/NodeKit.Tests/Recipes/InstallCommandParserTests.cs
    → 26케이스: Parsed 8, ParsedWithWarning 3, PartiallyParsed 3,
      CondaCreate 2, Failed 14 + OriginalCommand 보존 2건 + 기타 2건

설계 갭 해결 및 문서화 (Section 9.4 추가):
  - mamba → Failed (지원 엔진 외)
  - conda install 채널 미지정 → PartiallyParsed (Missing=[Channels]);
    묵시적 defaults 채널 불삽입 (재현성 원칙)
  - conda create → PartiallyParsed + 의미론 경고
  - 래핑 명령 → Failed (첫 토큰 기준)
  - public → internal 선언 (CA1515; InternalsVisibleTo로 테스트 접근)

커밋: (다음 커밋에 포함)
```

### Sprint R13. Phase 3b — Beginner Guide Flow + Image Reference Normalizer

Goal:

```text
Implement the clue-based entry flow for users who start with no method
decided, reusing InstallCommandParser from R12 and generalizing the
ImageRef/ImageDigest composition that already exists narrowly in
RecipeAuthoringSession.Build() for the Container method.
```

Tasks:

```text
1. Implement BeginnerGuideFlow: the first-question clue picker (Section 8.2)
   and per-clue sub-flows (install command, container image, source/GitHub,
   Dockerfile, internal mirror, tool-name-only) per Sections 9-14.
2. Implement ImageReferenceNormalizer as a generalization of the existing
   BaseImage+ImageDigest composition in RecipeAuthoringSession.Build():
   accept ImageRef with or without an embedded digest plus an optional
   separate ImageDigest, detect conflicts (Section 10.5), and run
   immediately before RecipeDocument construction (Section 10.4) — not
   inside prompt-layer string handling.
3. Implement the "아무것도 모름" (no clues at all) safe-exit path (Section 14)
   that explains the minimum required clue set instead of forcing the
   wizard forward.
```

Done when:

```text
- ImageRef-with-digest, ImageRef-without-digest+separate-ImageDigest, and
  conflicting-digest cases each produce the documented canonical URI or the
  documented conflict-resolution prompt.
- A test proves RecipeValidator/RecipeRenderer/L1 validators only ever see
  an already-normalized digest-pinned URI, never a raw split ImageRef.
- The "아무것도 모름" path exits without writing a file and without forcing
  a method choice.
```

**Progress (Sprint R13 완료):**

```text
완료:
- ImageReferenceNormalizeStatus / ImageReferenceNormalizeResult / ImageReferenceNormalizer
  (src/Authoring/Recipes/)
  • Normalize(imageRef, imageDigest): Normalized / MissingDigest / DigestConflict 세 결과
  • 임베디드 @sha256: 파싱, sha256: 접두어 자동 추가, 빈/공백 digest 처리

- BeginnerGuideFlow (src/NodeKit.Cli/BeginnerGuideFlow.cs)
  • 7-choice 단서 픽커 (Section 8.2)
  • 설치 명령 서브플로 (Section 9): InstallCommandParser 재사용,
    Parsed/PartiallyParsed/Failed 처리
  • 컨테이너 이미지 서브플로 (Section 10): ImageReferenceNormalizer 적용,
    MissingDigest 시 [1-4] 선택 프롬프트
  • 소스/GitHub 서브플로 (Section 11)
  • Dockerfile 서브플로 (Section 12): 기본-N 경고 + 확인
  • 미러 서브플로 (Section 13)
  • 아무것도 모름 서브플로 (Section 14): 최소 단서 안내 후 파일 저장 없이 exit 0

- RecipeCreateInteractiveRunner: mode==GuidedBeginner → BeginnerGuideFlow.Run() 호출,
  null 반환 시 "단서가 부족합니다." 출력 후 exit 0

- NodeKit.Cli.csproj: InstallCommandParser+ImageReferenceNormalizer 관련
  신규 파일 6개 소스 링크 추가

테스트:
- ImageReferenceNormalizerTests.cs: 12 unit + 2 integration (NodeKit.Tests)
  • Session_SplitImageRefDigest_BuildProducesCanonicalBioContainerUri
  • Session_SplitImageRefDigest_L1ValidationPasses_NoDigestViolation
    (BuildKind = RecipeBuildKindResolver.Resolve() 포함)
- BeginnerGuideFlowTests.cs: ~15 transcript 기반 테스트 (NodeKit.Cli.Tests)
- RecipeCreateInteractiveTests.cs: GuidedBeginner 모드 transcript 갱신

최종 테스트 결과: NodeKit.Tests 332/332, NodeKit.Cli.Tests 57/57 (0 failed)

설계 비고:
- DigestConflict 는 BeginnerGuideFlow 내에서 도달 불가
  (임베디드 digest → 즉시 Normalized; SeparateDigest 분기는 MissingDigest에서만
  진입 → 별도 digest 제공 시 항상 Normalized). 단위 테스트로만 커버.
```

### Sprint R14. Phase 4 — Non-Interactive Contract Alignment

Goal:

```text
Bring docs/NODEKIT_CLI_USAGE.md and the v0.9.2 doc's non-interactive
examples in line with RecipeCreateCommand's actual parsing behavior — this
is a documentation/test-confirmation sprint, not new parsing logic.
```

Tasks:

```text
1. Add a regression test asserting --field splits only on the first '=' and
   preserves embedded '=' in the value (e.g. Packages=bwa=0.7.17=h5bf99c6_8).
2. Add a regression test asserting repeated --field Name=Value for a
   StringList field (Packages, Channels, SourceBuildCommands,
   BuildDependencies, and Command since R8 confirmed it is StringList)
   accumulates rather than overwrites.
3. Update non-interactive examples in both the v0.9.2 doc and
   docs/NODEKIT_CLI_USAGE.md to use --field ToolVersion=... (internal field
   name), not --field Version=....
```

Done when:

```text
- New regression tests pass against the existing RecipeCreateCommand parser
  with no production code changes (Section 23 of the design doc frames this
  as confirming existing behavior, not building new behavior).
- No non-interactive example in either doc uses the Version alias.
```

**Progress (Sprint R14 완료):**

```text
완료:
- RecipeCreateCommandTests.cs 에 regression 테스트 2건 추가 (production code 변경 없음)
  • Field_EmbeddedEquals_PreservedAsValue_NotSplitAgain
    TrySplitOnce(IndexOf('=')) 가 Packages=bwa=0.7.17=h5bf99c6_8 에서
    이름=Packages, 값=bwa=0.7.17=h5bf99c6_8 으로 분리하는 동작 검증
  • Field_StringList_RepeatedField_AccumulatesAllValues
    --field Packages=pkg1 --field Packages=pkg2 (및 Channels) 반복 시
    덮어쓰지 않고 누적하는 동작 검증

문서 점검:
  - NODEKIT_CLI_USAGE.md: --field Version= 잔재 없음 (ToolVersion만 사용)
  - UX v0.9.2 doc: --field Version= 예시 없음
    (1767번 줄 "공식 문법이 아니다" 설명은 그대로 유지)

최종 테스트 결과: NodeKit.Cli.Tests 59/59 (2 추가), 0 failed. 빌드 경고 0.
```

### Sprint R15. Documentation Update

Goal:

```text
Restructure docs/NODEKIT_CLI_USAGE.md's recipe create section to match the
shipped v0.9.2 UX, per Section 28 of the design doc.
```

Tasks:

```text
1. Restructure the recipe create section into the 2-1..2-9 subsection layout
   from Section 28 (mode selection, easy guide mode, fast setup mode,
   common fields, per-method fields, Inputs/Outputs, recovery,
   non-interactive, escape hatches/review/change-method).
2. Document /back as explicitly out of scope for v0.9.2 with the reason
   from Section 17.2, not silently absent.
3. Document the digest-required-by-default container flow and the
   Dockerfile-method default-N warning from Sections 10.2/12.2.
```

Done when:

```text
- docs/NODEKIT_CLI_USAGE.md's recipe create section reflects the v0.9.2
  flows a reader would actually see, not the pre-v0.9.2 wizard.
- No stale field-name examples (Section 19.2's Version/ToolVersion
  distinction is reflected).
```

**Progress (Sprint R15 완료):**

```text
완료:
- NODEKIT_CLI_USAGE.md 섹션 2 전면 재구성 (2-1~2-5 → 2-1~2-9)
  2-1: 진행 방식 선택 (AuthoringModeSelector 화면 예시 포함)
  2-2: 쉬운 안내 모드
       · 7단서 picker 표 및 화면 예시
       · container digest 필수 플로우 (MissingDigest → [1-4])
       · Dockerfile default-N 경고 (y/N 화면)
       · 아무것도모름 안전종료 설명
  2-3: 빠른 설정 모드 (기존 6문항 Q&A 보존)
  2-4: 공통 필드 입력
  2-5: method별 필드 입력 (container ImageRef+digest 설명 개선)
  2-6: Inputs/Outputs 입력 (구 2-3 내용)
  2-7: recovery (구 2-4 내용)
  2-8: non-interactive
       · --field 첫 번째 = 구분자 규칙 명시
       · 목록 필드 반복 누적 명시
       · 예시 --input/--output 프리셋 id 형식으로 정정
  2-9: 중간에 나가기 / review / method 변경
       · /help, /review, /change-method, /cancel, /quit, /exit 표 형식
       · /back 명시적 범위 밖 기록 (Section 17.2 이유: 단방향 루프,
         method 변경 후 필드 무효화, Inputs/Outputs 이전 단계 의미 모호)

Version/ToolVersion 확인: 두 문서 모두 --field Version= 잔재 없음 (R14 확인 재확인)

변경: docs/NODEKIT_CLI_USAGE.md (+164 -68)
```

### 11.1 Sequencing Note

```text
R8 is mechanical documentation cleanup and can run first, independent of
everything else.
R9 and R10 are independent of method-recommendation/beginner-flow logic and
can ship before R11-R13; R10 depends on R9's Cancelled result type existing.
R11 only touches existing recommender presentation, not its logic; it can
run in parallel with R9/R10 if capacity allows, but has no hard dependency
on them.
R12 must precede R13 — R13 reuses InstallCommandParser and would otherwise
bake an unvalidated parser contract into BeginnerGuideFlow.
R13 is the highest-risk sprint in this track, matching the design doc's own
Phase 3 risk ordering (Section 25).
R14 only needs RecipeCreateCommand as it exists today; it has no dependency
on R9-R13 and could run anytime, but is sequenced last among code sprints
so its doc updates land alongside R15 in one documentation pass.
R15 depends on R9-R14 being functionally complete, since it documents their
combined behavior.
```

### 11.2 한국어 요약

```text
이 트랙은 v0.9.2 설계 문서(Freeze Candidate)를 구현하는 sprint 시퀀스다.
10번 섹션의 R0-R7(recipe create 기본 구현)은 이미 완료되어 있고, 이 트랙은
그 위에 진입 UX/이스케이프 핫치/Ctrl+C/non-interactive 문서 정합성을 추가한다.
NodeVault 제출, 이미지 빌드, MCP server 구현은 여전히 범위 밖이다.

R8:  문서 용어 정리 (RecipeDraft 등 -> RecipeAuthoringSession, Command는
     StringList로 확정 — 기계적, 코드 변경 없음, 가장 먼저 처리)
R9:  Phase 1-A — /help, /review, /cancel, /quit, /exit + 종료 코드 130
R10: Phase 1-B — Ctrl+C를 같은 취소 결과로 매핑 (R9의 Cancelled 결과 타입 의존)
R11: Phase 2 — AuthoringModeSelector + 기존 6문항 설명 보강 + 추천 결과 화면
     (추천 로직 자체는 변경 없음, R9/R10과 병행 가능)
R12: Phase 3a — InstallCommandParser 스파이크 (실제 설치 명령 fixture 테이블로
     Parsed/PartiallyParsed/Failed 계약을 먼저 검증, CLI 연결 없음)
R13: Phase 3b — BeginnerGuideFlow + ImageReferenceNormalizer
     (R12 의존, 이 트랙에서 위험도 가장 높음)
R14: Phase 4 — non-interactive 파싱 계약 회귀 테스트 + 문서 예시 정정
     (--field 첫 '=' 기준, 리스트 필드 반복 누적, ToolVersion 표기)
R15: 사용 가이드(NODEKIT_CLI_USAGE.md) recipe create 섹션 재구성
     (R9-R14 완료 후, 마지막)

순서: R8 단독 선행 가능 -> (R9 -> R10) 및 R11은 서로 독립적으로 병행 가능 ->
R12 -> R13 -> R14 -> R15 -> R16.
```

## 12. ResolveRecipe Client Seam (2026-06-28)

Design source: `docs/PLATFORM_MASTER_DESIGN.md` §4.9 / §6,
`NodeVault/docs/PLATFORM_SCHEDULE.md` 병렬 트랙 D.

이 트랙은 NodeVault `ResolveRecipe` RPC가 proto에 추가되기 전에 NodeKit의
UX 계층을 먼저 구현한다. proto 없이도 인터페이스 seam + Null 구현으로 전체
흐름을 선제적으로 완성하고, proto가 준비되면 `GrpcResolveRecipeClient`만
플러그인한다.

### Sprint R16. ResolveRecipe Seam + Candidate Selection UX

Goal:

```text
Build the NodeKit side of the ResolveRecipe pre-build step: define the
interface, provide a no-op null implementation, implement the candidate
selection UI, and wire it between L1 validation and recipe save.
```

Tasks:

```text
1. Define IResolveRecipeClient + result model types
   (ResolveRecipeResult, PackageResolution, BuildStringCandidate,
   RecipeResolutionSource enum).
2. Implement NullResolveRecipeClient (returns Unsupported → step skipped).
3. Implement PackageCandidatePresenter: show numbered candidate list for
   multi-candidate packages; auto-select for single-candidate packages;
   ApplySelections replaces version-only pins with full_pin strings.
4. Wire into RecipeCreateInteractiveRunner.Run(): after L1 validation
   passes and before SaveDocument, call IResolveRecipeClient.ResolveAsync
   for Package-method recipes and present any returned candidates.
5. Show polite guidance when resolution_source == NotFound (closed network
   without Harbor pre-registration).
```

Done when:

```text
- Build: 0 warnings, 0 errors.
- PackageCandidatePresenterTests: single-candidate auto-select, multi-candidate
  prompt+pick, Enter=first, /cancel throws, invalid-then-valid reprompt,
  ApplySelections replaces pin correctly.
- NullResolveRecipeClient test: returns Unsupported.
- All existing interactive tests still pass unchanged (Null client means
  the resolve step is a no-op today).
```

**Progress (Sprint R16 완료, 2026-06-28):**

```text
완료:
- src/NodeKit.Cli/IResolveRecipeClient.cs
    IResolveRecipeClient interface, ResolveRecipeResult, PackageResolution,
    BuildStringCandidate, RecipeResolutionSource enum
- src/NodeKit.Cli/NullResolveRecipeClient.cs
    Singleton no-op; returns Unsupported
- src/NodeKit.Cli/PackageCandidatePresenter.cs
    Present(): auto-select for 1 candidate; numbered prompt for N>1;
    /cancel|/quit|/exit → RecipeCreateCancelledException; invalid input → reprompt
    ApplySelections(): replaces version-only pin with full_pin by package name
- RecipeCreateInteractiveRunner.Run(): optional IResolveRecipeClient param
    (default = NullResolveRecipeClient); resolve step inserted after validation,
    before SaveDocument. NotFound path prints폐쇄망 guidance without blocking.
- tests/NodeKit.Cli.Tests/PackageCandidatePresenterTests.cs: 9 tests
    (auto-select, pick-second, Enter=first, cancel-throws, invalid-then-valid,
    ApplySelections-replaces, ApplySelections-empty, ApplySelections-full-pin,
    NullClient-returns-unsupported)

최종 결과: 336 NodeKit.Tests + 92 NodeKit.Cli.Tests = 428 / 428 통과, 0 warnings.
```

### Sprint R17. GrpcResolveRecipeClient (NodeVault proto 준비 후)

Goal:

```text
Implement the real gRPC client once NodeVault adds ResolveRecipe to the proto.
```

Entry criteria:

```text
- NodeVault has added ResolveRecipe RPC + ResolveRecipeRequest /
  ResolveRecipeResponse / PackageResolution / BuildStringCandidate messages
  to protos/nodevault/v1/nodevault.proto.
- Vendored proto in NodeKit is updated.
```

Tasks:

```text
1. Generate C# gRPC stubs from the updated proto.
2. Implement GrpcResolveRecipeClient: call NodeVault ResolveRecipe, map
   the response to NodeKit's ResolveRecipeResult model.
3. Wire GrpcResolveRecipeClient into RecipeCreateInteractiveRunner.Run()
   when NODEKIT_NODEVAULT_URL env var is set (same pattern as HarborImageDigestResolver).
4. Add integration tests (opt-in, skipped without live NodeVault).
```

Done when:

```text
- bwa=0.7.17 + Harbor cache hit → one candidate auto-selected, full_pin
  written to recipe.json.
- bwa=0.7.17 + Harbor miss + open network → candidate list shown to user.
- bwa=0.7.17 + Harbor miss + closed network → NotFound guidance shown,
  recipe saved without build_string (NodeVault will resolve at submit time).
```

**Progress (Sprint R17 완료, 2026-06-30):**

```text
완료 (커밋 0180674):
- src/NodeKit.Cli/GrpcResolveRecipeClient.cs
    TryCreate(): NODEKIT_NODEVAULT_URL 게이팅 (HarborImageDigestResolver와 동일 패턴)
    BuildService.BuildServiceClient.ResolveRecipeAsync 호출, PackageSpec/RecipeVariant/
    ResolveRecipeResponse → ResolveRecipeResult 매핑. GrpcChannel 소유, IDisposable.
- RecipeCreateInteractiveRunner.cs: resolve client fallback chain에 연결
    (test override → StubResolveRecipeClient → GrpcResolveRecipeClient → NullResolveRecipeClient)
- protos/nodevault/v1/nodevault.proto: NodeVault 원본과 diff 결과 동일, 재벤더링 불필요
    (ResolveRecipeRequest/ResolveRecipeResponse/PackageResolution/BuildStringCandidate 이미 반영됨)
- tests/NodeKit.Cli.Tests/GrpcResolveRecipeClientIntegrationTests.cs
    NODEKIT_NODEVAULT_URL 미설정 시 skip되는 opt-in 통합 테스트

NodeVault 측 pkg/build/recipe_resolve.go: Harbor 우선 조회 + conda/micromamba 외부 조회
(Anaconda.org API) + 폐쇄망 차단 모두 실구현, TestResolveRecipe_* 7/7 통과 확인.
BIOCONTAINER variant만 codes.Unimplemented로 명시적 미구현(P3, 의도된 설계).

최종 결과: PackageCandidatePresenterTests 9개 + GrpcResolveRecipeClientIntegrationTests
+ RecipeCreateInteractiveTests(FixedResolveRecipeClient) 모두 통과.
```
