using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
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
        private const int HotkeyFainter = 3;
        private const int HotkeyStronger = 4;
        private const int HotkeyMove = 5;
        private const uint VK_F7 = 0x76;
        private const uint VK_F8 = 0x77;
        private const uint VK_F9 = 0x78;
        private const uint VK_F10 = 0x79;
        private const uint VK_F11 = 0x7A;
        private const double EdgeMargin = 12;

        private static readonly Brush Normal = Frozen("#F2F2F2");
        private static readonly Brush Warn = Frozen("#F2B544");
        private static readonly Brush Hot = Frozen("#F2665C");

        private readonly FrameRateMonitor _fps = new();
        private HardwareMonitor? _hardware;
        private string? _hardwareError;
        private HardwareStats? _latestHardware;

        private readonly DispatcherTimer _uiTimer;
        private readonly CancellationTokenSource _cts = new();
        private readonly Settings _settings = Settings.Load();
        private Corner _corner;
        private IntPtr _hwnd;
        private bool _moveMode;

        public bool IsInMoveMode => _moveMode;

        public double OverlayOpacity => _settings.Opacity;
        public double BackgroundOpacity => _settings.BackgroundOpacity;

        public OverlayWindow()
        {
            InitializeComponent();

            _corner = (Corner)_settings.Corner;
            ApplyOpacity();

            _uiTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (_, _) => RefreshUi();

            SourceInitialized += OnSourceInitialized;
            SizeChanged += (_, _) => PlaceWindow();
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
            NativeMethods.RegisterHotKey(_hwnd, HotkeyFainter, mods, VK_F8);
            NativeMethods.RegisterHotKey(_hwnd, HotkeyStronger, mods, VK_F9);
            NativeMethods.RegisterHotKey(_hwnd, HotkeyMove, mods, VK_F7);

            _fps.Start();
            Log.Write(_fps.StartupError is null ? "FPS tracing started." : "FPS tracing NOT started: " + _fps.StartupError);
            _ = Task.Run(() => HardwareLoop(_cts.Token));
            _uiTimer.Start();
            PlaceWindow();
        }

        private async Task HardwareLoop(CancellationToken ct)
        {
            try
            {
                _hardware = new HardwareMonitor();
                Log.Write("Hardware sensors opened.");
            }
            catch (Exception ex)
            {
                _hardwareError = "Sensors unavailable: " + ex.Message;
                Log.Write("Hardware sensors failed: " + ex);
                return;
            }

            bool loggedFirstRead = false;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _latestHardware = _hardware.Read();
                    if (!loggedFirstRead)
                    {
                        loggedFirstRead = true;
                        Log.Write("First sensor read: " + _latestHardware);
                    }
                }
                catch (Exception ex)
                {
                    if (_hardwareError is null) Log.Write("Sensor read failed: " + ex);
                    _hardwareError = "Sensor read failed: " + ex.Message;
                }

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
            FpsText.Text = frames is { } f ? f.Fps.ToString("0", CultureInfo.InvariantCulture) : "–";

            var hw = _latestHardware;
            SetTemp(CpuTempValue, CpuTempUnit, hw?.CpuTempC);
            SetTemp(GpuTempValue, GpuTempUnit, hw?.GpuTempC);
            CpuPowerValue.Text = FormatNumber(hw?.CpuPowerW);
            GpuPowerValue.Text = FormatNumber(hw?.GpuPowerW);

            string? message = _moveMode
                ? "Drag to move  ·  Ctrl+Shift+F7 to lock"
                : _fps.StartupError ?? _hardwareError;
            StatusText.Text = message ?? "";
            StatusText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Temperatures stay white until they get hot, then turn amber and red.</summary>
        private static void SetTemp(Run value, Run unit, float? celsius)
        {
            value.Text = FormatNumber(celsius);
            Brush color = celsius is { } c ? (c >= 85 ? Hot : c >= 75 ? Warn : Normal) : Normal;
            value.Foreground = color;
            unit.Foreground = color;
        }

        private static string FormatNumber(float? n) =>
            n is { } v ? v.ToString("0", CultureInfo.InvariantCulture) : "–";

        public void ToggleVisibility()
        {
            if (_moveMode) ToggleMoveMode(); // lock before hiding
            if (IsVisible) Hide();
            else Show();
        }

        public void CycleCorner()
        {
            if (_moveMode) ToggleMoveMode(); // lock first so the new spot isn't overridden

            // Leaving a custom position snaps back to the current corner first.
            if (_settings.X is null)
                _corner = (Corner)(((int)_corner + 1) % 4);
            _settings.X = _settings.Y = null;
            _settings.Corner = (int)_corner;
            _settings.Save();
            PlaceWindow();
        }

        /// <summary>Sets the opacity of the whole overlay, text included.</summary>
        public void SetOverlayOpacity(double value)
        {
            _settings.Opacity = Math.Clamp(Math.Round(value, 2), Settings.MinOpacity, 1);
            ApplyOpacity();
            _settings.Save();
        }

        /// <summary>Sets the opacity of the dark pill only; the text stays as it is.</summary>
        public void SetBackgroundOpacity(double value)
        {
            _settings.BackgroundOpacity = Math.Clamp(Math.Round(value, 2), 0, 1);
            ApplyOpacity();
            _settings.Save();
        }

        private void ApplyOpacity()
        {
            Opacity = _settings.Opacity;
            var bg = new SolidColorBrush(Color.FromArgb((byte)Math.Round(_settings.BackgroundOpacity * 255), 0x08, 0x0A, 0x0C));
            bg.Freeze();
            Pill.Background = bg;
        }

        /// <summary>
        /// Move mode: the overlay temporarily accepts the mouse so it can be dragged
        /// anywhere, on any monitor. Turning it off saves the position and makes the
        /// overlay click-through again.
        /// </summary>
        public void ToggleMoveMode()
        {
            _moveMode = !_moveMode;

            long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            ex = _moveMode ? ex & ~NativeMethods.WS_EX_TRANSPARENT : ex | NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

            Pill.BorderThickness = new Thickness(_moveMode ? 2 : 0);
            Pill.BorderBrush = (Brush)FindResource("FpsColor");

            if (_moveMode)
            {
                if (!IsVisible) Show();
            }
            else
            {
                _settings.X = Math.Round(Left);
                _settings.Y = Math.Round(Top);
                _settings.Save();
            }

            RefreshUi();
        }

        private void PlaceWindow()
        {
            if (_moveMode)
                return; // the user is dragging it

            if (_settings.X is { } x && _settings.Y is { } y)
            {
                // Keep it on screen in case the monitor it was on has been unplugged.
                double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
                double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
                Left = Math.Clamp(x, vl, Math.Max(vl, vl + vw - ActualWidth));
                Top = Math.Clamp(y, vt, Math.Max(vt, vt + vh - ActualHeight));
                return;
            }

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
            switch (msg)
            {
                // Belt and braces on top of WS_EX_TRANSPARENT: tell Windows the mouse is
                // never over this window, and never let a click activate it.
                case NativeMethods.WM_NCHITTEST:
                    handled = true;
                    return new IntPtr(_moveMode ? NativeMethods.HTCAPTION : NativeMethods.HTTRANSPARENT);
                case NativeMethods.WM_MOUSEACTIVATE:
                    handled = true;
                    return new IntPtr(NativeMethods.MA_NOACTIVATE);

                case NativeMethods.WM_HOTKEY:
                    switch (wParam.ToInt32())
                    {
                        case HotkeyToggle: ToggleVisibility(); handled = true; break;
                        case HotkeyCorner: CycleCorner(); handled = true; break;
                        case HotkeyFainter: SetOverlayOpacity(_settings.Opacity - 0.1); handled = true; break;
                        case HotkeyStronger: SetOverlayOpacity(_settings.Opacity + 0.1); handled = true; break;
                        case HotkeyMove: ToggleMoveMode(); handled = true; break;
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _uiTimer.Stop();
            _cts.Cancel();
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyToggle);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyCorner);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyFainter);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyStronger);
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyMove);
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
