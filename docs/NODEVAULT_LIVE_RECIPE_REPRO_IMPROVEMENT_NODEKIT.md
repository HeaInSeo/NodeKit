# NodeKit Reproducibility Improvement Plan

Date: 2026-07-08
Scope: NodeKit recipe authoring, SourceBuild UX, submit/watch output, Conda/Micromamba pinning
Related NodeVault reports:

- `docs/NODEKIT_LIVE_RECIPE_REPRO_TEST_2026-07-08.md`
- `docs/NODEKIT_LIVE_RECIPE_EXTENDED_TEST_2026-07-08.md`

## Background

The live NodeKit to NodeVault tests confirmed that NodeKit can create recipes, submit them to NodeVault, and watch successful builds through the Kubernetes data-plane path.

Working methods:

```text
DockerfileFallback
BioContainer
Conda
Micromamba
PackageMirror
SourceBuild with curl-capable base image
```

NodeKit-side gaps found by the tests:

```text
1. SourceBuild recipes assume the selected base image already has curl, tar, and sha256sum.
2. BuildDependencies exists in the recipe surface but is not actionable in renderer/validation.
3. SourceBuild can encourage final images that retain fetch/build tools.
4. submit/watch output does not show the final pushed image digest.
5. Conda/Micromamba full pin UX is inconsistent across create, resolve, and submit.
```

## Design Position

NodeKit should make reproducibility choices explicit at authoring time.

NodeKit does not need to own the full build pipeline, but it should not produce recipes that look reproducible while depending on hidden base-image tooling or loose package pins.

Important boundary:

```text
NodeKit
= authoring UX, recipe validation, candidate selection, submit/watch display

NodeVault
= build execution, registry/referrer/reconcile, final image policy, durable artifact metadata
```

## SourceBuild UX and Recipe Contract

### Problem

The current SourceBuild output is effectively:

```dockerfile
FROM <BaseImage>

RUN curl -fsSL -o source.tar.gz "<SourceUri>" && \
    echo "<sha256>  source.tar.gz" | sha256sum -c - && \
    tar -xzf source.tar.gz && \
    <SourceBuildCommands>

USER 1000
```

This fails when the base image does not include `curl`, `sha256sum`, or `tar`.

Observed live behavior:

```text
alpine:3.20
=> curl missing
=> failed

condaforge/miniforge3
=> curl missing
=> failed

curlimages/curl
=> curl present
=> passed, but not a production-grade final runtime base
```

### Target UX

NodeKit should guide users toward multi-stage SourceBuild recipes:

```text
fetch/build stage
=> may contain curl, wget, tar, sha256sum, git, compiler, package manager

final ToolSpec image
=> should contain only the genome tool, runtime libraries, entrypoint/user
```

Conceptual output:

```dockerfile
FROM <fetch-image>@sha256:<digest> AS fetcher
RUN curl -fsSL -o source.tar.gz "<SourceUri>" && \
    echo "<sha256>  source.tar.gz" | sha256sum -c -

FROM <builder-image>@sha256:<digest> AS builder
COPY --from=fetcher /path/source.tar.gz /tmp/source.tar.gz
RUN tar -xzf /tmp/source.tar.gz && \
    <SourceBuildCommands>

FROM <runtime-image>@sha256:<digest> AS final
COPY --from=builder /built/tool /usr/local/bin/tool
USER 1000
ENTRYPOINT ["tool"]
```

## BuildDependencies / RuntimeDependencies

NodeKit should split dependency meaning clearly:

```text
BuildDependencies
= tools used only during fetch/build/checksum/compile stages

RuntimeDependencies
= dependencies expected to remain in the final ToolSpec image
```

Initial conservative policy:

```text
1. Treat BuildDependencies as build-stage-only metadata.
2. Warn if the current renderer cannot install or place them in a build stage.
3. Require SourceBuild recipes to declare how fetch/checksum/extract tools are satisfied.
4. Warn or block if a known fetch/build image is selected as the final runtime base.
```

Dependency modes can be added later:

```text
preinstalled
apk
apt
conda
micromamba
```

But automatic installation should not be added without pin/snapshot policy. Otherwise NodeKit would create recipes that are convenient but not reproducible.

Acceptance criteria:

```text
AC-SB-01: SourceBuild authoring warns when required fetch/checksum/extract tools are not accounted for.
AC-SB-02: BuildDependencies is described as build-stage-only.
AC-SB-03: RuntimeDependencies is separate from BuildDependencies.
AC-SB-04: curlimages/curl and similar images are warned or blocked as final runtime bases.
```

