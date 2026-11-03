using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FpsOverlay
{
    public partial class OverlayWindow : Window
    {
        private enum Corner { TopLeft, TopRight, BottomRight, BottomLeft }

        private const int HotkeyToggle = 1;
        private const int HotkeyCorner = 2;
        private const uint VK_F10 = 0x79;
        private const uint VK_F11 = 0x7A;
        private const double EdgeMargin = 12;

        private static readonly Brush Normal = Frozen("#F2F2F2");
        private static readonly Brush Good = Frozen("#7CDB8A");
        private static readonly Brush Warn = Frozen("#F2B544");
        private static readonly Brush Hot = Frozen("#F2665C");

        private readonly FrameRateMonitor _fps = new();
        private HardwareMonitor? _hardware;
        private string? _hardwareError;
        private HardwareStats? _latestHardware;

        private readonly DispatcherTimer _uiTimer;
        private readonly CancellationTokenSource _cts = new();
        private Corner _corner = Corner.TopLeft;
        private IntPtr _hwnd;

        public OverlayWindow()
        {
            InitializeComponent();

            _uiTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (_, _) => RefreshUi();

            SourceInitialized += OnSourceInitialized;
            SizeChanged += (_, _) => PlaceInCorner();
            Closed += OnClosed;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // Make the window click-through and keep it from ever taking focus from the game.
            long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED |
                  NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            const uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
            NativeMethods.RegisterHotKey(_hwnd, HotkeyToggle, mods, VK_F10);
            NativeMethods.RegisterHotKey(_hwnd, HotkeyCorner, mods, VK_F11);

            _fps.Start();
            _ = Task.Run(() => HardwareLoop(_cts.Token));
            _uiTimer.Start();
            PlaceInCorner();
        }

        private async Task HardwareLoop(CancellationToken ct)
        {
            try
            {
                _hardware = new HardwareMonitor();
            }
            catch (Exception ex)
            {
                _hardwareError = "Sensors unavailable: " + ex.Message;
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                try { _latestHardware = _hardware.Read(); }
                catch (Exception ex) { _hardwareError = "Sensor read failed: " + ex.Message; }

                try { await Task.Delay(1000, ct); }
                catch (TaskCanceledException) { break; }
            }
        }

        private void RefreshUi()
        {
            // Games occasionally push themselves above other topmost windows; reclaim the spot.
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

            var frames = _fps.GetStats(NativeMethods.GetForegroundProcessId());
            if (frames is { } f)
            {
                FpsText.Text = f.Fps.ToString("0", CultureInfo.InvariantCulture);
                FpsText.Foreground = f.Fps >= 55 ? Good : f.Fps >= 30 ? Warn : Hot;
                FrameTimeText.Text = f.FrameTimeMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
                LowText.Text = "1% " + f.OnePercentLowFps.ToString("0", CultureInfo.InvariantCulture);
            }
            else
            {
                FpsText.Text = "–";
                FpsText.Foreground = Normal;
                FrameTimeText.Text = "";
                LowText.Text = "";
            }

            var hw = _latestHardware;
            SetTemp(CpuTempText, hw?.CpuTempC);
            SetTemp(GpuTempText, hw?.GpuTempC);
            CpuLoadText.Text = FormatPct(hw?.CpuLoadPct);
            GpuLoadText.Text = FormatPct(hw?.GpuLoadPct);

            StatusText.Text = _fps.StartupError
                ?? _hardwareError
                ?? (frames is { } fr ? fr.ProcessName : "no game rendering");
        }

        private static void SetTemp(System.Windows.Controls.TextBlock block, float? celsius)
        {
            if (celsius is not { } c)
            {
                block.Text = "–";
                block.Foreground = Normal;
                return;
            }

            block.Text = c.ToString("0", CultureInfo.InvariantCulture) + "°C";
            block.Foreground = c >= 85 ? Hot : c >= 75 ? Warn : Normal;
        }

        private static string FormatPct(float? pct) =>
            pct is { } p ? p.ToString("0", CultureInfo.InvariantCulture) + "%" : "";

        public void ToggleVisibility()
        {
            if (IsVisible) Hide();
            else Show();
        }

        public void CycleCorner()
        {
            _corner = (Corner)(((int)_corner + 1) % 4);
            PlaceInCorner();
        }

        private void PlaceInCorner()
        {
            // Full primary-screen bounds (not the work area), since games cover the taskbar.
            double w = SystemParameters.PrimaryScreenWidth;
            double h = SystemParameters.PrimaryScreenHeight;
            double ww = ActualWidth, wh = ActualHeight;

            Left = _corner is Corner.TopLeft or Corner.BottomLeft ? EdgeMargin : w - ww - EdgeMargin;
            Top = _corner is Corner.TopLeft or Corner.TopRight ? EdgeMargin : h - wh - EdgeMargin;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                switch (wParam.ToInt32())
                {
                    case HotkeyToggle: ToggleVisibility(); handled = true; break;
                    case HotkeyCorner: CycleCorner(); handled = true; break;
                }
            }
            return IntPtr.Zero;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _uiTimer.Stop();
            _cts.Cancel();
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyToggle);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyCorner);
            _fps.Dispose();
            _hardware?.Dispose();
        }

        private static Brush Frozen(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            b.Freeze();
            return b;
        }
    }
}
