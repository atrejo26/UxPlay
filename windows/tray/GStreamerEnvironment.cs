using System.Diagnostics;
using System.IO;

namespace UxPlayTray
{
    /// <summary>
    /// Points a bundled GStreamer program (uxplay.exe, gst-launch-1.0.exe) at the plugins
    /// shipped with this app, ignoring any other GStreamer installed on the PC.
    /// </summary>
    static class GStreamerEnvironment
    {
        public static void Apply(ProcessStartInfo psi, string serverDir, string dataDir)
        {
            var env = psi.EnvironmentVariables;
            env["PATH"] = Path.Combine(serverDir, "bin") + ";" + env["PATH"];
            env["GST_PLUGIN_SYSTEM_PATH_1_0"] = Path.Combine(serverDir, "lib", "gstreamer-1.0");
            env["GST_PLUGIN_SCANNER_1_0"] = Path.Combine(serverDir, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");
            env["GST_REGISTRY_1_0"] = Path.Combine(dataDir, "gst-registry.bin");
            env.Remove("GST_PLUGIN_PATH");
            env.Remove("GST_PLUGIN_PATH_1_0");
            env.Remove("GST_PLUGIN_SYSTEM_PATH");
            env["FONTCONFIG_PATH"] = Path.Combine(serverDir, "etc", "fonts");
            // uxplay looks for ~/.uxplayrc and similar files under HOME
            env["HOME"] = dataDir;
        }
    }
}
