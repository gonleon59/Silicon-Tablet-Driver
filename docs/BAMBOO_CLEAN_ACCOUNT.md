# Clean-account release check

Automated fresh-profile creation and settings round-trip tests pass independently of the user's saved settings. This is not a fresh macOS-account permission test.

Actual fresh-account GUI login is still required. Use a temporary Standard user via System Settings > Users & Groups, as documented by Apple:
https://support.apple.com/guide/mac-help/add-a-user-or-group-mchl3e281fc9/mac

Do not copy the existing user's OpenTabletDriver preferences or reset the existing user's TCC permissions. In the new session:

1. Open the installed application normally from Applications.
2. Grant Input Monitoring and Accessibility to that application; restart it normally if requested.
3. Verify one running interface and daemon, no repeated permission prompts, native ARM64 execution.
4. Check default touch navigation, pen, side/body buttons, monitor list and language switching.
5. Save diagnostics using the button on the Console tab; confirm permission status and detected CTH-661.
6. Install the connection helper for that user using the documented script, quit the driver, reconnect USB, then sign out/in and verify launch.
7. Return to the original account and verify its settings are unchanged. Remove only the temporary account after keeping the test result.

Status: pending interactive login; no new user account has been created by this task.
