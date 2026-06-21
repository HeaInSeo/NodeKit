# NodeKit Legacy-First Sprint Plan

Status: Active Planning  
Created: 2026-06-17  
Updated: 2026-06-21  
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
