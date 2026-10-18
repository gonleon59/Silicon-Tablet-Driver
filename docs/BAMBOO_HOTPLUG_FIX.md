# Bamboo27: USB/HID hotplug crash fix / Исправление сбоя USB/HID

18 October 2026 / 18 октября 2026.

## Reported failure

A locally installed bamboo26 daemon terminated with an unhandled `InvalidOperationException: Collection was modified; enumeration operation may not execute` while filtering added devices in `DriverDaemon`. The GUI then raised `StreamJsonRpc.ConnectionLostException` from `TabletSwitcherPanel.OnProfilesChanged`. An OS crash report confirms daemon termination; loss of the RPC connection was a consequence.

The device log contains repeated HID additions/removals. Display sleep was reported as a possible preceding event, but it has not been established as the cause. No user diagnostics, personal settings or full crash reports are included in this repository.

## Changes

1. `DevicesChangedEventArgs` eagerly copies both input sequences into read-only snapshots. Retained events no longer refer to live producer lists or repeatedly enumerate lazy discovery sources.
2. `RootHub` protects its endpoint list, hub membership, debounce version and last published baseline with one lock. Readers receive copies, and notifications are captured under that lock before invoking subscribers outside it. Hub membership changes invalidate pending debounce callbacks and use the correct previous/current direction.
3. The daemon's hotplug exception boundary includes addition checks and vendor filtering, preventing exceptions before its first await from escaping an async event handler.
4. Both initial and profile-triggered GUI tablet queries tolerate transport loss and ignore results from superseded connections. A connected daemon with no tablet still returns an empty list. Genuine server errors continue to propagate.
5. Disconnected tablet panels unsubscribe from updates; in-flight refreshes check whether the panel is still active before changing controls. GUI initialization, resynchronization and log forwarding also tolerate transport loss.

## Regression evidence

Tests run against fake device hubs and private, randomly named test pipes. They do not discover real hardware, start the installed daemon, or write user settings.

- Five tests run against the previous discovery/event code failed as expected: mutable event arguments, repeated lazy enumeration, a live GetDevices result, reversed hub events, and a device change during in-flight vendor enumeration. The latter reproduces the exact collection-modified exception.
- The updated suite adds 11 tests: seven device/event tests and four RPC-query tests. Coverage includes concurrent independent hubs, rapid change bursts, absence of a tablet, daemon loss during an active request, and propagation of genuine server errors.
- Full local C# suite: **2,233 passed, 6 existing configuration skips, 0 failures** (2,239 total).
- The macOS preview workflow builds the ARM64 package and checks its code signature. Consult the matching GitHub Actions run for the result for a specific commit.

The six existing skips concern digitizer-dimension configuration checks for XP-Pen Deco 02, XP-Pen Star G430, Parblo Ninos M, Parblo Ninos N7B, VEIKK VK430 and Huion H610 Pro V3. None is a skipped hotplug regression test.

## Verification limits

Hardware-independent reproduction establishes the concurrency defect and the tested fix. It does not validate physical CTH-661 unplug/replug, display sleep, system sleep/wake, dock power changes, clean-account permissions, or gesture/pen behavior. The GUI RPC helper is exercised over real private pipes, but native window presentation/reconnection is not driven by these tests.

This update preserves the existing OpenTabletDriver application/settings identity. Ad-hoc CI signatures are not Developer ID signatures or Apple notarization. Installing the new build is a separate operation; publishing these changes does not update an already installed bamboo26.

## Кратко по-русски

Исправлена подтверждённая гонка при изменении списка USB/HID-устройств: чтение изменяемого списка приводило к завершению фонового процесса, после чего окно теряло с ним связь. Списки теперь передаются снимками, их изменение синхронизировано, потеря RPC при обновлении окна обрабатывается. Добавлены 11 проверок; прежний код воспроизводит ошибки в пяти проверках, исправленный проходит весь набор. Связь конкретно с выключением дисплеев и физические сценарии сна/док-станции пока не подтверждены.

This change includes AI-assisted implementation, tests and documentation.
