# Conda/Micromamba builder → runtime split POC

Status: **experimental / non-production**.

This POC exists to answer a narrow architecture question before changing NodeKit's production renderer:

> Can a ToolSpec package build use Conda/Micromamba only in a builder stage, copy the resulting fixed-prefix environment into a smaller final runtime image, remove the package manager/build helpers from the final image, and still run the declared tool directly without assuming `/bin/sh`?

## What is tested

Current NodeKit catalog builder families are used deliberately:

- `condaforge/miniforge3:24.3.0-0`
- `mambaorg/micromamba:1.5.8`

Representative Bioconda tools:

- `bwa=0.7.17`
- `samtools=1.20`
- `bcftools=1.20`

Each tool is installed into the fixed prefix `/opt/nodekit/env` and copied unchanged into three runtime targets:

1. `debian:bookworm-slim` — compatibility baseline.
2. `gcr.io/distroless/base-debian12:nonroot` — shell-less runtime candidate.
3. `scratch` — deliberately aggressive baseline used to expose intrinsic libc/loader requirements; failure is informative and is not treated as a POC infrastructure failure.

## Validation rule

The runtime tool is invoked by **absolute executable path + argv**, never by `/bin/sh -c`, `which`, `test`, or image default `ENTRYPOINT/CMD`.

Examples:

- `/opt/nodekit/env/bin/bwa`
- `/opt/nodekit/env/bin/samtools --version`
- `/opt/nodekit/env/bin/bcftools --version`

The runner records exit code and output/version evidence. Runtime filesystem hygiene is inspected externally with `docker export` + host-side `tar`; it does not execute helper tools inside the candidate image.

## Non-goals

- This does not change production NodeKit code.
- Package builds are version-pinned for the experiment, but the solver-selected build string is captured as evidence rather than being promoted to a production contract here.
- This does not yet define the final `RuntimeProfile` schema or UI.
- A successful version probe does not prove functional correctness on real input fixtures; that remains a later ToolFunctionSpec concern.
