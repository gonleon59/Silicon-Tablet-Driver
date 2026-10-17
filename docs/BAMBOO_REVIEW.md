# Independent AI-agent review, 2026-10-15

Two read-only passes were performed by a separate agent. This is not external human release approval.

Resolved findings:
- Rotated tablet aspect-ratio lock used unrotated dimension bounds. It now calls the existing rotation-aware largest-rectangle calculation.
- Manual macOS update checking opened upstream's updater, whose package choice is x64. Local previews now show a manual-update explanation.
- Diagnostic capture depended on a functioning daemon. Tablet lookup is now best effort, limited to three seconds; local logs and permission state remain exportable.
- Remapping a single tap still allowed it to arm left-button dragging. Dragging is now gated on the normal left-click tap assignment.
- Known physical button actions were not restored to their dropdown labels. Recognized bindings now show the saved action; unsupported custom bindings remain untouched.

Checked by inspection: pre-transform pen filtering order, pressure/button/eraser preservation, typed settings round-trip, local permission/startup path. No additional confirmed startup blocker found.

A proposed persistent primary-offset issue was retracted after verifying output pointer reconstruction on Save/Apply. It is not recorded as a fixed bug. Automatic pen remapping after monitor topology changes still requires applying updated settings.

Evidence: full suite 2186 passed / 6 pre-existing configuration skips / 0 failed; warning-free native package; installed signature verifies; new settings and one-file diagnostics exercised in the actual UI.

Physical release checks remain distinct from code review: new pen-filter feedback, Adobe integration and clean-account permission testing. The user confirmed USB reconnect and vertical orientation; the latter required pressing Save.

## Native settings follow-up

A separate read-only AI-agent pass inspected the native settings bridge. Findings addressed before installation: advanced-editor close could end background lifetime; legacy serialization could swallow write failures; partial daemon/file updates needed staged atomic persistence and rollback; unrelated native saves rejected manual rotation or advanced acceleration; profile conflict checks had to run after the RPC awaits. Nine dedicated tests cover typed conversion and transaction behavior. Full suite now: 2195 passed, 6 existing skips, 0 failed. Native window rendering and startup activation were subsequently exercised on the development Mac; see BAMBOO_NATIVE_SETTINGS.md. This remains AI review, not independent human release approval.
