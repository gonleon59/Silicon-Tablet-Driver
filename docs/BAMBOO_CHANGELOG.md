# Bamboo preview changes

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
