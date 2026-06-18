# NodeKit Tool Runtime Image POC Notes

Status: historical POC notes

The removed `NodeKit_POC/` directory was an independent Avalonia app for exploring a future
`Tool Runtime Image` authoring workflow. It was not wired into the current NodeKit legacy
`BuildRequest / BuildAndRegister` path.

Useful ideas preserved from the POC:

- Connected / disconnected source selection.
- Tool source routes:
  - external Bioconda
  - GitHub release
  - OCI registry
  - local package mirror
  - internal seed
- Fixture tools: `bwa`, `samtools`, `gatk`, `fastqc`.
- Candidate normalization flow:
  `ToolSourceCandidate -> ToolInstallRecipe -> GeneratedToolImageRecipe`.
- Preview artifacts:
  - `environment.yml`
  - multi-stage Dockerfile
  - lock metadata
  - reproducibility fingerprint
- Policy direction:
  - one image equals one primary tool runtime
  - seed runtime image should not hard-code execution entrypoint
  - actual command/script/entrypoint belongs to later wrapper or DAG node stages

Do not implement this path in production until NodeVault exposes the complete
`ToolSpecRequest -> ResolvedToolSpec -> SubmitToolBuild` migration path and the active
NodeKit sprint plan allows it.
