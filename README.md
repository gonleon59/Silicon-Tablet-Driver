# Silicon Tablet Driver

**A second life for an older tablet.**

An experimental [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) fork for macOS on Apple Silicon, focused on the **Wacom Bamboo CTH-661**.

The goal is to keep a useful tablet working with a modern Mac: pen input for drawing and pointing, two-finger touch navigation, and native settings.

English · [Русский](README_RU.md)

## Status

**Experimental preview.** Development builds have been tested locally with a CTH-661 on an Apple Silicon Mac. Other tablets, Intel Macs, and other operating systems have not been validated for this fork.

The development baseline is **bamboo30**. Silicon Tablet Driver is the public project name; existing build scripts and applications still use OpenTabletDriver names. Public packaging and a separate application identity are being prepared.

This is an independent community project, not an official OpenTabletDriver release, Wacom driver, or Apple product.

## Latest changes — bamboo30 (29 October 2026)

This update includes all local bamboo28–bamboo30 changes since published bamboo27:

- Corrected pen positioning after switching between right- and left-handed landscape orientations. The vertical comfortable-area preset no longer applies to landscape modes.
- Orientation remains editable after Save. Uncropped vertical mode, selected monitors and margins survive saving; unrelated edits preserve manually configured geometry.
- Standard landscape pen margins now default to **4.5 mm left/right and 5 mm top/bottom**, giving CTH-661 a **207.48 × 127 mm** pen area. These are half the initial bamboo29 margins (9/10 mm).
- Touch is independent of pen margins and uses the complete **190 × 130 mm** hardware touch area. This is smaller than the **216.5 × 137 mm** pen area; software cannot extend touch into strips without a touch sensor.
- Added actual coordinate-transform, profile-persistence, orientation-editability and touch-independence checks. GitHub Actions now tests the real Swift settings model and packages bamboo30.

**Tested on macOS 27.2 with a Mac mini M4 and Wacom Bamboo CTH-661.** [Complete update notes, validation and limitations](docs/BAMBOO30_UPDATE.md) · [Version history](docs/BAMBOO_CHANGELOG.md).

## Features

- Native SwiftUI settings with English/Russian UI and bilingual search.
- Absolute pen mapping, configurable pen buttons, and adjustable hover/contact stabilization.
- Relative touch pointer control with speed, acceleration, fine movement, and smoothing.
- Two-finger scrolling with natural scrolling and optional inertia.
- Tap-to-click, two-finger secondary click, and tap-to-drag.
- Navigation actions, shortcut-based pinch zoom, and document rotation in Preview.
- Tablet orientation, working-area size and margins, and monitor selection.
- Menu-bar operation, optional USB-triggered launch, and an input-test window.
- Explicit Save/Revert, numeric-field validation, and settings-conflict detection.

Some gestures use application keyboard shortcuts and depend on the active application. This is not a complete Magic Trackpad implementation. There are no three-/four-finger gestures or Force Touch.

## Compatibility

| Item | Current scope |
| --- | --- |
| Computer | Mac with Apple Silicon; native ARM64 |
| macOS | Build target: macOS 14 or later |
| Primary tablet | Wacom Bamboo CTH-661, USB |
| Local hardware validation | CTH-661 on Apple Silicon, macOS 27.2 |
| Runtime | Packaged builds include .NET; Rosetta is not required |

The minimum build target does not mean every macOS version from 14 onward has been tested. Upstream's device support does not establish compatibility with this fork.

## Installation and signing

Public release packaging is in preparation. Follow the installation and signing notes attached to the exact release you download.

For current development builds:

1. Quit other tablet drivers, including upstream OpenTabletDriver.
2. Copy the packaged OpenTabletDriver.app to Applications.
3. Launch the installed copy and allow **Input Monitoring** and **Accessibility** when requested.
4. Open settings from the tablet icon in the menu bar.
5. Check the tablet connection and permissions in Diagnostics, select the mapping, and save.

Closing settings leaves the driver running; Quit exits it.

