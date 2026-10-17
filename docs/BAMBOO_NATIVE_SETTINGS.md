# Native Bamboo settings (bamboo23)

The default CTH-661 settings window uses SwiftUI `NavigationSplitView` and grouped `Form`, hosted in AppKit `NSWindow`. System SF Symbols, colors, controls and light/dark appearance are used. The library runs inside the existing GUI process and retains its bundle/signing identity; it is not another driver or permissions helper. The existing Eto editor remains an explicit Advanced Settings option.

## Code boundaries

- `eng/macos/BambooSettings.swift`: native UI, drafts, live vector tablet drawing, Save/Revert, unsaved-close guard and diagnostics save panel.
- `OpenTabletDriver.UX/Native/BambooSettingsSnapshot.cs`: typed profile conversion and validation. Unknown bindings and manual mapping survive unrelated edits.
- `OpenTabletDriver.UX/Native/MacSettingsWindow.cs`: rooted C callbacks, copied UTF-8 strings, GUI-thread dispatch, current profile conflict check and fresh display geometry.
- `OpenTabletDriver.Desktop/SettingsFileTransaction.cs` and `DriverDaemon.SaveSettings`: stage/flush, checked application, normalized file contents and atomic replacement under the daemon settings gate. Detect intervening file edits and restore the previous state on failure. `OpenTabletDriver.UX/Native/NativeSettingsTransaction.cs` remains a compatibility wrapper for tests; the GUI no longer writes the live settings file itself.
- `MainForm`, `App`, `TrayIcon`: open routing, deferred startup activation and background lifetime. Closing the advanced editor must not disconnect the daemon.
- `eng/bash/macos/package.sh`: compile and sign the Swift library with the self-contained ARM64 app.

The portrait schematic follows the user's tablet reference: four wide keys above the inset pen surface and a small loop at the lower edge. Landscape orientations rotate the complete body. The highlighted area updates immediately from the draft scale, margin, selected display bounding rectangle, proportions and near-user placement. Saving applies the corresponding shared C# mapping geometry. Unsupported manual geometry is preserved; its exact rectangle is edited in Advanced Settings.

## Validation recorded on the development Mac

- 2195 tests passed, 6 existing upstream configuration skips, 0 failures. Includes 6 typed-profile tests and 3 save-transaction tests.
- Warning-free ARM64 build, deep strict signature verification, same GUI/daemon identity and both system permissions granted.
- Actual UI: all seven sections, live 85% to 55% schematic update, orientation selector, Revert, Save, Command-S and reopening inspected. Saved area changed to 84% and restored to 85%; the entire profile compared equal to its original value. Only the top-level saved build revision changed.
- Actual native save panel exported a valid diagnostics JSON with version, permission status, tablet identification and bounded recent logs.
- An immediate activation during startup exposed the old editor in an intermediate build. Bamboo23 queues that request until profile synchronization completes; the native window was observed after immediate activation of the installed build.

The prior physical pen/touch behavior remains the user's accepted bamboo19 baseline. Native continuous pinch, Adobe pressure/eraser integration, a clean macOS account and the full sleep/login matrix remain unverified or incomplete as documented elsewhere. No GitHub publication has been performed.

## Search and language (bamboo24)

Search covers 52 native settings across all seven sections, not only sidebar titles. `eng/macos/BambooSettingsSearch.swift` contains the Foundation-only bilingual index, aliases, available action names and connected display names. It normalizes case/diacritics (including Russian ё/е), splits whitespace and requires every token to match. Results use the active interface language and link to stable IDs in the corresponding page. Blank and unmatched queries have explicit behavior; clearing search restores regular navigation.

General → Language offers English / Русский. The SwiftUI window switches immediately and persists the choice atomically in the existing language preference file, independently of profile saving. Pending tablet edits survive a language switch. Eto windows retain their construction-time language until the next launch; the native UI explains this distinction. No driver restart or new permissions are required to change the native settings language.

