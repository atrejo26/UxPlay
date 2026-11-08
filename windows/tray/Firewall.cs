using System.Diagnostics;

namespace UxPlayTray
{
    /// <summary>Lets a program through Windows Firewall.</summary>
    interface IFirewall
    {
        /// <summary>
        /// Adds (or replaces) the rule for the program; Windows asks for administrator
        /// rights. Returns netsh's exit code (0 = done), or null if the user said No.
        /// </summary>
        int? Allow(string programPath);
    }

    /// <summary>The "UxPlay" inbound rule, the same one the installer adds.</summary>
    sealed class WindowsFirewall : IFirewall
    {
        public int? Allow(string programPath)
        {
            string rule = "advfirewall firewall {0} rule name=\"UxPlay\"";
            string cmd =
                "netsh " + string.Format(rule, "delete") + " >nul 2>&1 & " +
                "netsh " + string.Format(rule, "add") + " dir=in action=allow enable=yes profile=any " +
                "program=\"" + programPath + "\"";
            var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            try
            {
                using var p = Process.Start(psi);
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;  // the user said No to the permission prompt
            }
        }
    }
}
