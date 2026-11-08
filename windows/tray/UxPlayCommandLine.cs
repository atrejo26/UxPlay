using System.Collections.Generic;
using System.IO;

namespace UxPlayTray
{
    /// <summary>Turns <see cref="Settings"/> into options for uxplay.exe.</summary>
    static class UxPlayCommandLine
    {
        /// <param name="dataDir">Folder for the receiver's key and PIN register.</param>
        public static string Build(Settings s, string dataDir)
        {
            var args = new List<string>();
            // Show exactly this name on the iPhone (no "@hostname" suffix).
            args.Add("-n " + Quote(s.DisplayName));
            args.Add("-nh");
            // Keep the same identity across restarts so the iPhone recognises this PC.
            args.Add("-key " + Quote(Path.Combine(dataDir, "uxplay.pem")));
            // A new connection takes over instead of being refused.
            args.Add("-nohold");
            if (s.HasOwnVideoSink)
            {
                // The user chose a video output in Advanced options; leave it alone.
                if (s.FullScreen)
                    args.Add("-fs");
            }
            else
            {
                // Direct3D 11 output: borderless full screen that can be switched with
                // Alt+Enter, or from the tray menu, while mirroring.
                string sink = "d3d11videosink fullscreen-toggle-mode=alt-enter+property";
                if (s.FullScreen)
                    sink += " fullscreen=true";
                args.Add("-vs " + Quote(sink));
            }
            if (s.Resolution.Length > 0)
                args.Add("-s " + s.Resolution);
            // uxplay asks the iPhone for at most 30 frames per second unless told otherwise.
            args.Add("-fps " + s.Fps);
            if (!s.Audio)
                args.Add("-as 0");
            else if (s.AudioSink.Length > 0 && !s.HasOption("-as"))
                args.Add("-as " + s.AudioSink);
            // A random code each time; -reg remembers paired iPhones so it is asked only once.
            if (s.RequirePin)
                args.Add("-pin -reg " + Quote(Path.Combine(dataDir, "uxplay.register")));
            if (s.ExtraArgs.Length > 0)
                args.Add(s.ExtraArgs);
            return string.Join(" ", args);
        }

        static string Quote(string s) => "\"" + s + "\"";
    }
}