## Risky Final Base Warnings

NodeKit should warn when a user selects these as final runtime bases:

```text
curlimages/curl
wget-focused images
git-focused images
compiler/buildpack images
debug/toolbox images
package-manager-heavy images
```

Allowed pattern:

```text
curlimages/curl as fetcher
=> OK

curlimages/curl as final
=> warn or block
```

## Submit/Watch Artifact Output

The live test showed that `nodekit build submit` can reach success while only printing coarse states:

```text
[로그] build state: Building
[로그] build state: Pushing
[성공] build state: Succeeded
```

NodeKit should print artifact identity when NodeVault provides it:

```text
PUSH_SUCCEEDED image_ref=...
DIGEST_ACQUIRED image_digest=sha256:...
SPEC_REFERRER_PUSHED spec_referrer_digest=sha256:...
integrity_health=Complete
```

Short-term behavior:

```text
If NodeVault exposes image_digest on final build state, print it at Succeeded.
If NodeVault exposes event stream metadata, print structured digest/referrer events.
If integrity_health is Partial, surface it as a warning.
```

Acceptance criteria:

```text
AC-EVT-01: A successful submit prints the final image digest when NodeVault provides it.
AC-EVT-02: Partial integrity is visible in CLI output.
AC-EVT-03: CLI output is sufficient for a user to identify the exact pushed artifact.
```

## Conda / Micromamba Pinning UX

### Current mismatch

NodeKit authoring can allow:

```text
bwa=0.7.17
```

NodeVault reproducibility gate expects:

```text
bwa=0.7.17=h84994c4_5
```

This creates a user-facing mismatch:

```text
NodeKit creates a recipe
NodeVault rejects it later
```

### Proposed UX

NodeKit should offer explicit pin modes:

```text
--pin-mode select
  ResolveRecipe candidates must be shown and selected.

--pin-mode manual
  User enters name=version=build directly.

--pin-mode loose
  name=version is allowed, but submit warns that reproducibility is weak/partial.

--strict-reproducible
  submit blocks unless every package is full pinned.
```

Interactive create should make the same choices visible:

```text
1. Select a full pin candidate from NodeVault ResolveRecipe
2. Enter a full pin manually
3. Continue loose with a reproducibility warning
```

Acceptance criteria:

```text
AC-PIN-01: ResolveRecipe full pin candidates can be shown during create.
AC-PIN-02: User can choose candidate, manual full pin, or loose mode.
AC-PIN-03: strict mode blocks submit before NodeVault rejection.
AC-PIN-04: loose mode is clearly labeled as not fully reproducible.
```

## NodeKit/NodeVault Boundary

NodeKit should validate and explain what it can know locally. NodeVault remains authoritative for final reproducibility status.

NodeKit should block locally when:

```text
image refs lack digest
Dockerfile FROM lacks digest
strict reproducible mode has version-only conda pins
SourceBuild required fields are missing
known final runtime base is disallowed
```

NodeKit should warn locally when:

```text
BuildDependencies are declared but renderer/build backend cannot install them yet
SourceBuild uses a fetch/toolbox image as final base
loose pin mode is selected
NodeVault reports integrity_health=Partial
```

NodeVault should decide:

```text
final image_digest
spec_referrer_digest
integrity_health
lifecycle_phase
pinning_status
reproducibility_status
```

## Recommended Implementation Order

1. Add CLI display support for `image_digest`, `spec_referrer_digest`, and `integrity_health` once NodeVault exposes them.
2. Add pin mode UX and strict submit preflight for Conda/Micromamba.
3. Update SourceBuild authoring to require explicit fetch/build/runtime stage intent.
4. Clarify BuildDependencies and RuntimeDependencies in recipe docs and validation.
5. Add warnings or blocks for risky final runtime bases.

## Engineering Opinion

NodeKit should not hide reproducibility tradeoffs behind convenience defaults. For genomics images, `name=version` and "base probably has curl" are not strong enough.

The right compromise is not to force every user into one path. NodeKit should allow strict, manual, and loose flows, but each flow must produce an honest status. A loose pin can be useful during development, but it should never look equivalent to a full pin.

For SourceBuild, the safest first step is validation and UX clarity rather than automatic package installation. Automatic install without pinned package indexes or snapshots would create a new reproducibility problem while trying to solve the old one.
