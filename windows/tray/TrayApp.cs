using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace UxPlayTray
{
    /// <summary>Everything the tray app works with, so each part can be swapped or tested on its own.</summary>
    sealed class TrayServices
    {
        public IReceiver Receiver;
        public ISettingsStore SettingsStore;
        public IStartupRegistration Startup;
        public IMirrorWindow MirrorWindow;
        public IKeepAwake KeepAwake;
        public IFirewall Firewall;
        public ISoundTest SoundTest;
        public IUpdateCheck UpdateCheck;
        public IPrompt Prompt;
        public INetworkProfiles NetworkProfiles;
    }

    /// <summary>
    /// The tray icon and its menu. Coordinates the receiver, the settings and the
    /// windows; the work itself is done by the services it is given.
    /// </summary>
    sealed class TrayApp : ApplicationContext
    {
        const string ReleasesUrl = "https://github.com/atrejo26/UxPlay/releases";

        readonly IReceiver receiver;
        readonly ISettingsStore store;
        readonly IStartupRegistration startup;
        readonly IMirrorWindow mirrorWindow;
        readonly IKeepAwake keepAwake;
        readonly IFirewall firewall;
        readonly ISoundTest soundTest;
        readonly IUpdateCheck updateCheck;
        readonly IPrompt prompt;
        readonly INetworkProfiles networkProfiles;
        readonly ConnectionMonitor connection = new ConnectionMonitor();
        readonly RestartPolicy restartPolicy = new RestartPolicy();

        readonly NotifyIcon tray;
        readonly Icon readyIcon, connectedIcon, stoppedIcon;
        readonly Control ui;  // marshals receiver events onto the UI thread
        readonly ToolStripMenuItem statusItem, startStopItem, restartItem, fullScreenItem, onTopItem, soundTestItem, recordingsItem, screenshotItem;
        readonly EventWaitHandle showEvent;
        readonly RegisteredWaitHandle showWait;
        readonly System.Windows.Forms.Timer pendingRestartTimer;
        readonly System.Windows.Forms.Timer onTopTimer;
        readonly OnTopWatcher onTop;

        readonly Settings settings;
        SettingsForm settingsForm;
        PinForm pinForm;
        string balloonLink;   // opened when the balloon on show is clicked
        bool restartPending;  // apply changed settings once nothing is being mirrored

        public TrayApp(TrayServices services)
        {
            receiver = services.Receiver;
            store = services.SettingsStore;
            startup = services.Startup;
            mirrorWindow = services.MirrorWindow;
            keepAwake = services.KeepAwake;
            firewall = services.Firewall;
            soundTest = services.SoundTest;
            updateCheck = services.UpdateCheck;
            prompt = services.Prompt;
            networkProfiles = services.NetworkProfiles;
            settings = store.Load();

            ui = new Control();
            ui.CreateControl();

            receiver.OutputReceived += line => OnUi(() => connection.OnLine(line));
            receiver.ExitedUnexpectedly += code => OnUi(() => OnReceiverExited(code));
            connection.PinRequested += ShowPin;
            connection.Connected += OnConnected;
            connection.Disconnected += OnDisconnected;

            statusItem = new ToolStripMenuItem("UxPlay") { Enabled = false };
            statusItem.Font = new Font(statusItem.Font, FontStyle.Bold);
            startStopItem = new ToolStripMenuItem("Stop", null, (s, e) => ToggleReceiver());
            restartItem = new ToolStripMenuItem("Restart", null, (s, e) => RestartReceiver());
            fullScreenItem = new ToolStripMenuItem("Full screen", null, (s, e) => ToggleFullScreen())
            {
                Checked = settings.FullScreen,
                ShortcutKeyDisplayString = "Alt+Enter",
            };

            onTopItem = new ToolStripMenuItem("Keep on top of other windows", null, (s, e) => ToggleOnTop())
            {
                Checked = settings.KeepOnTop,
            };
            recordingsItem = new ToolStripMenuItem("Open my recordings", null, (s, e) => Open(AppPaths.RecordingsDir))
            {
                Visible = settings.Record,
            };
            screenshotItem = new ToolStripMenuItem("Save a screenshot", null, async (s, e) => await SaveScreenshot())
            {
                Enabled = false,  // until an iPhone is mirroring
            };
            soundTestItem = new ToolStripMenuItem("Play a test sound", null, async (s, e) => await TestSound());

            var menu = new ContextMenuStrip();
            // Alt+Enter in the mirror window changes full screen without telling us,
            // so show the window's real state whenever the menu opens.
            menu.Opening += (s, e) => fullScreenItem.Checked = CurrentFullScreen();
            menu.Items.AddRange(new ToolStripItem[]
            {
                statusItem,
                new ToolStripMenuItem("How to connect my iPhone", null, (s, e) => ShowHowToConnect()),
                new ToolStripSeparator(),
                startStopItem,
                restartItem,
                fullScreenItem,
                onTopItem,
                screenshotItem,
                new ToolStripMenuItem("Settings...", null, (s, e) => ShowSettings()),
                recordingsItem,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Troubleshooting", null,
                    new ToolStripMenuItem("Allow through Windows Firewall", null, (s, e) => FixFirewall()),
                    soundTestItem,
                    new ToolStripMenuItem("Show log", null, (s, e) => ShowLog()),
                    new ToolStripMenuItem("Copy details for a bug report", null, (s, e) => CopyBugReport()),
                    new ToolStripMenuItem("Open UxPlay's data folder", null, (s, e) => Open(AppPaths.DataDir))),
                new ToolStripMenuItem("About UxPlay / check for updates", null, (s, e) => ShowAbout()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Quit", null, (s, e) => Quit()),
            });

            readyIcon = AppIcon.Load(SystemInformation.SmallIconSize);
            connectedIcon = AppIcon.WithDot(readyIcon, Color.FromArgb(0x10, 0xB0, 0x40));
            stoppedIcon = AppIcon.WithDot(readyIcon, Color.FromArgb(0x90, 0x90, 0x90));
            tray = new NotifyIcon
            {
                Icon = readyIcon,
                Text = "UxPlay",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.DoubleClick += (s, e) => ShowHowToConnect();
            // Every balloon sets its own link (or none), so a late "closed" from a
            // replaced balloon can't take the link from the one on show.
            tray.BalloonTipClicked += (s, e) => { if (balloonLink != null) Open(balloonLink); };

            // Only runs while a changed setting waits for mirroring to end.
            pendingRestartTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            pendingRestartTimer.Tick += (s, e) => ApplyPendingRestart();

            // Only runs for a few seconds after a connection, until the mirror window opens.
            onTop = new OnTopWatcher(mirrorWindow);
            onTopTimer = new System.Windows.Forms.Timer { Interval = 500 };
            onTopTimer.Tick += (s, e) => ApplyOnTop();

            // A second launch of UxPlay.exe signals this event instead of starting another copy.
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
                (state, timedOut) => OnUi(ShowHowToConnect), null, Timeout.Infinite, false);

            if (!File.Exists(AppPaths.ServerExe))
            {
                SetStatus("UxPlay files are missing");
                MessageBox.Show("Could not find:\n" + AppPaths.ServerExe +
                    "\n\nPlease reinstall UxPlay or unzip the whole download folder.",
                    "UxPlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (StartReceiver())
                ShowBalloon(8000, "UxPlay is ready",
                    "On your iPhone: open Control Center, tap Screen Mirroring, then choose \"" +
                    settings.DisplayName + "\".", ToolTipIcon.Info);
            _ = WarnIfPublicNetwork();
            _ = CheckForUpdate();
        }

        /// <summary>
        /// On a network Windows calls Public, Windows Firewall keeps iPhones away from
        /// UxPlay, which is right in a café but puzzling at home: say why, and how to fix it.
        /// </summary>
        async System.Threading.Tasks.Task WarnIfPublicNetwork()
        {
            if (networkProfiles == null)
                return;
            var networks = await System.Threading.Tasks.Task.Run(() => networkProfiles.Connected());
            if (!NetworkSafety.OnlyPublic(networks))
                return;
            ShowBalloon(20000, "Your network is set to Public",
                "To keep you safe, Windows Firewall stops iPhones reaching UxPlay on public networks. " +
                "If this is your own Wi-Fi, click here and set it to Private.", ToolTipIcon.Warning, "ms-settings:network-status");
        }

        void ShowBalloon(int timeout, string title, string text, ToolTipIcon icon, string link = null)
        {
            balloonLink = link;
            tray.ShowBalloonTip(timeout, title, text, icon);
        }

        /// <summary>
        /// Once per start, a minute in so it doesn't slow sign-in: tells the user when a
        /// newer release is out. Only for release builds, and only when allowed in Settings.
        /// </summary>
        async System.Threading.Tasks.Task CheckForUpdate()
        {
            string current = ReleaseTag.Current;
            if (current == null || !settings.CheckForUpdates)
                return;
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(1));
            if (!settings.CheckForUpdates)
                return;
            string latest = await updateCheck.NewerReleaseAsync(current);
            if (latest == null)
                return;
            // don't cover the PIN or a connection message while someone is mirroring
            if (pinForm != null || connection.IsConnected)
                return;
            ShowBalloon(15000, "A new version of UxPlay is out",
                latest + " is ready to download (you have " + current + "). Click here to get it.",
                ToolTipIcon.Info, ReleasesUrl + "/tag/" + latest);
        }

        void OnUi(Action action)
        {
            if (ui.IsHandleCreated)
                ui.BeginInvoke(action);
        }

        static string Version => typeof(TrayApp).Assembly.GetName().Version.ToString(3);

        // ---- receiver -----------------------------------------------------------

        bool StartReceiver(bool keepLog = false)
        {
            if (receiver.IsRunning)
                return true;
            // the troubleshooting console gets the same options, without the recording
            SaveArgsForConsole(UxPlayCommandLine.Build(settings, AppPaths.DataDir));
            string args = UxPlayCommandLine.Build(settings, AppPaths.DataDir, NewRecordingName());
            ResetConnection();
            try
            {
                receiver.Start(args, keepLog);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException ||
                                       ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                SetStatus("Stopped");
                MessageBox.Show("Could not start UxPlay:\n" + ex.Message, "UxPlay",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            SetStatus("Ready - waiting for iPhone");
            return true;
        }

        /// <summary>
        /// Where this run of uxplay saves its recordings, when they are on: a name with the
        /// start time, so a restart never overwrites the videos of an earlier run.
        /// </summary>
        string NewRecordingName()
        {
            if (!settings.Record)
                return null;
            try
            {
                Directory.CreateDirectory(AppPaths.RecordingsDir);  // uxplay won't start without it
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowBalloon(8000, "UxPlay can't record", "The folder " + AppPaths.RecordingsDir +
                    " can't be created: " + ex.Message, ToolTipIcon.Warning);
                return null;
            }
            return Path.Combine(AppPaths.RecordingsDir, "UxPlay " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
        }

        /// <summary>uxplay's options, minus the recording's file name, which changes on every start.</summary>
        string OptionsForComparison() => UxPlayCommandLine.Build(settings, AppPaths.DataDir, "recording");

        /// <summary>uxplay-console.bat starts uxplay with these same options.</summary>
        static void SaveArgsForConsole(string args)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllText(AppPaths.ArgsFile, args, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        void StopReceiver()
        {
            receiver.Stop();
            ResetConnection();
            SetStatus("Stopped");
        }

        void RestartReceiver()
        {
            restartPolicy.Reset();
            StopReceiver();
            StartReceiver();
        }

        void ToggleReceiver()
        {
            if (receiver.IsRunning)
                StopReceiver();
            else
            {
                restartPolicy.Reset();
                StartReceiver();
            }
        }

        void OnReceiverExited(int code)
        {
            ResetConnection();
            var decision = restartPolicy.Decide(receiver.Uptime, DateTime.Now);
            if (decision == RestartDecision.Restart)
            {
                // Keep the log of the copy that stopped, for troubleshooting.
                try
                {
                    File.AppendAllText(AppPaths.LogFile,
                        "--- uxplay.exe stopped unexpectedly (exit code " + code + "); restarting ---\r\n");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                StartReceiver(keepLog: true);
                return;
            }
            SetStatus("Stopped (error)");
            string what = decision == RestartDecision.StopsTooOften
                ? "UxPlay keeps stopping unexpectedly (" + RestartPolicy.MaxRestarts + " times in a few minutes, last exit code " + code +
                  "), so it hasn't been restarted again.\n\nRight-click the UxPlay icon and choose Start to try again."
                : "UxPlay stopped unexpectedly (exit code " + code + ").";
            var answer = MessageBox.Show(what + "\n\nOpen the log to see what went wrong?",
                "UxPlay", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
                ShowLog();
        }

        // ---- connection ---------------------------------------------------------

        void OnConnected(string device, bool isNew)
        {
            ClosePin();  // paired: the code is no longer needed
            keepAwake.Set(true);  // don't let the screen turn off while mirroring
            SetStatus("Connected: " + device);
            WatchForMirrorWindow();
            if (isNew)
                ShowBalloon(3000, "UxPlay", device + " is connecting...", ToolTipIcon.None);
        }

        void OnDisconnected()
        {
            keepAwake.Set(false);
            if (receiver.IsRunning)
                SetStatus("Ready - waiting for iPhone");
        }

        /// <summary>Forgets the connection state of a uxplay.exe that has gone.</summary>
        void ResetConnection()
        {
            ClosePin();
            connection.Reset();
            keepAwake.Set(false);
            SetRestartPending(false);
        }

        void ShowPin(string pin)
        {
            if (!receiver.IsRunning)
                return;
            if (pinForm == null || pinForm.IsDisposed)
                pinForm = new PinForm();
            pinForm.ShowPin(pin);
        }

        void ClosePin()
        {
            if (pinForm != null && !pinForm.IsDisposed)
                pinForm.Close();
            pinForm = null;
        }

        // ---- full screen --------------------------------------------------------

        /// <summary>Full screen state of the open mirror window, or the setting when there is none.</summary>
        bool CurrentFullScreen()
        {
            if (receiver.IsRunning && !settings.HasOwnVideoSink)
                return mirrorWindow.IsFullScreen(receiver.ProcessId) ?? settings.FullScreen;
            return settings.FullScreen;
        }

        void ToggleFullScreen()
        {
            settings.FullScreen = !CurrentFullScreen();
            fullScreenItem.Checked = settings.FullScreen;
            SaveSettings();
            if (!receiver.IsRunning)
                return;
            // While mirroring, switch the open window right away so the connection is
            // kept; uxplay picks up the saved setting once nothing is being mirrored.
            if (!settings.HasOwnVideoSink && mirrorWindow.SetFullScreen(receiver.ProcessId, settings.FullScreen))
                SetRestartPending(true);
            else
                RestartReceiver();
        }

        // ---- keep on top --------------------------------------------------------

        void ToggleOnTop()
        {
            settings.KeepOnTop = !settings.KeepOnTop;
            onTopItem.Checked = settings.KeepOnTop;
            SaveSettings();
            if (receiver.IsRunning && !mirrorWindow.SetTopMost(receiver.ProcessId, settings.KeepOnTop) &&
                connection.IsConnected)
                WatchForMirrorWindow();  // connected, but its window isn't open yet
        }

        /// <summary>The mirror window opens a moment after the iPhone connects: wait up to 30 s for it.</summary>
        void WatchForMirrorWindow()
        {
            if (!settings.KeepOnTop)
                return;
            onTop.Start();
            onTopTimer.Start();
        }

        void ApplyOnTop()
        {
            if (!onTop.Tick(settings.KeepOnTop, receiver.IsRunning, receiver.ProcessId))
                onTopTimer.Stop();
        }

        void SetRestartPending(bool pending)
        {
            restartPending = pending;
            pendingRestartTimer.Enabled = pending;
        }

        void ApplyPendingRestart()
        {
            if (restartPending && receiver.IsRunning && !connection.IsConnected &&
                !mirrorWindow.Exists(receiver.ProcessId))
                RestartReceiver();
        }

        // ---- settings -----------------------------------------------------------

        void SaveSettings()
        {
            try
            {
                store.Save(settings);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show("Could not save the settings:\n" + ex.Message, "UxPlay",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void ShowSettings()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            string before = OptionsForComparison();
            using (settingsForm = new SettingsForm(settings, startup))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    SaveSettings();
                    fullScreenItem.Checked = settings.FullScreen;
                    // Restarting disconnects the iPhone, so only do it when something
                    // uxplay uses has changed (or it isn't running).
                    if (!receiver.IsRunning || OptionsForComparison() != before)
                        RestartReceiver();
                    recordingsItem.Visible = settings.Record;
                }
            }
            settingsForm = null;
        }

        // ---- other menu items ---------------------------------------------------

        void SetStatus(string text)
        {
            statusItem.Text = "UxPlay: " + text;
            string tip = "UxPlay - " + text;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;  // NotifyIcon limit
            startStopItem.Text = receiver.IsRunning ? "Stop" : "Start";
            screenshotItem.Enabled = receiver.IsRunning && connection.IsConnected;
            var icon = !receiver.IsRunning ? stoppedIcon : connection.IsConnected ? connectedIcon : readyIcon;
            if (tray.Icon != icon)
                tray.Icon = icon;
            restartItem.Enabled = receiver.IsRunning;
        }

        void ShowHowToConnect()
        {
            MessageBox.Show(
                "To show your iPhone on this PC:\n\n" +
                "1. Connect your iPhone and this PC to the same Wi-Fi network.\n" +
                "2. On the iPhone, swipe down from the top-right corner to open Control Center.\n" +
                "3. Tap Screen Mirroring.\n" +
                "4. Choose \"" + settings.DisplayName + "\".\n\n" +
                "To stop, tap Screen Mirroring again and choose Stop Mirroring.\n\n" +
                "Your iPhone doesn't list this PC? Right-click the UxPlay icon near the clock and choose " +
                "Troubleshooting > Allow through Windows Firewall.",
                "UxPlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void CopyBugReport()
        {
            string report = BugReport.Build(ReleaseTag.Current, Environment.OSVersion.VersionString +
                    (Environment.Is64BitOperatingSystem ? " 64-bit" : ""),
                OptionsForComparison(), BugReport.TailOfFile(AppPaths.LogFile, BugReport.LogLines),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            try
            {
                Clipboard.SetText(report);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show("Another program is using the clipboard. Please try again.", "UxPlay");
                return;
            }
            ShowBalloon(6000, "Copied", "The details are on the clipboard: paste them into your bug report. " +
                "They include this PC's name as shown on the iPhone and the end of the log.", ToolTipIcon.Info);
        }

        void ShowLog()
        {
            if (!File.Exists(AppPaths.LogFile))
            {
                MessageBox.Show("There is no log yet.", "UxPlay");
                return;
            }
            try
            {
                // Copy it so Notepad can open it while uxplay keeps writing.
                string copy = Path.Combine(AppPaths.DataDir, "uxplay-log-copy.txt");  // our own folder, not the shared temp one
                using (var src = new FileStream(AppPaths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var dst = File.Create(copy))
                    src.CopyTo(dst);
                Process.Start("notepad.exe", "\"" + copy + "\"")?.Dispose();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is System.ComponentModel.Win32Exception)
            {
                MessageBox.Show("Could not open the log:\n" + ex.Message + "\n\nIt is saved in:\n" + AppPaths.LogFile,
                    "UxPlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void FixFirewall()
        {
            int? result = firewall.Allow(AppPaths.ServerExe);
            if (result == 0)
                MessageBox.Show("UxPlay is now allowed through Windows Firewall.", "UxPlay",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (result != null)
                MessageBox.Show("Windows Firewall could not be updated (error " + result + ").", "UxPlay",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Helps fix silent mirroring, and switches to compatibility mode when only that is heard.</summary>
        async System.Threading.Tasks.Task TestSound()
        {
            soundTestItem.Enabled = false;
            try
            {
                var outcome = await new SoundTroubleshooter(soundTest, prompt).RunAsync(settings.AudioSink);
                if (outcome != SoundTestOutcome.UseCompatibilityMode)
                    return;
                settings.AudioSink = SoundTroubleshooter.CompatibilitySink;
                SaveSettings();
                // keep a connected iPhone connected; switch once mirroring ends
                if (receiver.IsRunning && connection.IsConnected)
                    SetRestartPending(true);
                else
                    RestartReceiver();
            }
            finally
            {
                soundTestItem.Enabled = true;
            }
        }

        void ShowAbout()
        {
            var answer = MessageBox.Show(
                "UxPlay for Windows, version " + Version + (ReleaseTag.Current != null ? " (" + ReleaseTag.Current + ")" : "") + "\n" +
                "AirPlay receiver based on UxPlay 1.74 (GPL v3).\n\n" +
                "Open the download page to check for a newer version?",
                "About UxPlay", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
                Open(ReleasesUrl);
        }

        async System.Threading.Tasks.Task SaveScreenshot()
        {
            await System.Threading.Tasks.Task.Delay(300);  // let the menu close so it isn't in the picture
            string path = null;
            bool saved;
            try
            {
                Directory.CreateDirectory(AppPaths.ScreenshotsDir);
                path = ScreenshotName.Next(AppPaths.ScreenshotsDir, DateTime.Now, File.Exists);
                saved = receiver.IsRunning && mirrorWindow.SaveScreenshot(receiver.ProcessId, path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is System.Runtime.InteropServices.ExternalException || ex is ArgumentException)
            {
                ShowBalloon(8000, "UxPlay couldn't save the screenshot", ex.Message, ToolTipIcon.Warning);
                return;
            }
            if (saved)
                ShowBalloon(5000, "Screenshot saved", Path.GetFileName(path) + " is in Pictures\\UxPlay. Click to open the folder.",
                    ToolTipIcon.Info, AppPaths.ScreenshotsDir);
            else
                ShowBalloon(8000, "UxPlay couldn't save the screenshot",
                    "The mirror window isn't open yet.", ToolTipIcon.Warning);
        }

        /// <summary>Opens a folder or web page with its default program.</summary>
        static void Open(string target)
        {
            try
            {
                if (Path.IsPathRooted(target))
                    Directory.CreateDirectory(target);  // a folder of ours that may not exist yet
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                MessageBox.Show("Could not open " + target + ":\n" + ex.Message, "UxPlay");
            }
        }

        void Quit()
        {
            StopReceiver();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                receiver.Dispose();
                ClosePin();
                keepAwake.Set(false);
                pendingRestartTimer.Dispose();
                onTopTimer.Dispose();
                showWait.Unregister(null);
                showEvent.Dispose();
                tray.Visible = false;
                tray.Dispose();
                readyIcon.Dispose();
                connectedIcon.Dispose();
                stoppedIcon.Dispose();
                ui.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
