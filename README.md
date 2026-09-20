# Darkenator

A small Windows tray app that switches the system between light and dark mode — manually, or
automatically at sunset and sunrise for your location.

Built for 64-bit Windows 10/11. The installer and the app need no admin rights and install no
background service.

## Download and install

**[Download Darkenator for Windows](https://github.com/rmimpact/Darkenator_Windows/releases/latest/download/Darkenator-Setup.exe)**

Run `Darkenator-Setup.exe`, follow the short installer, and launch Darkenator from the Start menu.
The installer is currently unsigned, so Windows may show a Microsoft Defender SmartScreen prompt;
choose **More info**, confirm the filename, then choose **Run anyway**.

The first launch opens the settings window and drops an icon in the notification area. New tray
icons start out in the hidden-icons overflow: click the `^` chevron on the taskbar and drag
Darkenator out to keep it visible.

- **Left double-click** the tray icon to open settings.
- **Right-click** for Light / Dark / Automatic, Settings, and Exit.

Running the exe a second time brings the settings window forward instead of starting a second copy.

## Modes

| Mode | Behaviour |
| --- | --- |
| **Light** | Hold light, always. |
| **Dark** | Hold dark, always. |
| **Automatic** | Light from sunrise, dark from sunset, recalculated every day. |

Automatic needs a latitude and longitude. Press **Detect** to look them up from your IP address,
or type them in. After that the sun times are computed locally — the app never touches the network
again unless you press Detect a second time.

The **Go light** / **Go dark** offsets shift the switch relative to sunrise and sunset. Use
`-30` on *Go dark* to go dark half an hour before the sun actually sets, for example.

Automatic mode is **edge-triggered**: it switches when the schedule crosses a boundary, not
continuously. If you override the theme by hand at 11pm it stays overridden until sunrise. Tick
*Re-apply if something else changes the theme* if you would rather it always win.

## Why this one actually syncs

Writing the two `Personalize` registry values is only half of a theme switch, which is why
PowerShell one-liners leave Windows in a half-changed state. Darkenator does the whole sequence
the shell itself performs:

1. Write `AppsUseLightTheme` and `SystemUsesLightTheme` as **REG_DWORD** under
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`.
   (A `REG_SZ` `"0"` is silently ignored — a common reason a script appears to do nothing.)
2. Call `RefreshImmersiveColorPolicyState()` — `uxtheme.dll` ordinal 104 — to drop the shell's
   cached colour policy. Without this, apps keep reading the *old* mode no matter what the
   registry says.
3. Call `FlushMenuThemes()` — ordinal 136 — so Win32 context menus rebuild in the new colours.
4. Broadcast `WM_SETTINGCHANGE` with `lParam = "ImmersiveColorSet"` to every top-level window.
   This is the message modern apps actually listen for.
5. Broadcast `WM_THEMECHANGED` and `WM_SYSCOLORCHANGE` for older Win32 apps.

Verified live: with **Settings › Personalization › Colors** open, switching from Darkenator flips
the *Choose your mode* dropdown, repaints the Settings window, and updates the preview thumbnail
immediately.

### Deep sync (optional, off by default)

The steps above change the mode everywhere, but **Settings › Personalization › Themes** keeps
showing whichever theme was last *selected*, marked as modified. Ticking **Deep sync** also hands
`aero.theme` / `dark.theme` to the shell's own theme manager (the undocumented `IThemeManager2`
COM object the Settings app drives), so that page agrees too.

It is off by default because applying a theme file **resets your accent colour** to the one baked
into the stock theme. Wallpaper, cursors, sounds, desktop icons and the screensaver are explicitly
preserved. Leave it off unless the Themes page bothers you.

## Sunrise and sunset

Computed with the NOAA solar position equations — pure arithmetic, no API, works offline.
Verified against published times for Sydney, Melbourne and London to within three minutes
(most within one). Polar day and polar night are handled: above the Arctic and Antarctic circles
the app holds light or dark for the whole day rather than failing.

The schedule is re-evaluated every 30 seconds, and immediately on resume from sleep and on any
clock or time-zone change — so a machine that slept through sunset catches up as soon as it wakes.

## Run at startup

The checkbox writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so it appears under
**Task Manager › Startup apps** where you would expect. If you move the exe, the entry is
repointed automatically the next time you run it.

## Settings and logs

Both live in `%APPDATA%\Darkenator\`:

- `settings.json` — plain JSON, safe to edit by hand while the app is closed.
- `darkenator.log` — every switch, with a timestamp. **Open log** in the settings window opens it.

## Building

Needs the .NET 10 SDK.

```bash
powershell -ExecutionPolicy Bypass -File S:\Darkenator_Windows\build.ps1
```

This regenerates the icon, runs the checks, and publishes a self-contained single-file exe to
`dist\` (runs on any supported 64-bit Windows 10/11 machine with no .NET installation required).

For a smaller build that requires the .NET 10 Desktop Runtime instead:

```bash
powershell -ExecutionPolicy Bypass -File S:\Darkenator_Windows\build.ps1 -FrameworkDependent
```

### Checks

```bash
dotnet run --project S:\Darkenator_Windows\tests\Darkenator.Tests -c Release
```

Validates the solar calculations against published sun times, checks polar edge cases and a full
year of dates, and renders the tray icons to PNGs under the test output's `icons\` folder so they
can be inspected at real size.

## Publishing a release

Push a version tag such as `v1.0.1`. GitHub Actions builds and tests the app on Windows, creates the
installer, and publishes it to GitHub Releases. The download link above always points to the newest
release, so a website can use that exact URL without being changed for every version.

## Layout

| Path | What it is |
| --- | --- |
| `src/Darkenator/ThemeSwitcher.cs` | Registry write plus the notification sequence |
| `src/Darkenator/DeepThemeSync.cs` | Optional `IThemeManager2` theme-file apply |
| `src/Darkenator/SolarCalculator.cs` | NOAA sunrise/sunset |
| `src/Darkenator/ThemeScheduler.cs` | Decides the target theme and the next switch time |
| `src/Darkenator/TrayApp.cs` | Tray icon, menu, timers, system-event handling |
| `src/Darkenator/SettingsForm.cs` | The settings window |
| `src/Darkenator/TrayIcons.cs` | Sun/moon icons drawn at runtime |
| `tools/` | Icon generator and the scripts used to verify the UI |
