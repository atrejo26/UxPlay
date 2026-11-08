using System;
using System.Text;

namespace UxPlayTray
{
    /// <summary>
    /// What the user chose in the Settings window. Plain data: saving it is
    /// <see cref="ISettingsStore"/>'s job and turning it into uxplay options is
    /// <see cref="UxPlayCommandLine"/>'s.
    /// </summary>
    sealed class Settings
    {
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

        public static readonly (string Label, string Sink)[] AudioOutputs =
        {
            ("Automatic (recommended)", ""),
            ("Windows audio (WASAPI)", "wasapisink"),
            ("Compatibility mode (DirectSound)", "directsoundsink"),
        };

        public string Name = Environment.MachineName;
        public bool FullScreen = false;
        public string Resolution = "";
        public int Fps = 60;
        public bool Audio = true;
        public string AudioSink = "";
        public bool RequirePin = false;
        public string ExtraArgs = "";

        /// <summary>The name shown on the iPhone, as uxplay will use it.</summary>
        public string DisplayName => CleanName(Name);

        /// <summary>True when the Advanced options pick their own video output (-vs).</summary>
        public bool HasOwnVideoSink => HasOption("-vs");

        /// <summary>True when the Advanced options contain this uxplay option.</summary>
        public bool HasOption(string option) =>
            (" " + ExtraArgs + " ").IndexOf(" " + option + " ", StringComparison.Ordinal) >= 0;

        public static string CleanName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
                if (c != '"' && c != '\\' && !char.IsControl(c))
                    sb.Append(c);
            string cleaned = sb.ToString().Trim();
            return cleaned.Length > 0 ? cleaned : Environment.MachineName;
        }
    }
}
