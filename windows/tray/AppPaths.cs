using System;
using System.IO;

namespace UxPlayTray
{
    /// <summary>Where the app's files are: the bundled receiver and the per-user data folder.</summary>
    static class AppPaths
    {
        /// <summary>%LOCALAPPDATA%\UxPlay: settings, log, the receiver's key and GStreamer's cache.</summary>
        public static readonly string DataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UxPlay");

        // Layout of the bundle next to UxPlay.exe (see windows/bundle.sh).
        public static readonly string ServerDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uxplay");
        public static readonly string ServerExe = Path.Combine(ServerDir, "bin", "uxplay.exe");

        public static readonly string SettingsFile = Path.Combine(DataDir, "settings.ini");
        public static readonly string LogFile = Path.Combine(DataDir, "uxplay.log");
        public static readonly string ArgsFile = Path.Combine(DataDir, "uxplay-args.txt");
        public static readonly string ErrorLog = Path.Combine(DataDir, "tray-error.log");
    }
}
