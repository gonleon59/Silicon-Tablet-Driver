# Bamboo on macOS

## Installation

Target: Apple Silicon, macOS 14 or newer; actual physical testing currently covers CTH-661 on macOS 27. Builds contain their .NET runtime and do not require Rosetta. Earlier macOS versions and other tablets are not physically verified.

Quit other tablet drivers before installation. Copy OpenTabletDriver.app into Applications. Launch it and grant Input Monitoring and Accessibility to this copy. Permissions attach to code identity; replacing an ad-hoc-signed release may require granting access again. Local development can use a persistent local signing certificate. The release must never include that certificate's private key.

The driver uses a tablet icon in the menu bar. Open native settings from that icon and choose the language in General; the English/Russian selection takes effect immediately and preserves pending tablet edits. Close the settings window to leave the driver running; Quit in the menu exits it.

Optional USB-triggered launch:

```sh
bash eng/macos/install-connection-monitor.sh /Applications/OpenTabletDriver.app
```

The user LaunchAgent listens to IOKit device arrivals and starts the application when a supported Bamboo USB ID appears. No periodic USB scan is used by this helper. A tablet present at login is included. After unplugging, the driver remains in the menu bar, ready for reconnection. Quitting while the tablet remains attached does not immediately reopen it; reconnect to trigger launch.

Remove the trigger with `bash eng/macos/uninstall-connection-monitor.sh`. This does not remove settings or the application.

## Comfortable mapping

Choose right-handed (buttons left, 0°), left-handed (buttons right, 180°), or vertical (buttons top, 270°). Vertical mode disables the four body buttons; pen buttons remain active. Working-area presets preserve screen proportions, leave an adjustable edge margin and can shift the area toward the user. Check one or several monitors, or All monitors. Several selected screens are mapped to their bounding rectangle; spaces between screens remain geometric gaps. A missing saved display falls back to connected screens. Press Refresh monitors after a display arrangement change, then Save / Apply.

Use Edit area manually before changing the detailed area editors below; this disables preset recalculation. The monitor and comfortable controls re-enable presets when used again. Pen mapping is absolute; touch navigation is relative.

## Controls

- Pen tip: primary click and pressure. Eraser: eraser tool and pressure where the application supports tablet events.
- First/lower pen button: right click. Second/upper button: double click. Existing custom bindings are preserved until changed explicitly.
- Four body buttons: back, forward, Mission Control, desktop in the local Bamboo preset; assignments remain editable.
- One finger: relative cursor with adjustable independent acceleration and stationary-noise suppression.
- One-finger tap: click. Two-finger tap: right click.
- Double tap, hold the second contact and move: drag; lift or bring the pen near to release.
- Two fingers together: pixel scrolling with begin/change/end phases and optional momentum. Natural scrolling and speed are editable. Default speed is 0.25 with bounded acceleration; timer resampling spreads hardware steps over smaller events.
- Quick horizontal two-finger swipe: back/forward using Cmd-[ / Cmd-]; supported behavior depends on the active application.
- Pinch: rate-limited Cmd-+ / Cmd-− steps. This changes application/page zoom; it is not native continuous magnification. Different apps, including ChatGPT, can use different zoom increments.
- Rotate two fingers: 90° document rotation in Preview only. Disabled in other applications to avoid unrelated shortcuts.

All gestures use two contacts. No three/four-finger gestures, Force Touch, or system-native touchpad identity are provided. Keep the pen away while using fingers; pen proximity suppresses touch briefly.

Open Pen and touch test to see pressure, eraser/pen identity, both pen buttons, four body buttons and contact count. Test pressure in the intended drawing application before relying on it for artwork.

## Privacy and troubleshooting

No diagnostic tablet stream is recorded during normal use. Recording through developer/debug tools is explicit and can include device metadata; do not post raw recordings publicly without inspection. The inherited plugin manager and manual updater can access the network when used. Automatic upstream-update checks are disabled for this macOS fork. USB helper and touch processing themselves need no network access.

If permissions appear enabled but the app cannot receive/control input, check that the Privacy entry refers to the installed, signed copy. The app rechecks on an explicit button press; it does not repeatedly relaunch itself.

The fork retains upstream settings paths, RPC names and bundle identifier for local compatibility. Do not run it simultaneously with upstream OpenTabletDriver. A separately branded public bundle identity and migration need to be settled before a stable independently installed release.

Startup logs are flushed to `~/Library/Application Support/OpenTabletDriver/Logs/Startup` before permission checks. Up to ten logs are retained, with a five MiB cap per process. They include startup identity, permission results and exception stacks, not a raw pen/touch stream. Open them from Journal → Open log folder. macOS native crashes can additionally produce reports in `~/Library/Logs/DiagnosticReports`.

A console launch may inherit the terminal/host app's privacy identity. The driver identifies this case without asking you to remove its valid grants, and offers a single manual launch through macOS. It never repeatedly relaunches itself.

## LinearMouse compatibility

In LinearMouse 0.11.4, enable General → “If enabled, LinearMouse will not modify events sent by other applications”. Its preference is `bypassEventsFromOtherApplications` in `com.lujjjh.LinearMouse`. This leaves per-device physical mouse rules in place, while allowing the driver's application-generated pointer/scroll events through. The option applies to events from other applications generally, not only this driver.

When this option is off, LinearMouse can attribute a generated event without HID device metadata to the last active physical mouse. A fixed-line scrolling rule then converts even a tiny continuous pixel event into a fixed number of lines and discards momentum. This makes driver speed changes appear ineffective. Check this setting before increasing smoothing or reducing sensitivity further. The driver does not change third-party preferences automatically.

Source references for the checked version: [event resolution](https://github.com/linearmouse/linearmouse/blob/v0.11.4/LinearMouse/EventTransformer/EventTransformerManager.swift), [device fallback](https://github.com/linearmouse/linearmouse/blob/v0.11.4/LinearMouse/Device/DeviceManager.swift), [fixed-line conversion](https://github.com/linearmouse/linearmouse/blob/v0.11.4/LinearMouse/EventTransformer/LinearScrollingVerticalTransformer.swift).


## Touch and button controls (bamboo19)

The Touch and buttons tab provides pointer speed, fine movement speed, acceleration and pointer smoothing, plus scroll speed/acceleration, per-gesture switches and command choices. Press Save or Apply after changing values. Restore recommended touch settings resets touch navigation only; pen stabilization, physical button bindings and monitor mapping are preserved.

Pen hover smoothing defaults to 35 ms and contact smoothing to 12 ms. Zero disables either filter. More smoothing trades response time for stability; contact filtering preserves pressure and eraser/button identity. The fine movement gain defaults to 0.65 and blends into normal speed as movement gets faster. Existing scroll values are retained on upgrade.

Tap-to-drag is enabled only when one-finger tap is assigned to left click. Gesture choices include click/right click/double click, Back/Forward, Mission Control/Desktop and tab switching; each app may interpret shortcuts differently. Vertical mode still disables the four body buttons.

The Console tab has Save diagnostics: one JSON file containing app version, OS/architecture, current GUI permission status, detected tablet names and four bounded startup-log tails. No environment variable dump or raw tablet recording is included. A disconnected/unresponsive daemon is recorded as an error while local diagnostics can still be saved.

Continuous pinch research and limitations: [BAMBOO_PINCH_RESEARCH.md](BAMBOO_PINCH_RESEARCH.md). Fresh-account validation: [BAMBOO_CLEAN_ACCOUNT.md](BAMBOO_CLEAN_ACCOUNT.md).
