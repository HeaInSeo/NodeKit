# NodeKit ↔ NodeVault Adversarial Review Briefing

Prepared: 2026-07-13, from the NodeKit side, for an adversarial cross-repo
review. The reviewing agent should treat every claim below as something to
independently re-verify against the actual code in both repos as of the
review date — this document is a snapshot, not a guarantee, and it was
written by the same kind of agent that wrote the code it describes.

Repos:

- NodeKit: `/opt/dotnet/src/github.com/HeaInSeo/NodeKit` (this repo, C#/.NET, CLI + Avalonia GUI)
- NodeVault: `/opt/go/src/github.com/HeaInSeo/NodeVault` (Go, K8s-based build/registry backend)

## 1. Ownership boundary (from NodeKit's CLAUDE.md, should hold from both sides)

NodeKit owns: ToolDefinition/DataDefinition authoring, L1 static validation,
DockGuard policy execution (client-side), BuildRequest/DataRegisterRequest
construction and gRPC transmission, AdminToolList/AdminDataList display via
Catalog 서비스 REST API.

NodeVault owns: BuildRequest reception, build orchestration (L2-L4), OCI
referrer push, artifact index (SoT), DockGuard policy bundle management,
Harbor lifecycle.

NodeKit must not implement K8s API calls, Job scheduling, or image-build
logic. NodeVault must not require NodeKit to reach into Kubernetes directly.
The `.proto` file is the contract boundary — a reviewer should flag anything
that couples the two repos outside that contract.

## 2. HIGH-PRIORITY CONFIRMED FINDING: legacy SourceBuild is now rejected by live NodeVault

This is not a hypothesis — it follows directly from reading both sides' code
as of today (2026-07-13) and should be the review's first thing to verify
independently, then act on.

**NodeVault side** (`pkg/build/validate.go`, commit `645c594`, "Sprint 9
P2a", landed 2026-07-13 13:39): `ValidateBuildRequest` now statically scans
RUN instructions **after the last FROM** in `dockerfile_content` for a fixed
risky-tool list:

```go
var riskyRuntimeTools = map[string]bool{
    "curl": true, "wget": true, "git": true, "ssh": true, "scp": true,
    "apt": true, "apt-get": true, "apk": true, "yum": true, "dnf": true,
    "gcc": true, "g++": true, "clang": true, "make": true, "cmake": true,
}
```

For a **single-stage** Dockerfile (only one `FROM`), "after the last FROM"
is the entire body — every RUN line is scanned. Rejection can only be
avoided via a new `allow_runtime_tools` + `allow_runtime_tools_reason`
field pair on `BuildRequest` (proto field numbers 18/19), which requires an
explicit, non-empty reason.

**NodeKit side**: `RecipeRenderer.RenderSourceBuild` (legacy
`RecipeBuildKind.SourceBuild`, still offered in the interactive wizard as
`RecipeMethodId.Source` with `Warning: null` — see
`src/Authoring/Recipes/RecipeMethodCatalog.cs`) renders a **single-stage**
Dockerfile whose one RUN line is:

```
RUN curl -fsSL -o source.tar.gz "<SourceUri>" && echo "<checksum>  source.tar.gz" | sha256sum -c - && tar -xzf source.tar.gz && <SourceBuildCommands...>
```

`curl` is always present. `SourceBuildCommands` commonly includes `make`
(see the method's own field examples/tests — `"make"`, `"make install"`),
which is also on the risky list. NodeKit has no code path that sets
`allow_runtime_tools` (see §3 — the field doesn't even exist in NodeKit's
vendored proto yet).

**Conclusion**: as of today, submitting a recipe authored with the plain
"소스코드로 직접 빌드하기" (`RecipeMethodId.Source`) wizard option — NodeKit's
own unwarned, fully-offered option — will be rejected by NodeVault's build
gate 100% of the time. `RecipeBuildKind.SourceBuildStructured` (this
session's R22-C work, commit `3117f8a`, also landed 2026-07-13) renders a
genuine 2-stage Dockerfile whose final stage has no RUN line at all (just
`COPY --from=builder` + `USER`), so it should **not** trip this check — but
that has only been checked by local `buildah bud` (see
`docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md` §13 Sprint R22-C Progress block),
never against a live NodeVault instance running the new P2a check. Interesting
detail from NodeVault's own commit message: the author was aware of
NodeKit's `SourceBuildStructured` design doc and *believed at the time* it
was "not implemented either" — both sides' changes landed on the same day,
so there was a real coordination gap, closed only by accident of timing.

**What the review should verify / NodeKit should do something about:**

1. Confirm the above reasoning by actually running a legacy SourceBuild
   recipe against a live (or freshly-built) NodeVault with this commit, and
   confirm it is in fact rejected.
2. Confirm `RecipeBuildKind.SourceBuildStructured`'s 2-stage output is in
   fact accepted (no RUN in the final stage → should pass cleanly) —
   ideally against a live NodeVault build, not just local buildah.
3. NodeKit's wizard still offers legacy `RecipeMethodId.Source` with no
   warning and no pointer to `SourceStructured`. This needs a decision:
   deprecate/hide it, add a warning, or wait for NodeVault's
   `allow_runtime_tools` support to be exposed on the NodeKit side (which
   would defeat the purpose of the whole R22 effort — not recommended).
   This wasn't in scope for any commit made so far this session and no
   NodeKit-side fix has been made yet — flagging it here specifically so it
   isn't lost.

## 3. Vendored proto is stale (mechanical, but has real consequences)

NodeKit vendors a copy of the proto at
`protos/nodevault/v1/nodevault.proto`. Diffing it against NodeVault's
canonical copy today shows real behavioral additions NodeKit doesn't know
about yet, not just comment/doc drift:

- `BuildRequest.allow_runtime_tools` (field 18, `repeated string`) and
  `allow_runtime_tools_reason` (field 19, `string`) — the escape hatch from
  §2, added by NodeVault commit `645c594`. NodeKit has no field, no UI, no
  CLI flag for this.
- `BuildEvent.image_ref` (7), `image_digest` (8), `spec_referrer_digest` (9),
  `integrity_health` (10) — added by NodeVault commit `03f5025` ("Sprint 7
  P1a: build_state 아티팩트 메타데이터 브릿지"), explicitly so that
  `WatchToolBuild` can expose digest/referrer/health data "without NodeKit
  scraping logs or reading NodeVault's private index state." This is very
  likely a direct fix for the exact problem NodeKit's own **Sprint R18
  ("Digest Observability Fallback")** worked around — see
  `docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md` §13 Sprint R18, commit `c23e026`,
  2026-07-09. R18's fallback (`digestReceived` flag → print an explicit "check
  the index yourself" notice on `Succeeded` without a prior
  `DigestAcquired` event) still works, but if NodeVault is now reliably
  sending this data, NodeKit could remove the workaround and consume the
  richer fields directly. **Not yet re-vendored, not yet consumed.**
- `BuildAndRegister` in NodeVault's proto now carries an explicit
  `// Deprecated: use ResolveToolSpec + SubmitToolBuild + WatchToolBuild`
  comment. This directly satisfies NodeKit's own pending
  **Sprint 7 Task 3** ("NodeVault 측 BuildAndRegister deprecated 표시") —
  worth closing out that checklist item now that it's independently
  confirmed done on the NodeVault side.

None of this is a wire-protocol break by itself (new proto3 fields are
additive/optional, so an old client omitting them is fine — NodeVault will
just see zero-values, e.g. no `allow_runtime_tools` entries, which is a
safe default). The consequence is purely functional: NodeKit is running
blind to capabilities NodeVault already ships. The review should confirm
whether NodeKit intends to re-vendor soon, and whether anything currently
in flight (Avalonia GUI migration, Sprint 7 Task 1) should wait for it.

## 4. NodeVault schedule state relevant to NodeKit (read from `docs/PLATFORM_SCHEDULE.md`, today)

- Sprint 5, 6, 7 (P0/P1a), 9 (P2a): **complete**.
- Sprint 8: on hold, no current consumer.
- **Sprint 10 (P2b, post-build image content scan)**: not started, blocked
  on an upstream precondition ("podbridge5 issue #2"). This is the piece
  that would catch a base image that *already ships* curl/wget/etc. (e.g.
  `curlimages/curl`, a real live-test failure case cited in NodeVault's own
  docs) — Sprint 9's static text scan cannot see that. **This means R22-C's
  2-stage design still has a real, currently-open gap**: NodeKit's curated
  `RuntimeProfileCatalog` "minimal" profile (`debian:bookworm-slim`) was
  hand-verified via `buildah run` to lack curl/wget/etc, but nothing
  server-side confirms that for a user-supplied "advanced" runtime image
  override, and won't until Sprint 10 ships. This is the same caveat
  already tracked in NodeKit's own docs (design doc §2.6 Q5/§8, Issue #38
  resolution comment) — the review should confirm that caveat is still
  accurately worded now that Sprint 9 (a *different* piece of the same gap)
  has shipped, since it's easy for that wording to go stale in an
  overly-optimistic *or* overly-pessimistic direction.
- **Sprint 11 (P3, pinning_status/reproducibility_status)**: not started.
  Explicitly notes that accepting `VersionOnly` pins ("loose mode",
  AC-PIN-03) is "a separate decision requiring NodeKit's agreement" and is
  out of scope until that agreement happens. NodeKit's current behavior
  (see Sprint R19, `PackagePinClassifier`) already allows `name=version`
  pins by default and only blocks them under `--strict-reproducible` —
  worth the review confirming both sides describe the same target behavior
  for "loose mode" before Sprint 11 starts, so it isn't designed against a
  stale understanding of what NodeKit does today.

## 5. Known, previously-documented gap (still accurate, now partially narrower)

NodeKit's own design doc
(`docs/NODEKIT_SOURCEBUILD_STRUCTURED_INTENT_DESIGN.md` §2.6 Q5, §8) and
Issue #38's resolution both state: "NodeVault has no server-side enforcement
that the runtime stage stays clean; this is authoring-time UX only." As of
Sprint 9 P2a landing today, that statement is **no longer fully true** —
there is now real server-side enforcement for the "explicit RUN of a risky
tool in the final stage" case (with Conda/Micromamba/SourceBuild-legacy
carve-outs/breakage as described in §2 above). It remains true only for the
"risky tool already baked into the base image" case, which needs Sprint 10.
NodeKit's docs should be updated to narrow this claim rather than continue
stating the broader "no enforcement at all" version — the review should
flag every place this claim appears (grep for "서버 쪽 강제" /
"server-side enforcement" across `docs/`) as needing a wording pass.

## 6. Suggested review angles

- Build a real NodeVault image from current `main` and actually submit both
  a legacy `SourceBuild` recipe and a `SourceBuildStructured` recipe through
  it — confirm §2's reasoning empirically rather than by code-reading alone
  (this document's author did not have a live NodeVault instance to test
  against and relied on static reading + local `buildah`, which is a real
  limitation of this briefing).
- Check whether `allow_runtime_tools` should ever be exposed through
  NodeKit at all, or whether the intended fix is fully "make legacy
  SourceBuild go away in favor of SourceBuildStructured" — these are very
  different product decisions and the review should force a clear answer
  rather than let both remain half-true.
- Check for other NodeVault-side static-analysis-style checks (conda pin
  format, digest pinning, etc.) that NodeKit's own L1 validators might
  duplicate imperfectly or contradict — the regex-anchor bug fixed in
  NodeKit today (commit `53349dc`, Issue #40 — `$` tolerating a trailing
  newline in `^...$`-anchored .NET regexes) is exactly the kind of
  validate/render mismatch class worth checking for on the Go side too,
  even though Go's `regexp` package doesn't share that specific `$`
  semantics (RE2 `$` without `(?m)` matches only true end-of-text, so this
  *specific* bug shouldn't reproduce in Go — but the general "validator
  checks something slightly different from what the renderer/builder
  actually consumes" pattern is worth an adversarial look on both sides).
- Confirm NodeKit's Avalonia GUI (`NodeKit.csproj`, still on
  `IBuildClient`/`GrpcBuildClient` → legacy `BuildAndRegister`, Sprint 7
  Task 1, not started) still works against current NodeVault, given
  `BuildAndRegister` is now marked deprecated-but-not-removed. Confirm
  there's no NodeVault-side plan to actually remove it before NodeKit's
  GUI migration lands.

## 7. What NOT to worry about

- The wire protocol itself (message shapes for fields both sides already
  agree on) has no known incompatibility — new fields are additive.
- NodeKit does not call any NodeVault-internal/K8s API directly; the
  boundary in §1 is intact as far as this author could tell.
- R22-C's own client-side rendering logic (2-stage Dockerfile synthesis)
  was verified against real `buildah bud` output this session, not just
  unit tests — see `docs/NODEKIT_CLI_FIRST_SPRINT_PLAN.md` §13 Sprint
  R22-C Progress block for the exact verification steps and commands used.
