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
        readonly ConnectionMonitor connection = new ConnectionMonitor();

        readonly NotifyIcon tray;
        readonly Control ui;  // marshals receiver events onto the UI thread
        readonly ToolStripMenuItem statusItem, startStopItem, restartItem, fullScreenItem, soundTestItem;
        readonly EventWaitHandle showEvent;
        readonly RegisteredWaitHandle showWait;
        readonly System.Windows.Forms.Timer pendingRestartTimer;

        readonly Settings settings;
        SettingsForm settingsForm;
        PinForm pinForm;
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
                new ToolStripMenuItem("Settings...", null, (s, e) => ShowSettings()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Troubleshooting", null,
                    new ToolStripMenuItem("Allow through Windows Firewall", null, (s, e) => FixFirewall()),
                    soundTestItem,
                    new ToolStripMenuItem("Show log", null, (s, e) => ShowLog()),
                    new ToolStripMenuItem("Open UxPlay's data folder", null, (s, e) => Open(AppPaths.DataDir))),
                new ToolStripMenuItem("About UxPlay / check for updates", null, (s, e) => ShowAbout()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Quit", null, (s, e) => Quit()),
            });

            tray = new NotifyIcon
            {
                Icon = AppIcon.Load(SystemInformation.SmallIconSize),
                Text = "UxPlay",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.DoubleClick += (s, e) => ShowHowToConnect();

            // Only runs while a changed setting waits for mirroring to end.
            pendingRestartTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            pendingRestartTimer.Tick += (s, e) => ApplyPendingRestart();

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
                tray.ShowBalloonTip(8000, "UxPlay is ready",
                    "On your iPhone: open Control Center, tap Screen Mirroring, then choose \"" +
                    settings.DisplayName + "\".", ToolTipIcon.Info);
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
            string args = UxPlayCommandLine.Build(settings, AppPaths.DataDir);
            SaveArgsForConsole(args);
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
            StopReceiver();
            StartReceiver();
        }

        void ToggleReceiver()
        {
            if (receiver.IsRunning)
                StopReceiver();
            else
                StartReceiver();
        }

        void OnReceiverExited(int code)
        {
            ResetConnection();
            // Unexpected exit: restart automatically if it had been running a while,
            // otherwise something is wrong with the setup, so tell the user.
            if (receiver.Uptime.TotalSeconds > 20)
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
            var answer = MessageBox.Show(
                "UxPlay stopped unexpectedly (exit code " + code + ").\n\nOpen the log to see what went wrong?",
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
            if (isNew)
                tray.ShowBalloonTip(3000, "UxPlay", device + " is connecting...", ToolTipIcon.None);
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
            string before = UxPlayCommandLine.Build(settings, AppPaths.DataDir);
            using (settingsForm = new SettingsForm(settings, startup))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    SaveSettings();
                    fullScreenItem.Checked = settings.FullScreen;
                    // Restarting disconnects the iPhone, so only do it when something
                    // uxplay uses has changed (or it isn't running).
                    if (!receiver.IsRunning || UxPlayCommandLine.Build(settings, AppPaths.DataDir) != before)
                        RestartReceiver();
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
                string copy = Path.Combine(Path.GetTempPath(), "uxplay-log.txt");
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

        /// <summary>
        /// Plays a beep through the receiver's sound output and helps fix silent mirroring:
        /// a beep that isn't heard points at the PC's sound, one that is points at the network.
        /// </summary>
        async System.Threading.Tasks.Task TestSound()
        {
            soundTestItem.Enabled = false;
            try
            {
                string sink = settings.AudioSink;
                var result = await soundTest.PlayAsync(sink);
                if (result == SoundTestResult.Unavailable)
                {
                    MessageBox.Show("This copy of UxPlay can't play a test sound. Please install the latest version.",
                        "UxPlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                bool heard = result == SoundTestResult.Played && MessageBox.Show(
                    "UxPlay just played a short beep through \"" + AudioOutputLabel(sink) + "\".\n\nDid you hear it?",
                    "UxPlay sound test", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                if (heard)
                {
                    MessageBox.Show(
                        "Sound on this PC works.\n\nIf mirroring is still silent:\n" +
                        "- Turn up the volume on your iPhone while mirroring (it controls UxPlay's volume).\n" +
                        "- Check that Settings > Play iPhone sound on this PC is ticked.\n" +
                        "- Sound comes over the network separately from the picture. If you use a firewall " +
                        "or security suite other than Windows Firewall, allow uxplay.exe there for UDP too.",
                        "UxPlay sound test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (sink != "directsoundsink")
                {
                    // Try the output that works with the most sound drivers.
                    var retry = await soundTest.PlayAsync("directsoundsink");
                    if (retry == SoundTestResult.Played && MessageBox.Show(
                            "UxPlay played the beep again, this time in compatibility mode.\n\nDid you hear it now?",
                            "UxPlay sound test", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        settings.AudioSink = "directsoundsink";
                        SaveSettings();
                        // keep a connected iPhone connected; switch once mirroring ends
                        if (receiver.IsRunning && connection.IsConnected)
                            SetRestartPending(true);
                        else
                            RestartReceiver();
                        MessageBox.Show("UxPlay now uses compatibility mode for sound. You can change this in Settings > Sound output.",
                            "UxPlay sound test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
                MessageBox.Show(
                    "The beep couldn't be heard, so the problem is with this PC's sound, not with your iPhone.\n\n" +
                    "- Check that the PC isn't muted and that the right speakers or headphones are chosen " +
                    "(click the speaker icon near the clock).\n" +
                    "- Make sure the volume mixer doesn't have UxPlay or gst-launch muted.\n" +
                    "- Then try the sound test again.",
                    "UxPlay sound test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                soundTestItem.Enabled = true;
            }
        }

        static string AudioOutputLabel(string sink)
        {
            foreach (var (label, value) in Settings.AudioOutputs)
                if (value == sink)
                    return label;
            return sink;
        }

        void ShowAbout()
        {
            var answer = MessageBox.Show(
                "UxPlay for Windows, version " + Version + "\n" +
                "AirPlay receiver based on UxPlay 1.74 (GPL v3).\n\n" +
                "Open the download page to check for a newer version?",
                "About UxPlay", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
                Open(ReleasesUrl);
        }

        /// <summary>Opens a folder or web page with its default program.</summary>
        static void Open(string target)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException)
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
                showWait.Unregister(null);
                showEvent.Dispose();
                tray.Visible = false;
                tray.Icon?.Dispose();
                tray.Dispose();
                ui.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
