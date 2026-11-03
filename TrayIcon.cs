using System;
using Forms = System.Windows.Forms;

namespace FpsOverlay
{
    /// <summary>
    /// Notification-area icon. The overlay itself ignores the mouse entirely,
    /// so this menu is where it's controlled with the mouse.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private static readonly int[] OverlayLevels = { 100, 90, 80, 70, 60, 50, 40, 30, 20 };
        private static readonly int[] BackgroundLevels = { 100, 90, 75, 60, 45, 30, 15, 0 };

        private readonly Forms.NotifyIcon _icon;

        public TrayIcon(OverlayWindow overlay, Action exit)
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Show / hide   (Ctrl+Shift+F10)", null, (_, _) => overlay.ToggleVisibility());
            var edit = new Forms.ToolStripMenuItem("Edit position && size   (Ctrl+Shift+F7)");
            edit.Click += (_, _) => overlay.ToggleEditMode();
            menu.Items.Add(edit);
            menu.Items.Add("Snap to next corner   (Ctrl+Shift+F11)", null, (_, _) => overlay.CycleCorner());
            menu.Opening += (_, _) => edit.Checked = overlay.IsInEditMode;
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(PercentMenu("Overlay opacity   (Ctrl+Shift+F8 / F9)", OverlayLevels,
                () => overlay.OverlayOpacity, overlay.SetOverlayOpacity));
            menu.Items.Add(PercentMenu("Background opacity", BackgroundLevels,
                () => overlay.BackgroundOpacity, overlay.SetBackgroundOpacity));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => exit());

            _icon = new Forms.NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "FPS Overlay",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.DoubleClick += (_, _) => overlay.ToggleVisibility();

            overlay.EditModeChanged += editing =>
            {
                if (editing)
                    _icon.ShowBalloonTip(4000, "Editing overlay",
                        "Drag it to move. Drag a corner to resize. Press Ctrl+Shift+F7 again (or use this menu) when done.",
                        Forms.ToolTipIcon.None);
            };
        }

        /// <summary>The app icon (app.ico, embedded) at the tray's small-icon size.</summary>
        private static System.Drawing.Icon LoadAppIcon()
        {
            try
            {
                using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("FpsOverlay.app.ico");
                if (stream is not null)
                    return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
            catch (Exception ex)
            {
                Log.Write("Couldn't load tray icon: " + ex.Message);
            }
            return System.Drawing.SystemIcons.Application;
        }

        /// <summary>A submenu of percentages with a check next to the current value.</summary>
        private static Forms.ToolStripMenuItem PercentMenu(string text, int[] levels, Func<double> get, Action<double> set)
        {
            var parent = new Forms.ToolStripMenuItem(text);
            foreach (int pct in levels)
            {
                var item = new Forms.ToolStripMenuItem(pct + "%");
                item.Click += (_, _) => set(pct / 100.0);
                parent.DropDownItems.Add(item);
            }

            // Values can also change via hotkeys, so refresh the checks each time it opens.
            parent.DropDownOpening += (_, _) =>
            {
                int current = (int)Math.Round(get() * 100);
                for (int i = 0; i < levels.Length; i++)
                    ((Forms.ToolStripMenuItem)parent.DropDownItems[i]).Checked = levels[i] == current;
            };
            return parent;
        }

        /// <summary>
        /// Windows 11 hides new tray icons behind the ^ overflow, so say where we are.
        /// </summary>
        public void ShowStartupBalloon()
        {
            _icon.ShowBalloonTip(5000, "FPS Overlay is running",
                "Right-click this icon for options. Ctrl+Shift+F10 shows or hides the overlay.",
                Forms.ToolTipIcon.Info);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
