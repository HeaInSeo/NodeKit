# NodeKit Agent Memory

Read these first when resuming work:

1. `docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md`
2. `CLAUDE.md`
3. `docs/ARCHITECTURE.md`
4. `/opt/go/src/github.com/HeaInSeo/NodeVault/docs/PLATFORM_SCHEDULE.md`
5. `/opt/go/src/github.com/HeaInSeo/NodeVault/docs/PLATFORM_MAP.md`
6. `/opt/go/src/github.com/HeaInSeo/NodeVault/protos/nodevault/v1/nodevault.proto`

Active planning rule:

- NodeVault is a Kubernetes data-plane app. NodeKit integrates with it through the documented gRPC/REST API surface, not by calling the Kubernetes API directly.
- **NodeVault Phase 1 gate opened 2026-07-02.** The CLI (`src/NodeKit.Cli/`) has already migrated to the production `ToolSpecRequest` path (`ResolveToolSpec → SubmitToolBuild → WatchToolBuild`); the `--legacy` flag and the `BuildAndRegister` path were removed from the CLI. Do not reintroduce a `--legacy`-style path in the CLI.
- `IBuildClient` / `GrpcBuildClient` (the legacy `BuildRequest` / `BuildAndRegister` gRPC path) remain only in the Avalonia GUI project (`NodeKit.csproj`) — migrating the GUI to `GrpcToolSpecClient` is Sprint 7, still in progress. Do not remove the legacy client code from the GUI project until that migration lands.
- Current work should focus on: Sprint 7 (Avalonia GUI ToolSpec migration), `docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md` §13 (R18-R21, completed) follow-up work, R22 SourceBuild structured-intent implementation (Issues #37-39, design finalized in `docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md`), L1 validation, BuildRequest mapping, gRPC client resilience, CI, lint, tests, and coverage.
- New application state should follow the DagEdit direction: ReactiveUI / System.Reactive first, with DynamicData when collection change streams are useful.
- The existing UI is still largely code-behind/event-handler based. Do not expand that pattern for new validation, submission progress, or result state; introduce focused reactive ViewModel/state objects instead.
- CI includes reactive architecture guard tests. If `MainWindow.axaml.cs` grows, gains more click subscriptions, or gains more `async void` handlers, move the work into reactive ViewModel/state code instead of raising the baseline.
- Commercial guardrails are active: NuGet versions must be explicit, lock files must stay committed, CI uses `dotnet restore --locked-mode`, tests run on xUnit v3 through Microsoft.Testing.Platform, vulnerable packages fail package audit, coverage must stay above the committed baseline, and direct Kubernetes client dependencies are blocked.

Historical documents under `docs/obsolete/` are preserved for context only. They do not override the active sprint plan.

Session-start NodeVault check:

- Inspect NodeVault status without editing it:
  `git -C /opt/go/src/github.com/HeaInSeo/NodeVault status --short --branch`
- Read the current NodeVault planning/API surface before NodeKit API work:
  `docs/PLATFORM_SCHEDULE.md`, `docs/PLATFORM_MAP.md`, and `protos/nodevault/v1/nodevault.proto`.
- Treat NodeVault planning documents as the upstream platform source of truth. Reconcile NodeKit documents and implementation to that state.
- If NodeVault has `ResolveToolSpec` but not a complete `SubmitToolBuild`/watch/cancel path, keep NodeKit on legacy `BuildRequest` / `BuildAndRegister`.

Remote integration rule:

- NodeVault and related platform services are tested on remote infrastructure by default.
- Discover live/remote environment details from documents under `~/.config/infra-lab`.
- Do not assume NodeVault is running locally. Keep NodeKit local tests independent of live NodeVault unless an integration test is explicitly opted in.
- NodeKit must remain a client of the documented NodeVault gRPC/REST endpoints; it must not require direct Kubernetes API access.
