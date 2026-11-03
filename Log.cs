using System;
using System.IO;

namespace FpsOverlay
{
    /// <summary>Plain-text log at %LOCALAPPDATA%\FpsOverlay\log.txt, for diagnosing startup problems.</summary>
    internal static class Log
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FpsOverlay", "log.txt");

        private static readonly object Gate = new();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    // Keep the log small: start over once it passes 1 MB.
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000)
                        File.Delete(FilePath);
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
                }
            }
            catch { /* logging must never take the app down */ }
        }
    }
}
