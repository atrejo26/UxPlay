using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace UxPlayTray
{
    /// <summary>What happened when the test sound was played.</summary>
    enum SoundTestResult
    {
        /// <summary>The sound was sent to the PC's speakers without an error.</summary>
        Played,
        /// <summary>GStreamer could not open the sound output.</summary>
        Failed,
        /// <summary>This copy of UxPlay has no sound test program.</summary>
        Unavailable,
    }

    /// <summary>Plays a short beep through the same sound output the receiver uses.</summary>
    interface ISoundTest
    {
        /// <param name="sink">GStreamer audio sink, or "" for the automatic choice.</param>
        Task<SoundTestResult> PlayAsync(string sink);
    }

    /// <summary>
    /// The beep is played by the bundled gst-launch-1.0.exe with the same GStreamer and
    /// sound output as uxplay.exe, so it tells apart a PC sound problem from a network one.
    /// </summary>
    sealed class GStreamerSoundTest : ISoundTest
    {
        readonly string serverDir;
        readonly string dataDir;

        public GStreamerSoundTest(string serverDir, string dataDir)
        {
            this.serverDir = serverDir;
            this.dataDir = dataDir;
        }

        string LauncherPath => Path.Combine(serverDir, "bin", "gst-launch-1.0.exe");

        /// <summary>gst-launch-1.0 options for about two seconds of a quiet tone.</summary>
        public static string Arguments(string sink)
        {
            string output = string.IsNullOrEmpty(sink) ? "autoaudiosink" : sink;
            return "-q audiotestsrc num-buffers=100 freq=660 volume=0.3 ! audioconvert ! audioresample ! " + output;
        }

        public Task<SoundTestResult> PlayAsync(string sink)
        {
            if (!File.Exists(LauncherPath))
                return Task.FromResult(SoundTestResult.Unavailable);
            return Task.Run(() => Play(sink));
        }

        SoundTestResult Play(string sink)
        {
            Directory.CreateDirectory(dataDir);
            var psi = new ProcessStartInfo(LauncherPath, Arguments(sink))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = dataDir,
            };
            GStreamerEnvironment.Apply(psi, serverDir, dataDir);
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return SoundTestResult.Failed;
                    if (!p.WaitForExit(15000))
                    {
                        try { p.Kill(); } catch (InvalidOperationException) { }
                        return SoundTestResult.Failed;
                    }
                    return p.ExitCode == 0 ? SoundTestResult.Played : SoundTestResult.Failed;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return SoundTestResult.Failed;
            }
        }
    }
}
