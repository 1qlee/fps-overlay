# FPS Overlay

A small, click-through, always-on-top Windows overlay that shows:

- **FPS** of the game you're playing, plus frame time and **1% low**
- **CPU** temperature and load
- **GPU** temperature and load

```
FPS   144   6.9 ms   1% 98
CPU   62°C   23%
GPU   71°C   97%
eldenring
```

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

**Prebuilt:** every push runs the
*Build* GitHub Actions workflow. Download the `FpsOverlay-win-x64`
artifact from that run, unzip it, and run `FpsOverlay.exe`. The build is
self-contained, so no .NET install is needed.

**Build it yourself** (needs the [.NET 8 SDK](https://dotnet.microsoft.com/download)):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
.\publish\FpsOverlay.exe
```

## Using it

| Action | How |
| --- | --- |
| Show / hide overlay | `Ctrl+Shift+F10`, or double-click the tray icon |
| Move to next screen corner | `Ctrl+Shift+F11` |
| Exit | Right-click the tray icon → Exit |

The overlay tracks the app in the **foreground window**. If that app isn't
rendering, it falls back to whichever process is presenting the most frames.
The bottom line shows the name of the process being measured.

Colours: FPS is green at 55 or more, amber at 30–54 and red below 30.
Temperatures turn amber at 75 °C and red at 85 °C.

## Limitations

- **Graphics APIs:** FPS works for Direct3D 9, 10, 11 and 12, which covers most
  Windows games. Pure Vulkan and OpenGL games that don't present through DXGI show
  `–`. Many Vulkan games on NVIDIA/AMD do go through DXGI and will work.
- **Exclusive fullscreen:** no normal window can draw over a true exclusive-fullscreen
  game. Use *borderless* / *windowed fullscreen*. Most DX12 games, and DX11
  games with Windows' fullscreen optimisations, already behave this way.
- **Multiple monitors:** the overlay sits on the primary monitor.
- **Sensor names:** these vary between CPU and GPU models. The app looks for
  "CPU Package" / "Core (Tctl/Tdie)" and "GPU Core". If those are missing it uses
  the highest temperature it can find.

## How it works

| File | Role |
| --- | --- |
| `FrameRateMonitor.cs` | Starts a real-time ETW session on the `Microsoft-Windows-DXGI` and `Microsoft-Windows-D3D9` providers, records the timestamp of each `Present` event per process, and computes FPS (last 1 s) and 1% low (last 10 s). |
| `HardwareMonitor.cs` | Polls CPU/GPU sensors once a second through [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). |
| `OverlayWindow.xaml(.cs)` | Transparent WPF window with `WS_EX_TRANSPARENT \| WS_EX_NOACTIVATE`, so clicks pass through and it never takes focus from the game. Refreshes twice a second and handles the global hotkeys. |
| `TrayIcon.cs` | Notification-area icon with show/hide, move and exit. |
