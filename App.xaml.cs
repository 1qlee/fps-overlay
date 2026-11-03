using System;
using System.Threading;
using System.Windows;

namespace FpsOverlay
{
    public partial class App : Application
    {
        private Mutex? _singleInstance;
        private OverlayWindow? _overlay;
        private TrayIcon? _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _singleInstance = new Mutex(true, @"Local\FpsOverlay-SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                MessageBox.Show("FPS Overlay is already running (see the tray icon).", "FPS Overlay");
                Shutdown();
                return;
            }

            _overlay = new OverlayWindow();
            _overlay.Show();

            _tray = new TrayIcon(_overlay, ExitApp);
        }

        private void ExitApp()
        {
            _tray?.Dispose();
            _overlay?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
