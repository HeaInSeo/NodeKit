# NodeKit — Claude Code Guidelines

## 1. Responsibility boundary (immutable)

**NodeKit owns**: ToolDefinition authoring (UI forms, field validation), L1 static validation
(image URI checks, package version pinning), DockGuard policy execution via `WasmPolicyChecker`,
BuildRequest generation and gRPC transmission to NodeForge, AdminToolList display,
and all admin UX semantics (status feedback, error display, policy management UI).

**NodeForge owns**: BuildRequest reception, builder Job orchestration, DockGuard policy bundle
management (`PolicyService`), internal registry integration, `RegisteredToolDefinition` creation
and CAS storage, L3 kind dry-run, L4 smoke run, and `ToolRegistryService`.

Do not implement image building, Job scheduling, or K8s API calls in NodeKit.
Do not implement editor UX, selection policy, or undo/redo in NodeKit — those belong to DagEdit.

## 2. Key term boundaries (immutable)

| Term | Owner | Meaning |
|------|-------|---------|
| `ToolDefinition` | NodeKit | Authoring draft model. Not the final registered object. |
| `BuildRequest` | NodeKit→NodeForge | What NodeKit sends over gRPC after L1 passes. |
| `RegisteredToolDefinition` | NodeForge | Post-L4 confirmed object. CAS-stored by NodeForge. |
| `AdminToolList` | NodeKit | Admin-only view of registered tools. Not `PipelineToolPalette`. |
| `PipelineToolPalette` | DagEdit etc. | Pipeline app's view. Separate concept, separate app. |

Do not conflate `ToolDefinition` with `RegisteredToolDefinition`. Do not call `AdminToolList`
a palette. DagEdit is a separate project track — do not couple NodeKit to DagEdit internals.

## 3. Reproducibility rules (non-negotiable)

The project's core philosophy is: **same data + same method = same result.**

- `latest` image tags: block at L1 — no exceptions, no flags to relax this.
- Image digest not pinned (`@sha256:` absent): block at L1.
- Package install without version+build string: block at L1.
- Do not add bypass flags, fallback modes, or "allow-latest" toggles. Use pre-validated
  fixture/sample profiles for testing instead.

## 4. IPolicyBundleProvider / IPolicyChecker interface contract

`IPolicyBundleProvider` and `IPolicyChecker` are the key seam for policy abstraction:

```
LocalFilePolicyBundleProvider  (sprint start — local .wasm file)
    ↓ swap at runtime
GrpcPolicyBundleProvider       (after NodeForge PolicyService is ready)
```

Do not hardcode file paths into `WasmPolicyChecker`. Provider must be injectable.
Interface must be finalized before implementation to minimize swap cost.

## 5. gRPC client responsibility

NodeKit is a **gRPC client only**. It sends `BuildRequest` and receives status/results.
Do not implement gRPC server logic in NodeKit. The proto contract is the boundary —
any change to `.proto` definitions requires coordination with NodeForge.

## 6. Decision checklist before every change

- Does it add K8s API calls, Job scheduling, or image build logic to NodeKit? **Block.**
- Does it add `RegisteredToolDefinition` creation logic to NodeKit? **Block.**
- Does it relax a reproducibility rule (latest tag, digest, version pinning)? **Block.**
- Does it hardcode a policy bundle file path bypassing `IPolicyBundleProvider`? **Block.**
- Does it couple NodeKit to DagEdit internals? **Block.**

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
- `AdminToolList` displaying stale data after successful registration
