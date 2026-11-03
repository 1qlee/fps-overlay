using System;
using Forms = System.Windows.Forms;

namespace FpsOverlay
{
    /// <summary>Notification-area icon: the only way to interact with the click-through overlay by mouse.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;

        public TrayIcon(OverlayWindow overlay, Action exit)
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Show / hide   (Ctrl+Shift+F10)", null, (_, _) => overlay.ToggleVisibility());
            menu.Items.Add("Move to next corner   (Ctrl+Shift+F11)", null, (_, _) => overlay.CycleCorner());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => exit());

            _icon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "FPS Overlay",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.DoubleClick += (_, _) => overlay.ToggleVisibility();
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