Validation on the Mac: 12 native search checks passed; 9 profile/transaction tests passed. The previous complete driver suite remains 2195 passed / 6 skips / 0 failures and was not repeated for this UI-only change. The final Swift build has no warnings. UI verified English search in Russian, Russian search in English, result navigation to scroll/pen controls, monitor-name lookup, no-results display, both language choices, persistence after relaunch, preservation of a pending pen edit, and restoration of original values.

Run search checks: `swiftc -swift-version 5 eng/macos/BambooSettingsSearch.swift eng/macos/tests/SettingsSearchTests.swift -o /tmp/bamboo-search-tests && /tmp/bamboo-search-tests`. The macOS CI workflow runs these checks before packaging.


## Editable numbers and focus (bamboo25)

All 12 native sliders now share `NumericSlider`, with a synchronized text field, displayed unit and validation. `BambooNumericInput.swift` is Foundation-only: comma and point decimals are accepted, percentages scale once, and partial/invalid/out-of-range input is retained visibly instead of becoming zero or a default. Invalid drafts disable Save; Return or leaving the field finishes a trailing decimal separator; Revert clears pending numeric edits. A displayed float representation echoed by SwiftUI does not mutate the profile merely by opening/focusing a field. Moving a slider explicitly replaces its text draft.

Opening/reopening settings clears the first responder immediately and after the presentation turn; fields start inactive and activate on click or keyboard navigation. Activation of an already visible window no longer requests a second open while its key-window transition is pending. The AppKit host supplies standard editing menu commands when the existing menu lacks them, so Command-A/C/V and normal text editing work.

Validation on the installed bamboo25: 18 Foundation numeric scenarios and 12 bilingual search checks passed. The self-contained ARM64 build and final Swift library compiled without warnings; the installed app passed deep strict signature verification. In the real UI, keyboard entry of 75,5 updated the area slider to 0.755; moving the slider to 0.8 displayed 80; invalid 999 and an empty field disabled Save and remained visible. A trailing separator finished on Tab; Return ended editing. Decimal point input 1.6 worked in Russian, saved as 1.6 in the profile and was restored to the original 1.5 through the UI. The full Profiles object compared equal to the backup after restoration. Reopening left focus on the window; click, Command-A and typing worked. Diagnostics showed both permissions available. The full C# test suite was not repeated for this UI-only revision; the latest full run remains bamboo22 (2195 pass / 6 upstream skips).

Run numeric checks with `swiftc -swift-version 5 eng/macos/BambooNumericInput.swift eng/macos/tests/NumericInputTests.swift -o /tmp/bamboo-numeric-tests && /tmp/bamboo-numeric-tests`. Both native Foundation checks run in the macOS CI workflow before packaging.

## Fixes following the independent bamboo25 review (source only, 2026-10-15)

- `SetSettings` now propagates rejection after one bounded rollback; initialization returning false also rejects the candidate. Partially initialized tools are disposed. A failed save cannot report success or overwrite the persistent file with a rejected candidate.
- `GetSettingsState`, `ApplySettings` and `SaveSettings` exchange detached snapshots with opaque, session-specific revision tokens. Native/advanced GUI commands and presets share one operation queue. The daemon serializes applies, saves and detection; stale conditional writes fail before application. GUI/daemon/CLI must be rebuilt and updated together.
- A file fingerprint before application and before rename detects external file edits; the external edit is preserved and runtime settings roll back. The staged file is refreshed after successful application to include normalized geometry/defaults. Arbitrary non-cooperating writers still cannot be made atomic with a compare-and-rename sequence alone.
- Slow parallel scrolling uses the actual elapsed time. Only an unchanged position held for 120 ms enters rest; this preserves the scroll anchor instead of discarding accumulated displacement. The bounded rest dead zone suppresses stationary noise; continuous small steps no longer repeatedly trigger a frame-count stop.
- Replacing a single contact cancels tap and drag history. Reading sparse binding settings uses non-mutating lookup, keeping the original profile and its token intact.

These are local source changes with synthetic, isolated filesystem and RPC regression tests. No installed application, user configuration, permission grant or physical tablet behavior is changed or certified by this section.
