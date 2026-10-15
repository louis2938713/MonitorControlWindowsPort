<img src=".github/Icon-cropped.png" width="160" alt="App icon" align="left"/>

<div>
<h3>MonitorControl for Windows</h3>
<p>Control the brightness, contrast and volume of your external displays from the notification area or the keyboard,
with an on-screen display. A Windows port of <a href="https://github.com/MonitorControl/MonitorControl">MonitorControl</a> for macOS.</p>
</div>

<br clear="left"/>

## Features

- Brightness, contrast and volume of external displays through **DDC/CI** (DisplayPort, HDMI, USB-C, DVI, VGA).
- Brightness of the **built-in display** of laptops (native Windows brightness).
- **Software dimming** (gamma table, or a dimming overlay for virtual displays, HDR and unsupported cases) for displays and TVs without DDC.
- **Combined dimming**: software dimming takes over below the minimum hardware brightness, down to full black if you allow it.
- **Smooth** brightness transitions, **sync** of laptop brightness changes (adaptive brightness, Fn keys, Quick Settings) to external displays.
- Sliders in the notification area: one block per display, only the display under the mouse, or one combined slider for all.
- **Keyboard**: universal default shortcuts, volume/mute keys, brightness keys of external keyboards, Scroll Lock / Pause, custom shortcuts.
- Windows 11 style **OSD**.
- Dozens of advanced options per display (polling, DDC min/max, scale curve, inversion, VCP code remapping…).
- 20 languages, light and dark themes, automatic updates. Free and open source.

## Install

1. Download `MonitorControl-win-x64-Setup.exe` (or `win-arm64` for ARM PCs) from [Releases](../../releases).
2. Run it. No administrator rights are needed. The app installs for the current user and updates itself.
3. The MonitorControl icon (a sun) appears in the notification area — it may be in the hidden icons (`^`). Drag it to the taskbar to keep it visible.

A portable zip is also available. Releases are currently **not code signed**, so Windows SmartScreen may warn on first launch
(*More info → Run anyway*). SHA-256 checksums are published with every release (`SHA256SUMS.txt`).

Requirements: Windows 10 (21H2 or later) or Windows 11, x64 or ARM64.

## Usage

- **Left click** the icon for the sliders, **right click** for Settings, updates and Quit.
- Launching MonitorControl again opens its settings (useful if you chose to hide the icon).
- Default shortcuts (change them in *Settings > Keyboard*):

| Action | Shortcut |
|---|---|
| Brightness up / down | `Ctrl+Alt+↑` / `Ctrl+Alt+↓` |
| Contrast up / down | `Ctrl+Alt+Shift+↑` / `Ctrl+Alt+Shift+↓` |
| Volume up / down | `Ctrl+Alt+→` / `Ctrl+Alt+←` |
| Mute | `Ctrl+Alt+M` |
| Fine steps | add `Win` to any shortcut |

- Standard keys: volume/mute keys (when the sound goes to a display), brightness keys of external keyboards, Scroll Lock / Pause.
  With them: `Ctrl` = built-in display, `Ctrl+Win` = external displays, `Shift+Alt` = fine steps, `Ctrl+Alt+Win` = contrast,
  `Alt` opens the Windows settings, `Win` + brightness down toggles *Duplicate* / *Extend*.
- Hold **Shift** while launching for *safe mode* (default settings, no DDC reads).

## Supported displays

- Most LCD/LED monitors support DDC/CI. Make sure it is enabled in the monitor's own menu (often called *DDC/CI*).
- Built-in laptop panels use the native Windows brightness.
- TVs, displays behind some docks/adapters/KVMs, DisplayLink, Miracast and Remote Desktop use software dimming.

## Known limitations

- Laptop brightness keys (Fn) are handled by the computer firmware: they change the built-in display; use *sync* to follow them on external displays.
- Keys are not seen by MonitorControl while a window running as administrator has the focus (Windows security).
- The dimming overlay does not dim the mouse cursor.
- Windows may refuse very dark gamma ramps; MonitorControl then switches to the overlay automatically.

## Troubleshooting

Run `MonitorControl.exe --diagnose` (from the install folder, `%LocalAppData%\MonitorControl\current`) to get a read-only report of
your displays and their DDC/CI answers. Logs are in `%LocalAppData%\MonitorControl\logs`, settings in `%AppData%\MonitorControl\settings.json`.

## Build from source

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build MonitorControl.Windows.slnx
dotnet test --solution MonitorControl.Windows.slnx
dotnet run --project src/MonitorControl.App
```

- `src/MonitorControl.Core` — display logic (no Windows dependency, unit tested).
- `src/MonitorControl.App` — WPF app and Windows integration (DDC/CI via dxva2, WMI, gamma, keyboard hook, Core Audio).
- `tools/localization` — translations: `en.json` is the reference; run `dotnet run tools/localization/convert-strings.cs` after changing strings.
- Releases: push a `vX.Y.Z` tag; `.github/workflows/release.yml` builds and publishes the installers.

## Credits

MonitorControl is the work of [@waydabber](https://github.com/waydabber) (maintainer, developer of [BetterDisplay](https://github.com/waydabber/BetterDisplay#readme)),
[@the0neyouseek](https://github.com/the0neyouseek), [@JoniVR](https://github.com/JoniVR), [@alin23](https://github.com/alin23),
[@mathew-kurian](https://github.com/mathew-kurian/) (original developer) and [all contributors](https://github.com/MonitorControl/MonitorControl/graphs/contributors).
This Windows port follows the original app's behaviour and reuses its translations.

Licensed under the [MIT License](License.txt).