Current local builds are **not Apple-notarized**. Their local/ad-hoc signature does not establish Apple-verified developer identity. macOS may block an internet-downloaded build; consult [Apple's guidance](https://support.apple.com/en-us/102445). Building from source is another option.

Development builds share application identity, settings paths, RPC names, and the USB helper label with upstream. Do not run them together. Renaming the app alone does not isolate them. Public identity and migration will be documented before an independently installed release.

## Using the tablet

Keep the pen away while using touch: pen proximity suppresses touch. Choose the orientation and monitors before adjusting the working area. Set pointer speed, fine movement, scrolling, and smoothing to suit your hand.

Use the pen/touch test window to inspect input. Test pressure and eraser behavior in your drawing application before relying on them for artwork.

Language can be changed in General. Numeric fields accept a decimal point or comma.

Pinch uses zoom shortcuts, not continuous native magnification. Rotation is restricted to Preview. The vertical development preset disables the four tablet-body buttons; pen buttons remain available.

## Validation and known gaps

Local bamboo30 verification on **29 October 2026, macOS 27.2**:

- 2,251 C# tests passed, 6 existing configuration tests skipped, 0 failures.
- 22 Swift settings-model checks, 18 numeric-input checks and 12 search checks passed.
- ARM64 packaging and signature integrity were checked. The user confirmed pen positioning after the orientation fix; saving and continued editing were checked in the native window.
- Pressure changes in tablet reports and reaches a separate AppKit window as values from 0 to 1. **In Linearity Curve 7.1.1, the user did not obtain pressure-dependent width on a new Brush stroke. The cause is unresolved; Curve compatibility is not claimed fixed.**

Results apply to this build and environment. Physical landscape positioning was checked on bamboo28; bamboo30 changes the margins, with automated checks covering both landscape modes. Touch independence from pen margins was checked in software; exact physical sensor boundaries were not measured.

Adobe pressure/eraser support, clean-account installation, a full sleep/wake/reboot/login cycle, and repeated physical USB/dock reconnection remain unverified. One discard-and-quit dialog branch remains unconfirmed. A changed public identity or signing method needs fresh validation. Production readiness and hardware certification are not claimed.

## Build from source

Requirements: .NET 10 SDK, Xcode Command Line Tools with Swift, Bash 4+, and GNU coreutils. The SDK policy is defined in global.json.

From the repository root on an Apple Silicon Mac, with Bash and coreutils installed through Homebrew:

~~~sh
dotnet test OpenTabletDriver.Tests -c Release

export PATH="$(brew --prefix coreutils)/libexec/gnubin:$PATH"
OTD_BUILD_NUMBER=1 VERSION_SUFFIX=-silicon-preview.1 \
  "$(brew --prefix bash)/bin/bash" eng/bash/package.sh \
  --runtime osx-arm64 --signed true --output release-artifacts

codesign --verify --deep --strict release-artifacts/OpenTabletDriver.app
file release-artifacts/OpenTabletDriver.app/Contents/MacOS/OpenTabletDriver.UX.MacOS
~~~

Packaging still produces OpenTabletDriver.app. Without OTD_SIGNING_IDENTITY, it uses ad-hoc signing; this is not notarization. To use your own certificate, set OTD_SIGNING_IDENTITY locally. Never commit a signing private key.

## Reporting issues and privacy

Include the build version, macOS version, Mac chip, tablet model, connection method, expected behavior, and reproduction steps. State whether pen, touch, mapping, permissions, or startup is affected.

Diagnostics and startup logs can contain device metadata and local paths. Inspect them before sharing. Diagnostic exports are not uploaded automatically.

The inherited plugin manager and manual updater can access the network when used. Automatic upstream update checks are disabled on macOS in this fork. A blanket “no network access” claim would be inaccurate.

## Origins, license, and contributions

Based on OpenTabletDriver's 0.6.x branch, revision 0d151c417454bc0ba76fa8cc0804a87a9c7d089a.

Original authorship and copyright notices are retained. This fork uses **LGPL-3.0-or-later**; see [LICENSE](LICENSE) and [COPYING.GPL3](COPYING.GPL3). Binary releases must provide access to corresponding source, including modifications and build scripts. Third-party components retain their own license terms.

Development includes **AI-assisted code, review, and documentation**. Follow [AGENTS.md](AGENTS.md) and [CONTRIBUTING.md](CONTRIBUTING.md), including applicable AI-disclosure rules for pull requests.

Thanks to the OpenTabletDriver contributors whose work makes this project possible.

[Upstream](https://github.com/OpenTabletDriver/OpenTabletDriver) · [Upstream documentation](https://opentabletdriver.net/) · [Development guide](docs/BAMBOO_MACOS.md)
