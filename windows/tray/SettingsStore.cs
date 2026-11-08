using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UxPlayTray
{
    /// <summary>Loads and saves <see cref="Settings"/>.</summary>
    interface ISettingsStore
    {
        /// <summary>The saved settings, or the defaults when there are none or they can't be read.</summary>
        Settings Load();

        /// <summary>Throws IOException or UnauthorizedAccessException when the settings can't be written.</summary>
        void Save(Settings settings);
    }

    /// <summary>Settings as key=value lines in a text file (%LOCALAPPDATA%\UxPlay\settings.ini).</summary>
    sealed class IniSettingsStore : ISettingsStore
    {
        readonly string path;

        public IniSettingsStore(string path)
        {
            this.path = path;
        }

        public Settings Load()
        {
            string[] lines;
            try
            {
                if (!File.Exists(path))
                    return new Settings();
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new Settings();  // unreadable: start with the defaults
            }
            return Parse(lines);
        }

        public static Settings Parse(IEnumerable<string> lines)
        {
            var s = new Settings();
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
                    case "audiosink": if (Array.Exists(Settings.AudioOutputs, o => o.Sink == val)) s.AudioSink = val; break;
                    case "pin": s.RequirePin = val == "1"; break;
                    case "extra": s.ExtraArgs = val; break;
                    case "updates": s.CheckForUpdates = val != "0"; break;
                    case "ontop": s.KeepOnTop = val == "1"; break;
                    case "record": s.Record = val == "1"; break;
                }
            }
            // "-vs d3d11videosink" was the manual workaround for full screen before the
            // app chose the Direct3D output itself; drop it so the built-in toggle works.
            if (s.ExtraArgs == "-vs d3d11videosink")
                s.ExtraArgs = "";
            return s;
        }

        public static IEnumerable<string> Format(Settings s) => new[]
        {
            "name=" + s.Name,
            "fullscreen=" + (s.FullScreen ? "1" : "0"),
            "resolution=" + s.Resolution,
            "fps=" + s.Fps,
            "audio=" + (s.Audio ? "1" : "0"),
            "audiosink=" + s.AudioSink,
            "pin=" + (s.RequirePin ? "1" : "0"),
            "extra=" + s.ExtraArgs,
            "updates=" + (s.CheckForUpdates ? "1" : "0"),
            "ontop=" + (s.KeepOnTop ? "1" : "0"),
            "record=" + (s.Record ? "1" : "0"),
        };

        public void Save(Settings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, Format(settings), new UTF8Encoding(false));
        }
    }
}
