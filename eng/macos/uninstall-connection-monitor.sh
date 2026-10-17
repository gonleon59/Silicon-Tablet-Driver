#!/bin/bash
set -euo pipefail
label="net.opentabletdriver.connection-monitor"
launchctl bootout "gui/$(id -u)/$label" 2>/dev/null || true
rm -f "$HOME/Library/LaunchAgents/$label.plist"
