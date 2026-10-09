# P01.contract fixtures

These files freeze the shared contract of the NodeKit self-CLI sprint (approved v0.9.1)
so that later stages build against one set of inputs and expectations.

| File | What it freezes | Implemented by |
|---|---|---|
| `cli-acceptance-contract.json` | `SchemaVersion=draft-1` gate and its rejection variants, support profile, 6 methods × 3 input modes (18 cells), fixture names, recommended command grammar, submit-block disposition (FR-020/021, SC-005, …), criterion→stage map | P01.support, P02, P04/P05, P07 |
| `toolfunction-persisted.json` | raw_spec.v1 exact 4 keys, typed RegisterToolFunction request/response fields, A/B/C1/C2 and F/H/P/version roles, OBSERVED_SAME_BUILD / MANUAL_UNVERIFIED boundary | P06, P07, P08 |
| `contract-source-manifest.json` | pinned NodeVault proto and raw_spec schema (revision, blob, sha256) and the consumer code the map was read from | — |
| `portable-authoring-transfer.json` | Recipe reuse on another install: relative companion files, absolute-path rejection, image-internal paths, source binding key | P04, P06 |

Every expected value is a target for the stage named on it. `verification: NOT_RUN` means no
stage has produced evidence yet; nothing here is a PASS.

`ContractFixtureFreezeTests` parses all four files and checks them against the current code:
method catalog and kind resolver, Recipe model properties, the vendored proto descriptor,
`protos/provenance.json` and the spec anchors. If one of those changes, update the fixture in
the same PR.
