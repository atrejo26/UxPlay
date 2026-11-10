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
    /// <para>
    /// The one exception is this PC's own Mobile hotspot, for networks (a campus, school
    /// or office) that keep phones from finding PCs: the iPhone joins the PC's hotspot
    /// instead. Windows calls that network Public, so the "UxPlay (Mobile hotspot)" rules
    /// let in only devices on the hotspot's subnet, and only to the hotspot's own
    /// address plus the "where is UxPlay?" questions iPhones ask (mDNS).
    /// </para>
    /// </summary>
    static class FirewallRule
    {
        public const string Name = "UxPlay";
        public const string Profiles = "private,domain";

        public const string HotspotName = "UxPlay (Mobile hotspot)";
        /// <summary>Windows' Mobile hotspot is always this PC at 192.168.137.1, with its devices on 192.168.137.x.</summary>
        public const string HotspotAddress = "192.168.137.1";
        public const string HotspotSubnet = "192.168.137.0/24";

        /// <summary>Starts UxPlay.exe as administrator just to add the rule.</summary>
        public const string ElevatedSwitch = "--allow-firewall";

        // a full path on a drive, with nothing netsh or a shell would read as more arguments
        static readonly Regex SafePath = new Regex(@"^[A-Za-z]:\\[^""\r\n\t%<>|*?]+\.exe$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static bool IsSafeProgramPath(string path) =>
            path != null && path.Length < 32767 && SafePath.IsMatch(path) && path.IndexOf(@"\..\", StringComparison.Ordinal) < 0;

        /// <summary>
        /// netsh's arguments: remove the old rules, then add the new ones. Deleting
        /// fails harmlessly when there is no old rule; every "add" must succeed.
        /// </summary>
        public static string[] NetshArguments(string programPath)
        {
            if (!IsSafeProgramPath(programPath))
                throw new ArgumentException("Not a program path the firewall rule can use: " + programPath, nameof(programPath));
            string rule = "advfirewall firewall {0} rule name=\"" + Name + "\"";
            string hotspot = "advfirewall firewall {0} rule name=\"" + HotspotName + "\"";
            string program = " program=\"" + programPath + "\"";
            string fromHotspot = " dir=in action=allow enable=yes profile=public remoteip=" + HotspotSubnet;
            return new[]
            {
                string.Format(rule, "delete"),
                string.Format(rule, "add") + " dir=in action=allow enable=yes profile=" + Profiles + program,
                string.Format(hotspot, "delete"),
                // mirroring: only to this PC's hotspot address
                string.Format(hotspot, "add") + fromHotspot + " localip=" + HotspotAddress + program,
                // finding the PC: iPhones ask by multicast, to 224.0.0.251 port 5353
                string.Format(hotspot, "add") + fromHotspot + " protocol=udp localport=5353" + program,
            };
        }

        /// <summary>True for the netsh commands that must succeed (adding a rule).</summary>
        public static bool MustSucceed(string netshArguments) =>
            netshArguments != null && netshArguments.StartsWith("advfirewall firewall add rule ", StringComparison.Ordinal);

        /// <summary>The overall result: the first failed "add", or 0 when they all worked.</summary>
        public static int CombinedExitCode(string[] netshArguments, int[] exitCodes)
        {
            for (int i = 0; i < netshArguments.Length && i < exitCodes.Length; i++)
                if (MustSucceed(netshArguments[i]) && exitCodes[i] != 0)
                    return exitCodes[i];
            return netshArguments.Length == exitCodes.Length ? 0 : -1;
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

        /// <summary>The part that runs as administrator. Returns 0, or netsh's exit code for the first rule it could not add.</summary>
        public static int ApplyElevated(string programPath)
        {
            if (!FirewallRule.IsSafeProgramPath(programPath) || !File.Exists(programPath))
                return -1;
            // netsh from System32 itself, never one found on PATH
            string netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
            string[] commands = FirewallRule.NetshArguments(programPath);
            var codes = new int[commands.Length];
            for (int i = 0; i < commands.Length; i++)
            {
                var psi = new ProcessStartInfo(netsh, commands[i])
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                p.WaitForExit();
                codes[i] = p.ExitCode;
            }
            return FirewallRule.CombinedExitCode(commands, codes);
        }
    }
}
