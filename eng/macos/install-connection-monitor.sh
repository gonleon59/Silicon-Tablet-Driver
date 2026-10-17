#!/bin/bash
set -euo pipefail
app_path="${1:-/Applications/OpenTabletDriver.app}"
helper_path="$app_path/Contents/Helpers/TabletConnectionMonitor"
label="net.opentabletdriver.connection-monitor"
plist_path="$HOME/Library/LaunchAgents/$label.plist"
if [[ ! -x "$helper_path" ]]; then
  echo "Connection monitor is missing: $helper_path" >&2
  exit 1
fi
mkdir -p "$(dirname "$plist_path")"
/usr/bin/python3 - "$plist_path" "$helper_path" "$label" <<'PY'
import plistlib, sys
path, executable, label = sys.argv[1:]
with open(path, 'wb') as f:
    plistlib.dump({'Label': label, 'ProgramArguments': [executable], 'RunAtLoad': True,
                  'KeepAlive': True, 'ThrottleInterval': 10,
                  'AssociatedBundleIdentifiers': ['net.opentabletdriver']}, f)
PY
launchctl bootout "gui/$(id -u)/$label" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$plist_path"
