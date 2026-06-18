# NodeKit Legacy-First Sprint Plan

Status: Active Planning  
Created: 2026-06-17  
Updated: 2026-06-17  
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

Use .NET coverage through the existing coverlet collector:

```bash
dotnet test --collect:"XPlat Code Coverage"
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
dotnet test NodeKit.sln --no-build --configuration Release --collect:"XPlat Code Coverage" --results-directory TestResults
./scripts/ci-check-coverage.sh
```

Latest local result:

```text
- Locked restore: pass
- Package audit: pass; xunit v2 is reported as deprecated and tracked for a dedicated migration
- Format: pass
- Build: pass, 0 warnings, 0 errors
- Tests: pass, 82 passed, 0 failed, 0 skipped
- Coverage threshold: pass, line >= 0.1400 and branch >= 0.0900
- Coverage artifact generated under TestResults/<run-id>/coverage.cobertura.xml
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
