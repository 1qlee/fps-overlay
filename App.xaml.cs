using System;
using System.Threading;
using System.Threading.Tasks;
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
            // Never die silently: log and show anything unexpected.
            DispatcherUnhandledException += (_, args) =>
            {
                Fatal(args.Exception);
                args.Handled = true;
                Shutdown(1);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) => Fatal(args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Log.Write("Unobserved task exception: " + args.Exception);
                args.SetObserved();
            };

            base.OnStartup(e);
            Log.Write($"Starting FPS Overlay {typeof(App).Assembly.GetName().Version} on {Environment.OSVersion}");

            _singleInstance = new Mutex(true, @"Local\FpsOverlay-SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                Log.Write("Another instance is already running; exiting.");
                MessageBox.Show("FPS Overlay is already running. Look for its icon in the system tray (click ^ next to the clock).",
                    "FPS Overlay");
                Shutdown();
                return;
            }

            _overlay = new OverlayWindow();
            _overlay.Show();
            Log.Write($"Overlay shown at ({_overlay.Left:0}, {_overlay.Top:0}), size {_overlay.ActualWidth:0}x{_overlay.ActualHeight:0}.");

            _tray = new TrayIcon(_overlay, ExitApp);
            _tray.ShowStartupBalloon();
            Log.Write("Tray icon created. Startup complete.");
        }

        private static void Fatal(Exception? ex)
        {
            Log.Write("FATAL: " + ex);
            MessageBox.Show(
                "FPS Overlay hit an error and has to close:\n\n" + ex?.Message +
                "\n\nDetails were written to:\n" + Log.FilePath,
                "FPS Overlay", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ExitApp()
        {
            Log.Write("Exit requested from tray.");
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
