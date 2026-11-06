using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace UxPlayTray
{
    /// <summary>
    /// Lives in the system tray, runs uxplay.exe hidden in the background and
    /// restarts it whenever settings change.
    /// </summary>
    sealed class TrayApp : ApplicationContext
    {
        // Layout of the bundle next to UxPlay.exe (see windows/bundle.sh).
        static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
        static readonly string ServerDir = Path.Combine(AppDir, "uxplay");
        static readonly string ServerExe = Path.Combine(ServerDir, "bin", "uxplay.exe");
        static readonly string LogPath = Path.Combine(Settings.DataDir, "uxplay.log");

        static readonly Regex ConnectRequest = new Regex(@"connection request from (.+?) \((.*?)\) with deviceID");

        readonly NotifyIcon tray;
        readonly Control ui;  // marshals process events onto the UI thread
        readonly ToolStripMenuItem statusItem, startStopItem, restartItem, fullScreenItem;
        readonly JobObject job = new JobObject();
        readonly EventWaitHandle showEvent;
        readonly RegisteredWaitHandle showWait;

        Settings settings = Settings.Load();
        Process server;
        StreamWriter log;
        bool stopping;
        DateTime startedAt;
        SettingsForm settingsForm;

        public TrayApp()
        {
            ui = new Control();
            ui.CreateControl();

            statusItem = new ToolStripMenuItem("UxPlay") { Enabled = false };
            statusItem.Font = new Font(statusItem.Font, FontStyle.Bold);
            startStopItem = new ToolStripMenuItem("Stop", null, (s, e) => ToggleServer());
            restartItem = new ToolStripMenuItem("Restart", null, (s, e) => RestartServer());
            fullScreenItem = new ToolStripMenuItem("Full screen", null, (s, e) => ToggleFullScreen())
            {
                Checked = settings.FullScreen,
            };

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[]
            {
                statusItem,
                new ToolStripMenuItem("How to connect my iPhone", null, (s, e) => ShowHowToConnect()),
                new ToolStripSeparator(),
                startStopItem,
                restartItem,
                fullScreenItem,
                new ToolStripMenuItem("Settings...", null, (s, e) => ShowSettings()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Troubleshooting", null,
                    new ToolStripMenuItem("Allow through Windows Firewall", null, (s, e) => FixFirewall()),
                    new ToolStripMenuItem("Show log", null, (s, e) => ShowLog())),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Quit", null, (s, e) => Quit()),
            });

            tray = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "UxPlay",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.DoubleClick += (s, e) => ShowHowToConnect();

            // A second launch of UxPlay.exe signals this event instead of starting another copy.
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
                (state, timedOut) => OnUi(ShowHowToConnect), null, Timeout.Infinite, false);

            if (!File.Exists(ServerExe))
            {
                SetStatus("UxPlay files are missing");
                MessageBox.Show("Could not find:\n" + ServerExe +
                    "\n\nPlease reinstall UxPlay or unzip the whole download folder.",
                    "UxPlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            KillLeftoverServers();
            if (StartServer())
                tray.ShowBalloonTip(8000, "UxPlay is ready",
                    "On your iPhone: open Control Center, tap Screen Mirroring, then choose \"" +
                    Settings.CleanName(settings.Name) + "\".", ToolTipIcon.Info);
        }

        static Icon LoadIcon()
        {
            using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("UxPlayTray.uxplay.ico");
            return new Icon(stream, SystemInformation.SmallIconSize);
        }

        void OnUi(Action action)
        {
            if (ui.IsHandleCreated)
                ui.BeginInvoke(action);
        }

        // ---- uxplay.exe process -------------------------------------------------

        bool StartServer()
        {
            if (server != null)
                return true;
            Directory.CreateDirectory(Settings.DataDir);

            var psi = new ProcessStartInfo(ServerExe, settings.BuildArguments())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Settings.DataDir,
            };
            ConfigureEnvironment(psi);

            try
            {
                log = new StreamWriter(LogPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                log.WriteLine("> uxplay.exe " + psi.Arguments);
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (s, e) => OnOutput(e.Data);
                p.ErrorDataReceived += (s, e) => OnOutput(e.Data);
                p.Exited += (s, e) => OnUi(() => OnServerExited(p));
                p.Start();
                job.Add(p);  // uxplay.exe is closed automatically if this app goes away
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                server = p;
                startedAt = DateTime.Now;
                stopping = false;
                SetStatus("Ready - waiting for iPhone");
                return true;
            }
            catch (Exception ex)
            {
                CloseLog();
                SetStatus("Stopped");
                MessageBox.Show("Could not start UxPlay:\n" + ex.Message, "UxPlay",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>Points GStreamer at the plugins bundled with this app, ignoring any other GStreamer install.</summary>
        static void ConfigureEnvironment(ProcessStartInfo psi)
        {
            var env = psi.EnvironmentVariables;
            string bin = Path.Combine(ServerDir, "bin");
            env["PATH"] = bin + ";" + env["PATH"];
            env["GST_PLUGIN_SYSTEM_PATH_1_0"] = Path.Combine(ServerDir, "lib", "gstreamer-1.0");
            env["GST_PLUGIN_SCANNER_1_0"] = Path.Combine(ServerDir, "libexec", "gstreamer-1.0", "gst-plugin-scanner.exe");
            env["GST_REGISTRY_1_0"] = Path.Combine(Settings.DataDir, "gst-registry.bin");
            env.Remove("GST_PLUGIN_PATH");
            env.Remove("GST_PLUGIN_PATH_1_0");
            env.Remove("GST_PLUGIN_SYSTEM_PATH");
            env["FONTCONFIG_PATH"] = Path.Combine(ServerDir, "etc", "fonts");
            // uxplay looks for ~/.uxplayrc and similar files under HOME
            env["HOME"] = Settings.DataDir;
        }

        void OnOutput(string line)
        {
            if (line == null)
                return;
            try { log?.WriteLine(line); } catch (ObjectDisposedException) { }

            var m = ConnectRequest.Match(line);
            if (m.Success)
            {
                string device = m.Groups[1].Value.Trim();
                OnUi(() =>
                {
                    SetStatus("Connected: " + device);
                    tray.ShowBalloonTip(3000, "UxPlay", device + " is connecting...", ToolTipIcon.None);
                });
            }
        }

        void OnServerExited(Process p)
        {
            if (p != server)
                return;
            server = null;
            int code = SafeExitCode(p);
            p.Dispose();
            CloseLog();

            if (stopping)
            {
                SetStatus("Stopped");
                return;
            }

            // Unexpected exit: restart automatically if it had been running a while,
            // otherwise something is wrong with the setup, so tell the user.
            if ((DateTime.Now - startedAt).TotalSeconds > 20)
            {
                StartServer();
                return;
            }
            SetStatus("Stopped (error)");
            var answer = MessageBox.Show(
                "UxPlay stopped unexpectedly (exit code " + code + ").\n\nOpen the log to see what went wrong?",
                "UxPlay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
                ShowLog();
        }

        static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch (InvalidOperationException) { return -1; }
        }

        void StopServer()
        {
            var p = server;
            if (p == null)
                return;
            stopping = true;
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            server = null;
            p.Dispose();
            CloseLog();
            SetStatus("Stopped");
        }

        void RestartServer()
        {
            StopServer();
            StartServer();
        }

        void ToggleServer()
        {
            if (server != null)
                StopServer();
            else
                StartServer();
        }

        void CloseLog()
        {
            var l = log;
            log = null;
            try { l?.Dispose(); } catch (IOException) { }
        }

        /// <summary>Ends copies of the bundled uxplay.exe left behind by a crash.</summary>
        static void KillLeftoverServers()
        {
            foreach (var p in Process.GetProcessesByName("uxplay"))
            {
                try
                {
                    if (string.Equals(p.MainModule.FileName, ServerExe, StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch (Exception)
                {
                    // not ours, or no access
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        // ---- menu actions -------------------------------------------------------

        void SetStatus(string text)
        {
            statusItem.Text = "UxPlay: " + text;
            string tip = "UxPlay - " + text;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;  // NotifyIcon limit
            startStopItem.Text = server != null ? "Stop" : "Start";
            restartItem.Enabled = server != null;
        }

        void ShowHowToConnect()
        {
            MessageBox.Show(
                "To show your iPhone on this PC:\n\n" +
                "1. Connect your iPhone and this PC to the same Wi-Fi network.\n" +
                "2. On the iPhone, swipe down from the top-right corner to open Control Center.\n" +
                "3. Tap Screen Mirroring.\n" +
                "4. Choose \"" + Settings.CleanName(settings.Name) + "\".\n\n" +
                "To stop, tap Screen Mirroring again and choose Stop Mirroring.\n\n" +
                "Your iPhone doesn't list this PC? Right-click the UxPlay icon near the clock and choose " +
                "Troubleshooting > Allow through Windows Firewall.",
                "UxPlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ToggleFullScreen()
        {
            settings.FullScreen = !settings.FullScreen;
            fullScreenItem.Checked = settings.FullScreen;
            settings.Save();
            if (server != null)
                RestartServer();
        }

        void ShowSettings()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            using (settingsForm = new SettingsForm(settings))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    settings.Save();
                    fullScreenItem.Checked = settings.FullScreen;
                    RestartServer();
                }
            }
            settingsForm = null;
        }

        void ShowLog()
        {
            if (!File.Exists(LogPath))
            {
                MessageBox.Show("There is no log yet.", "UxPlay");
                return;
            }
            // Copy it so Notepad can open it while uxplay keeps writing.
            string copy = Path.Combine(Path.GetTempPath(), "uxplay-log.txt");
            using (var src = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = File.Create(copy))
                src.CopyTo(dst);
            Process.Start("notepad.exe", "\"" + copy + "\"")?.Dispose();
        }

        static void FixFirewall()
        {
            // Needs administrator rights, so Windows will ask for permission.
            string rule = "advfirewall firewall {0} rule name=\"UxPlay\"";
            string cmd =
                "netsh " + string.Format(rule, "delete") + " >nul 2>&1 & " +
                "netsh " + string.Format(rule, "add") + " dir=in action=allow enable=yes profile=any " +
                "program=\"" + ServerExe + "\"";
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
                if (p.ExitCode == 0)
                    MessageBox.Show("UxPlay is now allowed through Windows Firewall.", "UxPlay",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show("Windows Firewall could not be updated (error " + p.ExitCode + ").", "UxPlay",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // the user said No to the permission prompt
            }
        }

        void Quit()
        {
            StopServer();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopServer();
                showWait.Unregister(null);
                showEvent.Dispose();
                tray.Visible = false;
                tray.Dispose();
                ui.Dispose();
                job.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
