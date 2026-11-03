using System;
using System.IO;
using System.Text.Json;

namespace FpsOverlay
{
    /// <summary>User preferences, saved to %APPDATA%\FpsOverlay\settings.json.</summary>
    public sealed class Settings
    {
        /// <summary>Opacity of the whole overlay, text included (0.2–1).</summary>
        public double Opacity { get; set; } = 1.0;

        /// <summary>Opacity of the dark pill behind the text (0–1).</summary>
        public double BackgroundOpacity { get; set; } = 0.9;

        /// <summary>0 = top left, 1 = top right, 2 = bottom right, 3 = bottom left.</summary>
        public int Corner { get; set; }

        public const double MinOpacity = 0.2;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FpsOverlay", "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                    s.Opacity = Math.Clamp(s.Opacity, MinOpacity, 1);
                    s.BackgroundOpacity = Math.Clamp(s.BackgroundOpacity, 0, 1);
                    s.Corner = Math.Clamp(s.Corner, 0, 3);
                    return s;
                }
            }
            catch { /* unreadable file: fall back to defaults */ }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* not worth interrupting the game over */ }
        }
    }
}
