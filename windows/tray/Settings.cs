using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace UxPlayTray
{
    /// <summary>User settings, stored as key=value lines in %LOCALAPPDATA%\UxPlay\settings.ini.</summary>
    sealed class Settings
    {
        public static readonly string DataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UxPlay");
        static readonly string FilePath = Path.Combine(DataDir, "settings.ini");

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "UxPlay";

        public static readonly (string Label, string Size)[] Resolutions =
        {
            ("1080p (recommended)", ""),
            ("720p (smoother on slow Wi-Fi)", "1280x720"),
            ("1440p (sharper, needs good Wi-Fi)", "2560x1440"),
        };

        public static readonly (string Label, int Fps)[] FrameRates =
        {
            ("60 fps - smoothest (recommended)", 60),
            ("30 fps - for older PCs or slow Wi-Fi", 30),
        };

        public string Name = Environment.MachineName;
        public bool FullScreen = false;
        public string Resolution = "";
        public int Fps = 60;
        public bool Audio = true;
        public bool RequirePin = false;
        public string ExtraArgs = "";

        public static Settings Load()
        {
            var s = new Settings();
            string[] lines;
            try
            {
                if (!File.Exists(FilePath))
                    return s;
                lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return s;  // unreadable: start with the defaults
            }
            foreach (var line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "name": if (val.Length > 0) s.Name = val; break;
                    case "fullscreen": s.FullScreen = val == "1"; break;
                    case "resolution": s.Resolution = val; break;
                    case "fps": if (int.TryParse(val, out int fps) && fps > 0 && fps <= 60) s.Fps = fps; break;
                    case "audio": s.Audio = val != "0"; break;
                    case "pin": s.RequirePin = val == "1"; break;
                    case "extra": s.ExtraArgs = val; break;
                }
            }
            // "-vs d3d11videosink" was the manual workaround for full screen before the
            // app chose the Direct3D output itself; drop it so the built-in toggle works.
            if (s.ExtraArgs == "-vs d3d11videosink")
                s.ExtraArgs = "";
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDir);
            var lines = new List<string>
            {
                "name=" + Name,
                "fullscreen=" + (FullScreen ? "1" : "0"),
                "resolution=" + Resolution,
                "fps=" + Fps,
                "audio=" + (Audio ? "1" : "0"),
                "pin=" + (RequirePin ? "1" : "0"),
                "extra=" + ExtraArgs,
            };
            File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
        }

        /// <summary>Command line for uxplay.exe built from these settings.</summary>
        public string BuildArguments()
        {
            var args = new List<string>();
            // Show exactly this name on the iPhone (no "@hostname" suffix).
            args.Add("-n " + Quote(CleanName(Name)));
            args.Add("-nh");
            // Keep the same identity across restarts so the iPhone recognises this PC.
            args.Add("-key " + Quote(Path.Combine(DataDir, "uxplay.pem")));
            // A new connection takes over instead of being refused.
            args.Add("-nohold");
            if (HasOwnVideoSink)
            {
                // The user chose a video output in Advanced options; leave it alone.
                if (FullScreen)
                    args.Add("-fs");
            }
            else
            {
                // Direct3D 11 output: borderless full screen that can be switched with
                // Alt+Enter, or from the tray menu, while mirroring.
                string sink = "d3d11videosink fullscreen-toggle-mode=alt-enter+property";
                if (FullScreen)
                    sink += " fullscreen=true";
                args.Add("-vs " + Quote(sink));
            }
            if (Resolution.Length > 0)
                args.Add("-s " + Resolution);
            // uxplay asks the iPhone for at most 30 frames per second unless told otherwise.
            args.Add("-fps " + Fps);
            if (!Audio)
                args.Add("-as 0");
            if (RequirePin)
                args.Add("-pin -reg " + Quote(Path.Combine(DataDir, "uxplay.register")));
            if (ExtraArgs.Length > 0)
                args.Add(ExtraArgs);
            return string.Join(" ", args);
        }

        /// <summary>True when the Advanced options pick their own video output (-vs).</summary>
        public bool HasOwnVideoSink =>
            (" " + ExtraArgs + " ").IndexOf(" -vs ", StringComparison.Ordinal) >= 0;

        public static string CleanName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
                if (c != '"' && c != '\\' && !char.IsControl(c))
                    sb.Append(c);
            string cleaned = sb.ToString().Trim();
            return cleaned.Length > 0 ? cleaned : Environment.MachineName;
        }

        static string Quote(string s) => "\"" + s + "\"";

        public static bool StartWithWindows
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) != null;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                    key.SetValue(RunValue, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                else
                    key.DeleteValue(RunValue, false);
            }
        }
    }
}
