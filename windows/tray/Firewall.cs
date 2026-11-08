using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// The "UxPlay" inbound rule, the same one the installer adds. It is for Private and
    /// Domain networks only: on a network Windows calls Public (a café, hotel or airport
    /// Wi-Fi) nobody can reach UxPlay.
    /// </summary>
    static class FirewallRule
    {
        public const string Name = "UxPlay";
        public const string Profiles = "private,domain";

        /// <summary>Starts UxPlay.exe as administrator just to add the rule.</summary>
        public const string ElevatedSwitch = "--allow-firewall";

        // a full path on a drive, with nothing netsh or a shell would read as more arguments
        static readonly Regex SafePath = new Regex(@"^[A-Za-z]:\\[^""\r\n\t%<>|*?]+\.exe$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static bool IsSafeProgramPath(string path) =>
            path != null && path.Length < 32767 && SafePath.IsMatch(path) && path.IndexOf(@"\..\", StringComparison.Ordinal) < 0;

        /// <summary>netsh's arguments: remove the old rule, then add the new one.</summary>
        public static string[] NetshArguments(string programPath)
        {
            if (!IsSafeProgramPath(programPath))
                throw new ArgumentException("Not a program path the firewall rule can use: " + programPath, nameof(programPath));
            string rule = "advfirewall firewall {0} rule name=\"" + Name + "\"";
            return new[]
            {
                string.Format(rule, "delete"),
                string.Format(rule, "add") + " dir=in action=allow enable=yes profile=" + Profiles +
                    " program=\"" + programPath + "\"",
            };
        }
    }

    /// <summary>
    /// Runs this app again as administrator with <see cref="FirewallRule.ElevatedSwitch"/>.
    /// No command shell is involved, so no folder name can turn into a command run with
    /// administrator rights.
    /// </summary>
    [ExcludeFromCodeCoverage]  // needs Windows and a UAC prompt
    sealed class WindowsFirewall : IFirewall
    {
        readonly string selfExe;

        public WindowsFirewall(string selfExe)
        {
            this.selfExe = selfExe;
        }

        /// <param name="programPath">Must be uxplay.exe: the copy running as administrator works it out itself.</param>
        public int? Allow(string programPath)
        {
            if (!FirewallRule.IsSafeProgramPath(programPath))
                return -1;
            var psi = new ProcessStartInfo(selfExe, FirewallRule.ElevatedSwitch)
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

        /// <summary>The part that runs as administrator. Returns netsh's exit code for adding the rule.</summary>
        public static int ApplyElevated(string programPath)
        {
            if (!FirewallRule.IsSafeProgramPath(programPath) || !File.Exists(programPath))
                return -1;
            // netsh from System32 itself, never one found on PATH
            string netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
            int code = -1;
            foreach (string args in FirewallRule.NetshArguments(programPath))
            {
                var psi = new ProcessStartInfo(netsh, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                p.WaitForExit();
                code = p.ExitCode;  // "delete" fails when there is no old rule; only "add" counts
            }
            return code;
        }
    }
}
