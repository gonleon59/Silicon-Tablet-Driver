# Public release preparation

This code is a fork, not a clean-room standalone driver. Preserve attribution, LGPL-3.0-or-later license and upstream history. Distribute corresponding source with each binary tag, including build scripts and modifications. Review the license obligations before distributing; refer to https://www.gnu.org/licenses/lgpl-3.0.html.

## Build and verify

Requirements: .NET SDK 10 (global.json), macOS Command Line Tools with Swift, Bash 4+, GNU coreutils/readlink. On macOS, Homebrew can provide bash/coreutils/dotnet.

```sh
dotnet test OpenTabletDriver.Tests -c Release
OTD_BUILD_NUMBER=1 VERSION_SUFFIX=-bamboo-preview.1 /opt/homebrew/bin/bash build.sh --runtime osx-arm64 --package=macos --signed true --self-contained true --output release-artifacts
codesign --verify --deep --strict release-artifacts/OpenTabletDriver.app
file release-artifacts/OpenTabletDriver.app/Contents/MacOS/OpenTabletDriver.UX.MacOS
```

Without OTD_SIGNING_IDENTITY, packaging signs ad hoc. Set OTD_SIGNING_IDENTITY only for a certificate in the builder's own keychain. CI never requires a local private key. A free ad-hoc release is not Apple-notarized; do not describe it as notarized. Choose public branding/bundle identity before a stable release. Current identity deliberately preserves existing local permissions, settings and RPC compatibility and cannot coexist with upstream.

The Bamboo workflow builds/tests and uploads workflow artifacts; it does not publish GitHub Releases or NuGet packages. Standard macOS ARM64 GitHub runners are available for public repositories: https://docs.github.com/en/actions/reference/runners/github-hosted-runners.

## Before publication

- Choose the owner/repository and product identity. Publish as a fork or clearly credit the upstream repository, base revision and authors. Keep LICENSE and GPL license text bundled.
- Review diff and AI disclosure; AGENTS.md requires an AI-disclosure marker in PR titles and a disclosure in PR bodies.
- Review NuGet dependencies and inherited cross-platform changes. Retain the upstream API compatibility where practical. This fork targets .NET 10; plugin binary compatibility must be checked individually.
- Validate LinearMouse compatibility using the guide without changing physical mouse rules.
- Validate on a clean Apple Silicon account: first install, permissions, reboot/login with tablet attached, unplug/replug, quit/reconnect, uninstall, language changes and multiple monitors.
- Physically validate each gesture and pen tool in advertised applications. Report native versus shortcut-based features honestly. No claimed Adobe certification before testing Adobe.
- Attach ARM64 archive, SHA256 checksum, matching source archive and change notes to the same tag. Keep generated binaries out of Git history.
- Exclude diagnostics, user configurations, signing credentials, certificates, original private context files, device serials and machine-specific paths.
- The inherited tag-release workflow is restricted to the upstream repository so a fork cannot accidentally publish their NuGet workflow. Review all other workflows before enabling them.
- Source publication was authorized and the public fork is available at https://github.com/gonleon59/Silicon-Tablet-Driver. Workflow artifacts are experimental builds; a separate GitHub Release and independent application identity remain in preparation.

## Review focus

Gesture recognition is a deterministic state machine independent of macOS output. Tests cover cancellation/release, contact transitions, jitter, acceleration, scroll momentum, pinch/page/rotation exclusion and rotated area geometry. Native events own/release CoreFoundation objects. The output timer runs only while scroll data or momentum are pending; USB helper waits on IOKit notifications. Display metadata are cached for one second. Avoid private touchpad APIs and unrestricted application rotation shortcuts.

OTD_BUILD_NUMBER sets a numeric CFBundleVersion independently of the user-visible version suffix; increment it for each distributed build to avoid version ambiguity. It accepts Apple's three-component numeric format (4/2/2 digits).

Native settings require macOS 14 or later, SwiftUI and AppKit; the packaged Swift dynamic library is compiled for the target architecture and signed inside the GUI bundle. See BAMBOO_NATIVE_SETTINGS.md for the interop and save transaction review.
