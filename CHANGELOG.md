# Changelog

## Unreleased

- Removed the process-wide low-level mouse hook used for tray-wheel volume.
  Windows Defender classified the unsigned hook + auto-start combination as
  `Trojan:Win32/Bearfoos.A!ml` and terminated/quarantined the application.
- Fixed the settings/back buttons closing the popup when their focused controls
  were disposed during page navigation.
- Fixed the x64 native property buffer size, preventing memory corruption when
  reading audio device properties.
- Fixed popup focus loss, tray-click dismissal races, settings-to-mixer reopening,
  and stale background preload results. Startup no longer activates a hidden window.
- Restored missing master controls after audio-device loss/recovery and refresh
  open mixer sessions/output state without depending on creation notifications.
- Fixed sliders continuing to drag after losing capture and live polling moving
  the thumb during a drag. Added Windows regression tests (`.\test.ps1`).
- Added a self-contained `VolumeMixerSetup.exe` installer.
- Installer deploys to the current user's LocalAppData directory, enables
  per-user auto-start, launches the app, and registers an uninstaller.
- Synced auto-start with Windows Startup Apps / Task Manager so Windows-side
  enable and disable choices are reflected by the tray menu.

## 1.0.0 - 2026-07-14

- Added a tray-based master and per-application volume mixer.
- Added output-device switching and per-app device routing.
- Added Windows theme/accent integration and spatial sound settings shortcut.
- Added smooth popup animations, outside-click dismissal, and fullscreen protection.
- Added reliable per-user auto-start registration.
- Fixed tray click, hidden-icons chevron, and wheel-volume edge cases.
- Reduced popup rendering work and released native audio/UI resources deterministically.
- Scoped the system mouse hook to active tray interactions and bounded the icon cache.
- Reduced idle polling and skipped redundant session-list renders.
