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
- **Multiple monitors:** the overlay sits on the primary monitor.
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
| `TrayIcon.cs` | Notification-area icon with show/hide, move and exit. |
