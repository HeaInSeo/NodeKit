# Avalonia v16 authoring spike findings v0.1

Status: **discovery evidence — not a Design Freeze and not a normative Rule Set revision**

## 1. Evidence collected

The v16 Data Authoring spike has now been rendered in two independent Avalonia environments.

### Linux / Xvfb layout probes

Runner screen reported by Avalonia:

- Bounds: `1280 × 1024 px`
- WorkingArea: `1280 × 1024 px`
- Scaling: `1.0`

Captured logical client budgets:

- `1093 × 614` — proxy for 1366×768 at 125% before OS working-area deductions
- `1280 × 720` — proxy for 1920×1080 at 150% before OS working-area deductions
- `980 × 600` — current minimum-size hypothesis
- capture-only unclamped stress: `1093 × 560`, `980 × 560`, `900 × 560`, `900 × 520`

Linux does not provide a useful Korean fallback font in this runner image, so these frames are geometry/clipping evidence only.

### GitHub-hosted Windows renderer probes

Avalonia reported the hosted Windows runner as:

- OS: `Microsoft Windows NT 10.0.26100.0`
- Screen Bounds: `1024 × 768 px`
- WorkingArea: `1024 × 720 px`
- Scaling: `1.0`

This environment renders the Korean UI correctly and is therefore useful for native Windows font metrics and platform-layout comparison.

Captured usable frames:

- `980 × 600`
- capture-only unclamped stress: `980 × 560`, `900 × 560`, `900 × 520`
- keyboard-navigation focus: Center input at `980 × 600`
- keyboard-navigation focus: Activity at `980 × 600`

Requests for `1093 × 614` and `1280 × 720` were width-clamped by the hosted runner's 1024px display and must **not** be treated as evidence for those requested widths.

## 2. What survived native rendering

Across Linux and Windows, `980 × 560` keeps the core structure intact:

- global Activity remains visible
- rail and Data surface navigation remain usable
- Center remains the canonical authoring owner
- Supporting remains a distinct secondary column
- the Outcome → Next Action footer remains visible and readable, although it wraps more tightly
- the Center ScrollViewer continues to provide access to sections below the fold
- no horizontal application scroll is required

This is stronger evidence than the historical fixed 1280×840 HTML canvas because it exercises the actual Avalonia renderer and a materially smaller logical client area.

## 3. First clear degradation point found

At `900 × 560` on Windows, the layout is still technically operable but visible width pressure appears:

- the page subtitle is compressed against the `초안` badge
- Center explanatory text wraps aggressively
- the source URI field becomes visually truncated within the reduced input width
- Center loses comfortable reading width because rail + Left + Supporting still consume fixed columns

At `900 × 520`, the same width pressure remains while vertical scrolling increases. The vertical reduction itself does not create a new semantic failure before the width pressure does.

### Current inference

- `900` is **below the comfortable width** for the current four-column structure unless a new adaptive Supporting/Left behavior is designed.
- `980` remains a defensible provisional width floor for this spike.
- `600` appears conservative as a height floor; `560` is now a viable next hypothesis because it survived both Linux and Windows renderer probes at width `980`.

This is still an inference, not a product requirement.

## 4. Typography finding

The Windows renderer probe is the first artifact in this spike that shows the Korean UI with a native Windows fallback font rather than missing-glyph boxes.

The carried `9.5` Supporting kicker value (`증거와 제안`) is visibly smaller than the surrounding information hierarchy. Therefore:

- `9.5` must **not** become a normative R1 floor
- it should be treated as a migration probe only
- the next typography candidate should test at least `10.5` for this secondary label role

The current `10–11` technical/helper values remain readable in the hosted 100% Windows renderer, but this does **not** prove their readability at 125%/150% user scaling on the target devices.

## 5. Keyboard-focus finding

The capture harness now applies focus using keyboard-navigation semantics rather than pointer/programmatic focus alone.

At `980 × 600` on the hosted Windows renderer:

- the first Center TextBox shows a high-visibility blue focus border
- the global Activity control shows a high-visibility light focus outline
- both remain visually distinguishable from their unfocused surrounding controls

Current verdict: **native focus visibility PASS for this spike at the hosted Windows runner's 100% scaling**.

Do not add an explicit custom focus-ring override solely to solve a defect that is not reproduced here. Re-open this only if the real 125%/150% device captures show the native theme focus treatment becoming ambiguous.

## 6. What this evidence does not prove

The hosted Windows runner is `1024 × 768 @ 100%` with a `1024 × 720` working area. It cannot reproduce the two required target device cases:

- `1366 × 768 @ 125%`
- `1920 × 1080 @ 150%`

Also, `RenderTargetBitmap` captures the Avalonia client visual, not the native Windows title bar. Therefore these CI artifacts do not close the native-device gate.

The spike records Avalonia Screen Bounds, WorkingArea, and Scaling in every capture JSON so the eventual device captures can be compared using actual working-area evidence rather than nominal display resolution.

## 7. Current decisions after these probes

Do **not** make the following normative yet:

- final R1 typography floor
- final product `MinWidth`
- final product `MinHeight`
- Supporting collapse breakpoint
- any new synthesized status rule / Decision 101

Keep for the next device check:

- provisional width hypothesis: `980 DIP`
- next height candidate to falsify: `560 DIP`
- typography candidate: remove the `9.5` secondary-label probe before a final floor is declared
- native focus treatment: keep unless target-device evidence falsifies it

No backend capability or registration CTA should be added as part of this sizing work.

## 8. Remaining device gate

On actual Windows devices, capture and record:

1. `1366 × 768 @ 125%`
2. `1920 × 1080 @ 150%`
3. actual Screen Bounds, WorkingArea, and Scaling
4. full window at startup
5. Center scrolled to section 04
6. keyboard focus on a Center input
7. keyboard focus on Activity

If `980 × 560` remains structurally readable under those real DPI conditions, the spike may revise its minimum-height hypothesis from `600` to `560`. Only then should a product Rule Set minimum be considered.
