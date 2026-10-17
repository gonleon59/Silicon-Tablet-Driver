# Local validation, 2026-10-15

Candidate: 0.6.7-local-arm64.bamboo20, native ARM64. Local hardware: CTH-661, Apple Silicon, macOS 27.0.1, two displays.

- Full tests: 2186 passed, 6 existing upstream configuration skips, 0 failed. No platform test exclusion used.
- ARM64 package built successfully without compiler warnings; GUI, daemon, console and connection helper are ARM64 Mach-O executables. Stable local code signature verified, including nested helper. Local signing is not Apple notarization.
- Public macOS display metadata loads successfully in an integration test. An earlier ColorSync import mistake was found during actual launch and corrected.
- Live settings UI verified: monitor names and checkboxes, one/all monitor mapping, vertical rotation and return, proportions and near-user offset. Advanced editor clamping during rotation was found and corrected.
- USB helper's initial scan launched the GUI with a connected tablet. One GUI and one daemon observed. Permission log reports responsible PID equal to GUI PID, Input Monitoring granted and Accessibility true. Actual unplug/replug and next-login checks are still needed for release sign-off.
- Automatic early logs verified in Logs/Startup before permission checking. A console launch under a host app now receives a distinct launch-context explanation and a single manual normal-launch option. Do not ask users to revoke correct grants in that case.
- Physical recording confirms both pen side buttons, eraser identity and pen/eraser pressure up to 1023. Adobe behavior remains unverified.
- User confirmed the off-screen cursor displacement bug is fixed.
- User confirmed working dragging, stationary scroll suppression and smooth scrolling after LinearMouse bypass was enabled. User settings are preserved: scroll speed 1.7 and acceleration strength 0.9 (latest saved values). Bamboo18 shortens pointer filtering during fast movement, discards the stopping tail and reduces the small-motion dead zone. Regression tests cover stationary noise, fine movement, acceleration and flick stopping. The installed build starts with both permissions granted, one GUI, one daemon and one connection helper. User reports improved pointer stopping and fine control in bamboo18; this remains a preview.
- LinearMouse 0.11.4 was rewriting synthetic tablet scrolling using the last physical mouse rule. Its General setting to bypass events from other applications is now enabled; physical mouse rules remained byte-identical. This setting applies to events from all applications. No three/four-finger gestures or Force Touch claims. Pinch remains keyboard-step zoom; Preview rotation remains application-scoped document commands.

Preparing release material has not published a repository, tag, release or pull request. The public owner/name and bundle identity are still open release decisions. The complete fork diff needs human review before stable distribution.


Bamboo19 validation:
- Native package built without compiler warnings. Installed signature verifies; permission log reports Input Monitoring granted, Accessibility true and self-responsible GUI process.
- New Russian Touch and buttons tab visually inspected; pointer/scroll values and pen controls present. Pen settings saved via the UI and independently read from settings.json. Existing scroll speed 1.7 / acceleration 0.9 retained.
- Save diagnostics exercised through the native save dialog; resulting JSON parsed successfully with version19, Arm64, granted permissions, CTH-661 and four log tails.
- User confirmed USB reconnect and vertical orientation work; the earlier orientation report was an unapplied setting.
- Independent AI-agent review found rotated aspect bounds and upstream manual updater issues, both corrected. A second review found diagnostics dependence on daemon availability and dragging after remapped taps; corrected, with known button action restoration also improved. This is an independent agent review, not human release approval.
- Pen raw capture confirms hover coordinate variation. Separate hover/contact filters and tests added; physical feedback pending.
- Real fresh-account testing explicitly deferred by user. No temporary OS account created.


Bamboo20 regression fix:
- User reported NotSupportedException when selecting Bamboo touch navigation in the existing Filters tab: generated controls lacked enum support for TouchCommand.
- Added typed enum dropdowns in GeneratedControls, preserving numeric/string JSON conversion and enum defaults. DescriptionAttribute supplies translated action names.
- Repeated the exact native UI flow: select touch filter, open the action list, change LeftClick to RightClick, restore LeftClick, Save, switch to pen filter and back. All four action controls load; original action and scroll settings persist without an exception.
- Full suite: 2186 passed, 6 existing skips, 0 failures. Signed native package installed and verified.
- User had already confirmed qualitative improvement of bamboo19 pen/touch handling. This fix changes enum editing and labels, not gesture/pen processing.
