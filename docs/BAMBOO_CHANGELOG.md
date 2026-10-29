# Bamboo preview changes

## Bamboo30 — 29 October 2026

- Halve the standard landscape pen margins introduced in bamboo29: 4.5 mm left/right and 5 mm top/bottom in both right- and left-handed modes. CTH-661 maps a centered 207.48 × 127 mm pen area. Centralize defaults and use them for native snapshot fallbacks; preserve explicitly saved margins.
- Keep the whole physical touch-active area independent of pen margins. Add four software regression cases exercising the touch filter through the clipped absolute-output pipeline and comparing edge/center movement at 0° and 180°. This adds coverage; it does not modify touch parsing or gesture production code.
- Clarify the hardware limit: touch is 190 × 130 mm, pen is approximately 216.5 × 137 mm, per Wacom's manual. Pen-only edge strips cannot be activated for touch in software.
- Tested on **macOS 27.2, Mac mini M4, Wacom Bamboo CTH-661**. Full local suite: 2,251 passed, 6 existing configuration skips, 0 failures. Swift: 22 real settings-model, 18 numeric-input and 12 search checks. ARM64 package and signature integrity verified.
- Document changing raw pressure and receiving AppKit pressure values. The user did not obtain pressure-dependent new Brush strokes in Linearity Curve 7.1.1; the cause remains unresolved. No Curve compatibility fix is included.
- Update both READMEs and add consolidated [bamboo28–30 notes](BAMBOO30_UPDATE.md). CI now runs the real native settings-model checks and packages `-bamboo30`.

## Bamboo29 — 29 October 2026

- Introduce dedicated, persisted horizontal pen margins, initially 9 mm left/right and 10 mm top/bottom at the user's request. Bamboo30 halves these defaults; explicitly saved values remain profile settings.
- Carry margins through native settings snapshots and saves; validate their ranges, show them in the native caption and preview, and safely clamp geometry for smaller digitizers.
- Add margin-persistence coverage. Local C# suite: 2,247 passed, 6 skipped, 0 failures; real Swift model: 22 checks passed. ARM64 build installed with the existing local signing identity.

## Bamboo28 — 29 October 2026

- Separate vertical comfortable-area cropping from orientation. Landscape 0°/180° no longer inherits vertical crop, scale, edge-margin or aspect-lock settings. At this stage landscape used the full pen surface; bamboo29/30 introduce the margins described above.
- Keep mapping preferences even when comfortable-area cropping is off, so saving no longer disables orientation selection or retains an obsolete cropped rectangle. A vertical uncropped preset uses its full oriented area.
- Track explicit mapping edits separately from the profile. Preserve manual geometry during unrelated saves; changing orientation, monitor selection or the explicit standard-area action recalculates it. Recover monitor selection from the old output rectangle when a manual profile has no saved monitor IDs.
- Enable monitor selection independently of cropping, hide unavailable landscape crop settings from search, and clear only hidden crop-number drafts when switching to landscape. Other invalid fields continue to block Save. Keep shared geometry and aspect locking consistent in the daemon, advanced editor and native preview.
- Re-enable the four body buttons in horizontal presets; the vertical preset disables them. Pen buttons remain available.
- Add real coordinate-transform checks for corners/center and a display with negative coordinates, profile/save round trips, and 22 checks compiling the production Swift settings model. Local C# suite: 2,246 passed, 6 skipped, 0 failures. User confirmed physical pen positioning after the fix; native right/left/vertical save and continued editing were checked locally.

## Bamboo27 — 18 October 2026

- Fix the USB/HID discovery race behind `Collection was modified; enumeration operation may not execute`: copy device-event inputs, synchronize endpoint updates and debounce state, and return detached discovery snapshots.
- Preserve the last published device baseline across notification bursts; invalidate pending debounce callbacks after a hub membership change. Correct the reversed previous/current lists in hub notifications.
- Include hotplug vendor filtering in the daemon async-event exception boundary.
- Treat lost RPC connections as an unavailable tablet refresh rather than an unhandled GUI exception; detach disconnected panels and stop their pending updates. Guard asynchronous GUI initialization, resynchronization and log forwarding against transport loss.
- Add 11 hardware-independent regression tests; five discovery tests fail against the prior implementation. Full local result: 2,233 passed, 6 existing configuration skips, 0 failures.
- See [reproduction, changes and verification limits](BAMBOO_HOTPLUG_FIX.md). A display-sleep trigger has not been established; physical sleep/wake and dock tests remain pending.

## Earlier previews

- Bamboo26 incorporates the bamboo25 review fixes: rejected saves propagate errors, daemon-owned saves use revision checks and bounded rollback, GUI/preset/CLI writes are coordinated, external file edits are detected, slow scrolling retains displacement, replaced contacts cancel taps, and sparse bindings are read without mutation. GUI, daemon and CLI are rebuilt together. Native Command-Q and the Quit commands share busy/unsaved-draft protection and managed process cleanup. The locally installed build was verified on 15 October 2026; public identity and distribution packaging are still in preparation.

- Silicon Tablet Driver is the public project name. README.md and README_RU.md describe the experimental CTH-661 scope, local bamboo26 validation, build requirements and remaining gaps. Development application and IPC names currently retain OpenTabletDriver compatibility.

- Bamboo25 adds synchronized editable numbers to all 12 native sliders, comma/point input, unit-aware percentages and visible validation for partial, malformed or out-of-range input. Save is blocked until input is complete; Revert restores the complete draft. Opening or reactivating settings no longer automatically selects a field. Standard native editing shortcuts remain available.

- Bamboo24 searches all 52 native settings in English and Russian, including actions and monitor names, and opens the matching control. General now offers an immediate, persistent English/Russian language picker that preserves pending tablet edits.

- Bamboo23 introduces native SwiftUI/AppKit settings in the existing ARM64 GUI process: seven sidebar sections, search, system appearance, explicit Save/Revert and local diagnostics export.
- The CTH-661 vector schematic follows the physical tablet, with a live working-area preview that follows scale, margin, display aspect and near-user placement.
- Standard macOS window controls are visible in a separate compact toolbar; closing settings keeps the driver alive. Early activation waits for profile synchronization instead of opening the legacy editor.
- Native saves stage the file, apply the daemon settings and atomically replace the file, with rollback on failure and preservation of custom bindings/manual geometry.

- Bamboo20 fixes generated enum controls in the Filters tab (TouchCommand NotSupportedException).

- Touch/gesture/button controls with recommended reset, fine movement gain and adjustable pen hover/contact stabilization.
- One-file diagnostics export survives daemon disconnection.
- Rotation-aware aspect constraints and manual update guard for local previews.
- ARM64 self-contained .NET 10 builds; no Rosetta dependency.
- CTH-661 two-contact and four-body-button reports, pen pressure/eraser diagnostics.
- Relative touch smoothing/acceleration, phased pixel scrolling and inertia, drag, pinch/page/Preview rotation shortcuts.
- Adaptive pointer smoothing and finer slow corrections; no trailing movement after a fast stop.
- LinearMouse synthetic-event bypass documented and verified on local hardware.
- Pointer constrained to actual screen bounds without storing off-screen displacement.
- Right-/left-handed and vertical presets, near-user proportional areas, named monitor selection.
- Optional Russian interface, white menu-bar tablet icon, no Dock presence.
- Explicit permission recheck, persistent local signing option and USB-connection LaunchAgent.
- Platform-explicit USB parser tests; initialization streams now disposed after use.

These changes include AI-assisted implementation and documentation. Hardware behavior and release limitations are documented in BAMBOO_MACOS.md and BAMBOO_RELEASE.md.
