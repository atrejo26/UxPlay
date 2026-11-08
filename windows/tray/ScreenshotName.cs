using System;
using System.IO;

namespace UxPlayTray
{
    /// <summary>File names for screenshots: the date and time, never overwriting an earlier one.</summary>
    static class ScreenshotName
    {
        /// <param name="exists">Whether a file is already there (File.Exists in the app).</param>
        public static string Next(string folder, DateTime now, Func<string, bool> exists)
        {
            string stem = Path.Combine(folder, "UxPlay " + now.ToString("yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture));
            string path = stem + ".png";
            // two in the same second: "... (2).png", "... (3).png"
            for (int n = 2; exists(path); n++)
                path = stem + " (" + n + ").png";
            return path;
        }
    }
}
