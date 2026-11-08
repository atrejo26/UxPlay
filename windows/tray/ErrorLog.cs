using System;
using System.IO;

namespace UxPlayTray
{
    /// <summary>
    /// tray-error.log: unexpected errors of the tray app, for troubleshooting. An error that
    /// keeps coming back must not fill the disk, so past the size limit the file is moved
    /// to tray-error.old.log (replacing the older one) and a new one is started.
    /// </summary>
    static class ErrorLog
    {
        public const long MaxBytes = 1024 * 1024;

        /// <summary>Adds one error; false when it couldn't be saved.</summary>
        public static bool Append(string path, DateTime when, Exception error)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    string old = OldPath(path);
                    File.Delete(old);
                    File.Move(path, old);
                }
                File.AppendAllText(path, when.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +
                    "  " + (error?.ToString() ?? "unknown error") + Environment.NewLine + Environment.NewLine);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                                       ex is NotSupportedException)
            {
                return false;
            }
        }

        public static string OldPath(string path) =>
            Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".old" + Path.GetExtension(path));
    }
}
