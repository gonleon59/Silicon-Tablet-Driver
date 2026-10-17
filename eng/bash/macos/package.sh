#!/usr/bin/env bash

[ "${BUILD}" != "true" ] && exit_with_error "Must build to package MacOS"

pkg_script_root="$(readlink -f $(dirname "${BASH_SOURCE[0]}"))"

echo -e "\nPreparing package..."

PKG_FILE="${OTD_NAME}-${OTD_VERSION}_${NET_RUNTIME}.tar.gz"

pkg_root="${OUTPUT}/${OTD_NAME}.app"

move_to_nested "${OUTPUT}" "${pkg_root}/Contents/MacOS"

mkdir -p "${pkg_root}/Contents/Helpers"
helper_arch="${NET_RUNTIME#osx-}"
[ "$helper_arch" == "x64" ] && helper_arch="x86_64"
swiftc -O -target "${helper_arch}-apple-macos14.0" "${pkg_script_root}/../../macos/TabletConnectionMonitor.swift" -o "${pkg_root}/Contents/Helpers/TabletConnectionMonitor"

echo "Building native SwiftUI settings..."
swiftc -swift-version 5 -O -emit-library -target "${helper_arch}-apple-macos14.0" "${pkg_script_root}/../../macos/BambooSettings.swift" "${pkg_script_root}/../../macos/BambooSettingsSearch.swift" "${pkg_script_root}/../../macos/BambooNumericInput.swift" -o "${pkg_root}/Contents/MacOS/libBambooSettings.dylib"

echo "Copying MacOS assets..."
mkdir -p "${pkg_root}/Contents/Resources"
cp "${pkg_script_root}/Icon.icns" "${pkg_root}/Contents/Resources/"
cp "${pkg_script_root}/../../../LICENSE" "${pkg_root}/Contents/Resources/LICENSE"
cp "${pkg_script_root}/../../../COPYING.GPL3" "${pkg_root}/Contents/Resources/COPYING.GPL3"
cp "${pkg_script_root}/Info.plist" "${pkg_root}/Contents/"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString ${OTD_VERSION_BASE}" "${pkg_root}/Contents/Info.plist"
bundle_build="${OTD_BUILD_NUMBER:-${OTD_VERSION_BASE}}"
if ! [[ "$bundle_build" =~ ^[0-9]{1,4}(\.[0-9]{1,2}){0,2}$ ]]; then
  exit_with_error "OTD_BUILD_NUMBER must be a numeric Apple bundle version"
fi
/usr/libexec/PlistBuddy -c "Add :CFBundleVersion string ${bundle_build}" "${pkg_root}/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Add :OTDBuildVersion string ${OTD_VERSION}" "${pkg_root}/Contents/Info.plist"

if [ "${SIGNED}" == "true" ]; then
  echo "Signing app bundle..."
  if [ -n "${OTD_SIGNING_IDENTITY:-}" ] && hash codesign 2>/dev/null; then
    codesign --deep --force --sign "${OTD_SIGNING_IDENTITY}" "${pkg_root}"
  elif hash rcodesign 2>/dev/null; then
    rcodesign sign "${pkg_root}"
  elif hash codesign 2>/dev/null; then
    codesign --deep --force --sign - "${pkg_root}"
  else
    echo "Warning: neither rcodesign nor codesign found, skipping signing"
  fi
fi

echo "Creating tarball..."
create_binary_tarball "${pkg_root}" "${OUTPUT}/${PKG_FILE}"

echo -e "\nPackaging finished! Package created at '${OUTPUT}/${PKG_FILE}'"
