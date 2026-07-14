# VolumeMixer

Lightweight Windows tray volume mixer. Single-file `.exe`, no installer
required, under 100 KB. Works on 64-bit Windows 10 1803+ and Windows 11.

Current release: **1.0.0**

## Features

- System-tray speaker icon that reflects current volume (5 states + mute)
- Per-application volume sliders (master + every app currently playing audio)
- Per-app brand colors and pill badges (Spotify, Discord, Zoom, Chrome, etc.)
- **Sound Settings page** with active output device picker (one-click switch
  via `IPolicyConfig`)
- Spatial Sound shortcut that opens the current/default output device's
  Windows sound properties
- **Per-app output device routing** — right-click any app row to slide open a
  device picker (uses Windows' internal `IAudioPolicyConfig` WinRT API)
- Mouse-wheel over the tray icon → master volume
- Mouse-wheel over a slider → per-app volume
- Live updates: external volume changes (media keys, other apps) reflected
  in the popup in real time
- Dark theme that picks up the Windows accent color (live, no restart)
- TR/EN UI based on system language
- Optional auto-start at login (toggle in tray menu)

## Quick start

```powershell
# 1. Build the exe (uses Windows' built-in .NET 4.x C# compiler)
.\build.ps1

# 2. Install to %LOCALAPPDATA%\VolumeMixer\ + enable auto-start + launch
.\install.ps1

# Variations:
.\install.ps1 -NoAutoStart    # install + launch, no startup entry
.\install.ps1 -Uninstall      # stop, remove auto-start, delete install dir

# Build a versioned release ZIP with SHA-256 checksums
.\release.ps1
```

You can also just double-click `VolumeMixer.exe` from anywhere — no install
needed for trying it out.

## Tray icon controls

| Action                       | Effect                                    |
|------------------------------|-------------------------------------------|
| Left click                   | Toggle mixer popup                        |
| Mouse wheel (over the icon)  | Master volume ±2 % per notch              |
| Right click                  | Context menu (Open / Sound Settings / Auto-start / Exit) |

## Mixer popup

| Action                                  | Effect                                          |
|-----------------------------------------|-------------------------------------------------|
| Drag a slider                           | Set that app's (or master's) volume             |
| Mouse wheel over a slider               | ±2 % adjust without dragging                    |
| Speaker button on a row                 | Toggle mute                                     |
| ⚙ in header                             | Switch to Sound Settings page                   |
| Right-click on an app row               | Slide open per-app output device picker         |
| Click outside the popup                 | Close                                           |

## Sound Settings page

- Lists every active render endpoint
- Click any device card to make it the system default (for console,
  multimedia, **and** communications roles — same as the Settings UI does)
- Spatial Sound card opens the Windows device properties page for the current
  output device
- Animated reorder when you switch devices (selected card slides to the top)
- ← in header to go back

## Per-app output device picker

- Right-click an app card → row expands and slides up to the top of the list
- Pick "System default" to clear the per-app override
- Pick any other device to route just that app there
- Right-click again to close (returns to original list position)

This uses the undocumented `Windows.Media.Internal.AudioPolicyConfig` WinRT
API. It's stable on Win10 1803+ and Win11 but isn't formally part of the
public SDK.

## Files

| File              | Purpose                                                |
|-------------------|--------------------------------------------------------|
| `VolumeMixer.cs`  | Single-file source (~4000 lines, all C#)               |
| `build.ps1`       | Compile to `VolumeMixer.exe` via Framework `csc.exe`   |
| `install.ps1`     | Install to LocalAppData + per-user auto-start          |
| `release.ps1`     | Build a versioned x64 ZIP with checksums                |
| `app.ico`         | Embedded application icon (Windows speaker glyph)      |
| `VolumeMixer.exe` | Build output                                           |

## Diagnostics

Crashes are logged to `%TEMP%\VolumeMixer.log`.

Release builds print a SHA-256 checksum after compilation. The executable is
currently unsigned, so Windows may show an unknown-publisher warning on first run.

## Limitations

- Spatial audio formats (Dolby Atmos / DTS:X / Windows Sonic) are opened in
  Windows' own device properties UI rather than switched directly in-app.
- `IAudioPolicyConfig` (per-app routing) is undocumented; if Microsoft
  changes the COM vtable in a future Windows build, per-app routing may
  silently no-op. Other features keep working.
