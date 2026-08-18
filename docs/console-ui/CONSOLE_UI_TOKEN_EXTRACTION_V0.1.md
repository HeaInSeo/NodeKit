# Console UI token extraction v0.1

Status: **measurement input — not a normative design floor**  
Source: `NodeKit_v16_System_Pass_P2_v0.1.1.html` (System Pass + P2 review target, SHA-256 `69bfab8f761de9b77b5ec3d9576eab73d0f97345f77747586996622797464155`)  
Purpose: carry the *effective* HTML prototype values into the first Avalonia discovery spike without pretending that CSS pixel values are already correct native-product values.

## 1. Rule for using this table

- These values are observations, not a new Decision or accessibility minimum.
- Numeric transfer from CSS `px` to Avalonia `FontSize`/DIP is a hypothesis to test, not an equivalence claim.
- Values below 10 remain listed because they are real evidence of the current prototype; they are **not** endorsed.
- The Avalonia spike must report which values survive native font rendering and Windows scaling, and which must change.
- Semantic ownership remains unchanged: Center owns canonical authoring fields; Supporting shows evidence/proposals and never silently changes Center.

## 2. Color tokens — effective prototype values

| Token | Value | Current use |
|---|---:|---|
| `bg` | `#0a0a10` | app background |
| `shell` | `#12111a` | main shell / Supporting after System Pass |
| `panel` | `#16151f` | navigation / status surfaces |
| `panel2` | `#141320` | Center header/footer |
| `panel3` | `#131220` | historical Supporting base before System Pass override |
| `line` | `#2a2a40` | primary separators |
| `line2` | `#22213a` | secondary separators |
| `text` | `#f1eef8` | primary text after System Pass |
| `text2` | `#d9d5e7` | secondary-strong text |
| `muted` | `#aaa6bf` | secondary text |
| `dim` | `#85819c` | tertiary text |
| `faint` | `#706d86` | low-priority technical text |
| `focus` | `#8298da` | keyboard focus ring |
| `amber` | `#d1a75e` | authoring/current emphasis |
| `amber2` | `#8a6a2e` | subdued amber |
| `teal` | `#7fc8c6` | Supporting/proposal emphasis |
| `blue` | `#8fa2c8` | evidence/information |
| `green` | `#5cb98c` | positive/available observation |
| `red` | `#d98d82` | failed/missing observation |

## 3. Typography — effective values

| Surface / role | Effective HTML value | Spike mapping note |
|---|---:|---|
| Page title | `19px` | transfer as Avalonia `19` for first comparison |
| Page subtitle | `13px` | transfer as `13` |
| Section title | `14.5px` | transfer as `14.5` |
| Section explanatory text | `12px` | transfer as `12` |
| Field label | `12.5px` | transfer as `12.5` |
| Field input | `13.5px` | transfer as `13.5` |
| Field helper | `11px` | transfer as `11` |
| Left surface item | `13px` | transfer as `13` |
| Left page item | `12.5px` | page TOC is omitted from the first native spike; do not promote this token yet |
| Left note title/body | `12px / 11.5px` | transfer for comparison |
| Center result title / next action | `12px / 11px` | transfer for comparison |
| Supporting heading | `12.5px` | transfer as `12.5` |
| Supporting group title | `11.5px` | transfer as `11.5` |
| Supporting paragraph | `11px` | transfer as `11` |
| Supporting evidence rows | `10.5px` | transfer only to expose native readability; not a floor |
| Supporting code | `10px` | transfer only to expose native readability; not a floor |
| Supporting kicker | `9.5px` | known review concern; keep only as a probe value |
| State label | `10.5px` | transfer as probe |
| Generic chip | `9.5px` | known review concern; keep only as a probe value |
| Status bar | `10px` | transfer as probe |

## 4. Geometry — effective prototype values

| Token / surface | Effective HTML value | Native spike treatment |
|---|---:|---|
| HTML app shell | `1280 × 840` fixed | **not carried as a fixed constraint**; only the initial window size uses 1280×840 |
| Top bar | `40` high | native uses content-sized `Auto`, 40 as minimum visual target |
| Rail | `38` wide | spike widens it slightly for visible labels; measure trade-off rather than canonize |
| Left context | `184` wide | first native hypothesis: ~176–184 |
| Supporting | `218` wide | first native hypothesis: ~218–230 |
| Center header | `82` high | native uses content-sized row; 82 is comparison evidence |
| Center footer | `58` high | native uses content-sized row; 58 is comparison evidence |
| Left surface item | `30` min-height | use `MinHeight`, not fixed `Height` |
| Field input | `34` high | use `MinHeight=34`, not fixed `Height` |
| Field textarea | `56` high | use `MinHeight=56`, not fixed `Height` |
| Supporting header | `62` high | native uses content-sized row |
| Supporting tab | `29` high | not part of the first spike unless the tab survives simplification |
| Supporting action | `30` high | use as comparison only |
| Evidence row | `33` min-height | use `MinHeight`, not fixed `Height` |
| Registered-data head/row | `32 / 42` | not part of the first v16-authoring native spike |
| Status bar | `28` high | native uses content-sized row |

## 5. State color observations

The prototype currently assigns colors per raw status (`green`, `blue`, `red`, `gray`). Red Team review found that adjacent lifecycle/integrity columns can use different colors for equally "normal" states, which weakens scan meaning. Do not treat the present mapping as a native token contract.

For the v16 authoring spike only:

- amber = authoring/current emphasis
- teal = proposal/supporting emphasis
- red = concrete failure/missing only
- neutral = absence/not-yet-created

Lifecycle/integrity status color redesign is deferred because the first spike does not include Registered Data.

## 6. Known defects intentionally carried as probes

The following are evidence, not acceptance criteria:

- `9.5px` chip/kicker values survive in the HTML target.
- the HTML shell is fixed at `1280×840` with `overflow:hidden`.
- System Pass values are layered through overrides rather than owned as base tokens.

The native spike should answer whether the numeric font values are readable in Avalonia and whether a fluid shell with `MinWidth`/`MinHeight` can support the target Windows scaling cases without losing information.
