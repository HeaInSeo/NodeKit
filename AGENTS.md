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
- **NodeVault Phase 1 gate opened 2026-07-02.** The CLI (`src/NodeKit.Cli/`) migrated to the production `ToolSpecRequest` path (`ResolveToolSpec → SubmitToolBuild → WatchToolBuild`); the `--legacy` flag and the `BuildAndRegister` path were removed from the CLI. Do not reintroduce a `--legacy`-style path in the CLI.
- **Sprint 7 Task 1 complete (2026-07-14).** `IBuildClient`/`GrpcBuildClient` (the legacy `BuildRequest`/`BuildAndRegister` gRPC path) have been fully removed from the repo — the Avalonia GUI (`NodeKit.csproj`) now also uses `GrpcToolSpecClient` (shared with the CLI via `src/Grpc/`), with build-submission state owned by `UI/ViewModels/BuildSubmissionViewModel.cs`. There is no legacy client code left anywhere in this repo; do not reintroduce it.
- Current work should focus on: Sprint 7 Task 2 (U5-2, seoy live manual test — see `docs/NODEKIT_SEOY_SMOKE_FIXTURES.md` for fixed repro recipes and the opt-in `GrpcToolSpecClientIntegrationTests`), L1 validation, BuildRequest mapping, gRPC client resilience, CI, lint, tests, and coverage. R22 (SourceBuild structured-intent, Issues #37-39) and three rounds of adversarial-review follow-up (Issues #41-45) are done — see `docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md` for the full history.
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
- (Historical decision rule, now moot — kept for context.) The migration criterion used before Phase 1 opened was: only move NodeKit onto the new `ToolSpecRequest` path once NodeVault has a complete `SubmitToolBuild`/watch/cancel path, not just `ResolveToolSpec`. That gate opened 2026-07-02 and the legacy `BuildRequest`/`BuildAndRegister` path has since been fully removed from this repo (see above) — there is no legacy fallback to revert to.

Remote integration rule:

- NodeVault and related platform services are tested on remote infrastructure by default.
- Discover live/remote environment details from documents under `~/.config/infra-lab`.
- Do not assume NodeVault is running locally. Keep NodeKit local tests independent of live NodeVault unless an integration test is explicitly opted in.
- NodeKit must remain a client of the documented NodeVault gRPC/REST endpoints; it must not require direct Kubernetes API access.
