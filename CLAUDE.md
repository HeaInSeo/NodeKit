# NodeKit — Claude Code Guidelines

## 0. Active planning memory

Read `docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md` before resuming NodeKit work.
Read `docs/NODEKIT_CLI_UX_IMPROVEMENT_SPRINT_PLAN.md` for the active CLI UX improvement sprints (U1-U5).

Current planning source of truth:

- **NodeVault Phase 1 gate 열림 (2026-07-02)**: `ResolveToolSpec` / `SubmitToolBuild` / `WatchToolBuild`
  클라이언트 경로가 NodeKit에서 구현 허용됨.
- `nodekit submit` 경로: `ResolveToolSpec → SubmitToolBuild → WatchToolBuild`.
- **Phase 6 완료 (2026-07-02)**: `--legacy` 플래그 및 `BuildAndRegister` 경로 NodeKit CLI에서 제거.
  `IBuildClient` / `GrpcBuildClient` 는 NodeKit.csproj(Avalonia) 에만 남음.
  NodeVault 측 `BuildAndRegister` RPC deprecated 표시는 NodeVault 담당.

Older sprint documents under `docs/obsolete/` are historical reference only and
must not override the active sprint plan.

New application state should follow the same direction as DagEdit: ReactiveUI /
System.Reactive first, with DynamicData where collection change streams are useful.
The current UI is still largely code-behind/event-handler based; do not expand
that pattern for new validation, submission progress, or result state. Introduce
focused reactive ViewModel/state objects for new behavior while keeping pure
validation and serialization code deterministic and easy to test.

At the start of each development session, observe NodeVault without editing it:

- `git -C /opt/go/src/github.com/HeaInSeo/NodeVault status --short --branch`
- read `/opt/go/src/github.com/HeaInSeo/NodeVault/docs/PLATFORM_SCHEDULE.md`
- read `/opt/go/src/github.com/HeaInSeo/NodeVault/docs/PLATFORM_MAP.md`
- inspect `/opt/go/src/github.com/HeaInSeo/NodeVault/protos/nodevault/v1/nodevault.proto`

NodeVault's planning/API documents are the upstream platform source of truth for
NodeKit integration. If NodeVault has only partial new-path support, NodeKit must
stay compatible with the legacy `BuildRequest` / `BuildAndRegister` path.

NodeVault and adjacent platform services are tested on remote infrastructure by
default. Discover live environment details from documents under
`~/.config/infra-lab`; do not assume a local NodeVault process. NodeKit local
tests should remain independent of live NodeVault unless an integration test is
explicitly opted in.

## 1. Responsibility boundary (immutable)

**NodeKit owns**: ToolDefinition authoring (UI forms, field validation), DataDefinition authoring
(reference data metadata forms), L1 static validation (image URI checks, package version pinning),
DockGuard policy execution via `WasmPolicyChecker`, BuildRequest / DataRegisterRequest generation
and gRPC transmission to NodeVault, AdminToolList / AdminDataList display (via Catalog 서비스 REST API),
and all admin UX semantics (status feedback, error display, policy management UI).

**NodeVault is a Kubernetes data-plane app.** NodeKit integrates with it through
the documented gRPC/REST API surface and must not reach into Kubernetes directly.

**NodeVault owns**: BuildRequest / DataRegisterRequest reception, tool image build orchestration
(L2/L3/L4), reference data packaging (sori), OCI referrer push, artifact index management (SoT),
DockGuard policy bundle management (`PolicyService`), Harbor lifecycle control, and Harbor webhook
reconciliation.

**Catalog 서비스 owns**: Read-only artifact palette (tools + reference data) for pipeline builders.
NodeKit queries Catalog 서비스 REST API for AdminToolList and AdminDataList.

Do not implement image building, Job scheduling, or K8s API calls in NodeKit.
Do not implement production `ToolSpecRequest`, `ResolveToolSpec`, or `SubmitToolBuild`
client paths until NodeVault Phase 1 is complete and the active sprint plan permits it.
Do not implement editor UX, selection policy, or undo/redo in NodeKit — those belong to DagEdit.

## 2. Key term boundaries (immutable)

| Term | Owner | Meaning |
|------|-------|---------|
| `ToolDefinition` | NodeKit | Tool authoring draft model. Not the final registered object. |
| `DataDefinition` | NodeKit | Reference data authoring draft model. Not the final registered object. |
| `BuildRequest` | NodeKit→NodeVault | What NodeKit sends over gRPC after L1 passes (tool). |
| `DataRegisterRequest` | NodeKit→NodeVault | What NodeKit sends over gRPC for reference data registration. |
| `RegisteredToolDefinition` | NodeVault | Post-L4 confirmed tool object. Harbor referrer + index. |
| `RegisteredDataDefinition` | NodeVault | Confirmed reference data object. Harbor referrer + index. |
| `AdminToolList` | NodeKit | Admin-only view of registered tools. Queries Catalog 서비스. |
| `AdminDataList` | NodeKit | Admin-only view of registered reference datasets. Queries Catalog 서비스. |
| `PipelineToolPalette` | DagEdit etc. | Pipeline app's view. Separate concept, separate app. |

