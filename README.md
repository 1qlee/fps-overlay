# FPS Overlay

A small, click-through, always-on-top Windows overlay: a single dark pill that shows

```
CPU 69°C 70W   GPU 62°C 305W   FPS 70
```

- **CPU** (red label): temperature and package power draw
- **GPU** (green label): temperature and board power draw
- **FPS** (blue label): frame rate of the game you're playing

The overlay doesn't inject anything into the game. It reads the same Windows
event traces (ETW) that PresentMon and CapFrameX use, so it's safe to run with
anti-cheat games.

## Requirements

- Windows 10 or 11, x64
- Administrator rights. ETW sessions and CPU temperature sensors both need them,
  and the app asks for elevation when it starts.
- For CPU temperatures: the [PawnIO](https://pawnio.eu/) driver, which current
  LibreHardwareMonitor releases use for low-level sensor access. If CPU shows `–`,
  install PawnIO and restart the overlay. GPU temperatures don't need it.

## Getting it

**Prebuilt:** every push runs the *Build* GitHub Actions workflow.

1. Open the latest run under **Actions → Build**. While signed in to GitHub,
   download the `FpsOverlay-win-x64` artifact and unzip it.
2. In the unzipped folder, double-click **`install.cmd`**. The window stays
   open at the end so you can read the result. This copies the app to `%LOCALAPPDATA%\Programs\FpsOverlay`, adds a Start
   Menu shortcut and launches it. The build is self-contained, so no .NET install
   is needed.

To also start it at sign-in without a UAC prompt each time, run this in a
Command Prompt or PowerShell window in that folder:

```
.\install.cmd -StartWithWindows
```

`.\install.cmd -Uninstall` removes the app, its shortcut and the startup task.

**Build it yourself** (needs the [.NET 8 SDK](https://dotnet.microsoft.com/download)):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
.\publish\FpsOverlay.exe
```

## Using it

| Action | How |
| --- | --- |
| Show / hide overlay | `Ctrl+Shift+F10`, or double-click the tray icon |
| Move or resize | `Ctrl+Shift+F7` (or tray → *Edit position & size*): drag it to move, drag a corner to resize, then `Ctrl+Shift+F7` again to lock |
| Snap to next screen corner | `Ctrl+Shift+F11` |
| Make the overlay fainter / stronger | `Ctrl+Shift+F8` / `Ctrl+Shift+F9` (10% steps, down to 20%) |
| Set overlay or background opacity | Right-click the tray icon → *Overlay opacity* / *Background opacity* |
| Exit | Right-click the tray icon → Exit |

In edit mode the overlay gets a blue outline and corner grips, and it accepts
the mouse:

- **Drag the overlay** to move it anywhere, including onto another monitor.
- **Drag a corner grip** to resize it. Text and spacing scale together
  (50%–300%), and the opposite corner stays where it is.

Locking it saves the position and size. If its monitor is later unplugged,
the overlay is pulled back onto a screen that still exists.

*Overlay opacity* fades everything, text included. *Background opacity* fades
only the dark pill, so the numbers stay fully readable. Opacity, position and size
are saved to `%APPDATA%\FpsOverlay\settings.json` and restored the next time
it starts.

Outside edit mode the overlay ignores the mouse completely. Clicks, hovering and scrolling all
go straight through to the game underneath, and it never takes focus. Use the
tray icon or the hotkeys to control it.

The overlay tracks the app in the **foreground window**. If that app isn't
rendering, it falls back to whichever process is presenting the most frames.

Temperatures turn amber at 75 °C and red at 85 °C. A reading the hardware
doesn't report shows `–`. If something is wrong, for example the app isn't
elevated or the sensors can't load, a short message appears under the pill.

## Limitations

- **Graphics APIs:** FPS works for Direct3D 9, 10, 11 and 12, which covers most
  Windows games. Pure Vulkan and OpenGL games that don't present through DXGI show
  `–`. Many Vulkan games on NVIDIA/AMD do go through DXGI and will work.
- **Exclusive fullscreen:** no normal window can draw over a true exclusive-fullscreen
  game. Use *borderless* / *windowed fullscreen*. Most DX12 games, and DX11
  games with Windows' fullscreen optimisations, already behave this way.
- **Multiple monitors:** the corner presets use the primary monitor. Use edit
  mode to put it on another one.
- **Sensor names:** these vary between CPU and GPU models. For temperature the
  app looks for "CPU Package" / "Core (Tctl/Tdie)" and "GPU Core". For power it
  looks for "CPU Package" / "Package" and "GPU Package" / "GPU Board Power" /
  "GPU Power". If none of those exist it uses the highest reading of that type.
  Some older GPUs don't report power at all.

## How it works

| File | Role |
| --- | --- |
| `FrameRateMonitor.cs` | Starts a real-time ETW session on the `Microsoft-Windows-DXGI` and `Microsoft-Windows-D3D9` providers, records the timestamp of each `Present` event per process, and computes FPS over the last second. |
| `HardwareMonitor.cs` | Polls CPU/GPU temperature and power once a second through [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). |
| `OverlayWindow.xaml(.cs)` | Transparent WPF window with `WS_EX_TRANSPARENT \| WS_EX_NOACTIVATE`, so clicks pass through and it never takes focus from the game. Refreshes twice a second and handles the global hotkeys. |
| `TrayIcon.cs` | Notification-area icon with show/hide, edit mode, corner, opacity and exit. |
| `Settings.cs` | Loads and saves preferences as JSON. |
| `app.ico`, `tools/make_icon.py` | App/tray icon and the script that draws it. |
