# Avalonia v16 authoring spike protocol v0.1

Status: **discovery spike — not production UI and not a Design Freeze**

## 1. Why this exists

The HTML Console prototypes established semantic ownership and interaction rules, but they are not the shipping renderer. The current NodeKit product is Avalonia, and the existing `MainWindow.axaml` still starts at `Width=1280 Height=900` without a native Console-system implementation.

This spike moves one v16 Data Authoring screen into the real Avalonia renderer so layout, DPI, font rendering, minimum window size, focus, and native window chrome can falsify assumptions that an HTML gate cannot test.

No backend capability is added. `DataRegisterRequest` UI/gRPC wiring remains outside this spike.

## 2. Launch

Default NodeKit launch is unchanged.

To open the discovery window:

```bash
NODEKIT_UI_SPIKE=v16 dotnet run --project NodeKit.csproj
```

On PowerShell:

```powershell
$env:NODEKIT_UI_SPIKE = 'v16'
dotnet run --project NodeKit.csproj
```

Unset `NODEKIT_UI_SPIKE` to return to the existing `MainWindow`.

## 3. What is intentionally represented

- Console top context + global Activity slot
- visible raw Activity summary: `실패 1 · 중단 1`
  - this deliberately avoids synthesized `확인 필요 2` while Decision 101 remains held
- rail + Data surface navigation
- Center-owned v16 authoring fields
- simplified Supporting evidence/proposal area
- Outcome → Next Action footer without a dead registration CTA
- default native window chrome rather than HTML pseudo window controls

## 4. What is intentionally not implemented

- Data registration submission
- Checksum proposal apply/dismiss behavior
- Activity drawer/list behavior
- Registered Data surface
- technical-detail drawer behavior
- support-panel collapse breakpoint
- final font-size/accessibility floor
- final rail iconography

The point is to discover native constraints before those behaviors are implemented.

## 5. Minimum-size hypothesis

The first spike uses:

```text
Initial: 1280 × 840 DIP
MinWidth: 980 DIP
MinHeight: 600 DIP
```

This is a measurement hypothesis, not a new product requirement.

The content grid is fluid in the Center and uses scrollable authoring content. Field/control heights are `MinHeight` rather than fixed `Height` where practical, specifically so native text growth can be observed instead of clipped.

## 6. Required Windows measurement matrix

### Case A — common laptop scaling

```text
Display: 1366 × 768
Windows scale: 125%
Approx logical working size before OS chrome: 1093 × 614 DIP
```

Capture:

1. full window at startup
2. Center scrolled to section 04
3. keyboard focus on a Center input
4. keyboard focus on Activity

Record:

- whether top Activity remains visible
- whether Center can reach all four sections without horizontal scrolling
- whether Supporting remains readable rather than crushing Center
- whether footer text wraps without clipping
- actual usable client size after native title bar/taskbar effects

### Case B — 1080p at 150%

```text
Display: 1920 × 1080
Windows scale: 150%
Approx logical display size: 1280 × 720 DIP
```

Repeat the four captures and observations above.

### Case C — stress resize

Resize toward `980 × 600`.

Record the first width/height at which any of the following occurs:

- field label/value becomes ambiguous
- Activity state summary clips
- Supporting competes with Center enough to prevent authoring
- footer meaning becomes unreadable
- rail/left navigation loses a usable target

That first failure point, not the historical 1280 HTML canvas, should inform the eventual product minimum size.

## 7. Typography comparison

Use `CONSOLE_UI_TOKEN_EXTRACTION_V0.1.md` as the comparison source.

The spike initially transfers the HTML numeric values into Avalonia `FontSize` where practical. Specifically inspect the known low values:

- 9.5 — Supporting kicker / former generic chip class
- 10 / 10.5 — technical/evidence/status text
- 11 — helper and next-action text
- 12.5 — labels
- 13.5 — inputs
- 19 — page title

Do **not** declare a normative R1 minimum until screenshots are reviewed in the native renderer.

## 8. Interaction/semantic checks for this spike

PASS only if all are true:

- default launch still opens existing `MainWindow`
- `NODEKIT_UI_SPIKE=v16` alone opens the spike window
- Activity slot is visible on the v16 surface
- Activity summary does not merge Failed and Interrupted into a new semantic status label
- Center contains authored source/checksum/display fields
- Supporting does not silently mutate Center
- no enabled registration CTA is invented
- post-registration empty evidence rows are reduced to a single explanation
- native OS window controls are used rather than non-semantic glyph spans

## 9. Evidence required before any Rule Set numeric revision

Attach to the spike report:

- Case A screenshots
- Case B screenshots
- measured client sizes
- build/test result from the branch
- list of HTML assumptions that did not survive Avalonia
- token-by-token notes for values that visually changed meaning in native rendering

Only after this evidence exists should R1 numeric floors, adaptive breakpoints, or product `MinWidth`/`MinHeight` be made normative.