Do not conflate `ToolDefinition` with `RegisteredToolDefinition`. Do not call `AdminToolList`
a palette. DagEdit is a separate project track — do not couple NodeKit to DagEdit internals.

## 3. Reproducibility rules (non-negotiable)

The project's core philosophy is: **same data + same method = same result.**

- `latest` image tags: block at L1 — no exceptions, no flags to relax this.
- Image digest not pinned (`@sha256:` absent): block at L1.
- Package install without version: block at L1 (`bwa` alone is invalid; `bwa=0.7.17`
  is valid). Build string (`=version=build`) is resolved by NodeVault `ResolveToolSpec`,
  not enforced at L1 — see PLATFORM_MASTER_DESIGN.md §4.9.
- Do not add bypass flags, fallback modes, or "allow-latest" toggles. Use pre-validated
  fixture/sample profiles for testing instead.

## 4. IPolicyBundleProvider / IPolicyChecker interface contract

`IPolicyBundleProvider` and `IPolicyChecker` are the key seam for policy abstraction:

```
LocalFilePolicyBundleProvider  (sprint start — local .wasm file)
    ↓ swap at runtime
GrpcPolicyBundleProvider       (after NodeVault PolicyService is ready)
```

Do not hardcode file paths into `WasmPolicyChecker`. Provider must be injectable.
Interface must be finalized before implementation to minimize swap cost.

## 5. gRPC client responsibility

NodeKit is a **gRPC client only**. It sends `BuildRequest` / `DataRegisterRequest` and receives
status/results. AdminToolList / AdminDataList display uses Catalog 서비스 REST API (not gRPC).
Do not implement gRPC server logic in NodeKit. The proto contract is the boundary —
any change to `.proto` definitions requires coordination with NodeVault.

## 6. Decision checklist before every change

- Does it add K8s API calls, Job scheduling, or image build logic to NodeKit? **Block.**
- Does it add `RegisteredToolDefinition` / `RegisteredDataDefinition` creation logic to NodeKit? **Block.**
- Does it relax a reproducibility rule (latest tag, digest, version pinning)? **Block.**
- Does it hardcode a policy bundle file path bypassing `IPolicyBundleProvider`? **Block.**
- Does it couple NodeKit to DagEdit internals? **Block.**
- Does it bypass Catalog 서비스 and query NodeVault index directly from NodeKit? **Block.**

## 7. Small diffs; no unrelated refactors

Each commit must have a single, stated purpose. Do not clean up surrounding code,
add comments to unchanged lines, or refactor while fixing a bug.

## 8. Warning policy

`EnforceCodeStyleInBuild=true` is set in `Directory.Build.props`.
Do not introduce new compiler warnings or InspectCode warnings.
Run `dotnet build` after every change to verify warning count does not increase.

## 9. Validation responsibility

| Change type | Expected validation |
|---|---|
| New feature | New or updated tests covering the added behavior |
| Bug fix | Regression test that would have caught the bug |
| Refactor | Existing tests must remain green; add tests if coverage was absent |
| L1 rule change | Direct test for the new rule (pass + block cases) |
| IPolicyBundleProvider swap | Both LocalFile and Grpc provider tests pass |
| Purely mechanical cleanup | No new tests required; existing tests must still pass |

## 10. Completion reporting

A task is not complete until the following are stated explicitly:

- **What changed**: files and logic affected
- **Validation run**: which tests, lint checks, or manual verifications were performed
- **Results**: pass/fail counts, warning counts, any regressions
- **Remaining risks**: known unknowns, deferred items, or assumptions not verified

## 11. Hidden failure mode review

Before marking a change complete, explicitly check for:

- L1 rules that can be bypassed by unusual input (empty string, whitespace, unicode variants)
- `WasmPolicyChecker` not loading the bundle (file missing, wrong path) silently passing all checks
- `IPolicyBundleProvider` swap leaving stale bundle in memory
- gRPC send failure not surfaced in UI (fire-and-forget without error propagation)
- `BuildRequest` missing required fields after serialization round-trip
- `AdminToolList` / `AdminDataList` displaying stale data after successful registration
- `DataRegisterRequest` missing required metadata fields after serialization round-trip
