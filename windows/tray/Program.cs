using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace UxPlayTray
{
    static class Program
    {
        const string MutexName = @"Local\UxPlayTray.SingleInstance";
        internal const string ShowEventName = @"Local\UxPlayTray.ShowHelp";

        [STAThread]
        static void Main()
        {
            using var mutex = new Mutex(true, MutexName, out bool firstInstance);
            if (!firstInstance)
            {
                // Already running: ask the running copy to pop up its connect instructions.
                try
                {
                    using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                    ev.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportError(e.ExceptionObject as Exception);
            // The one place that picks the concrete parts the tray app works with.
            var receiver = new ReceiverProcess(AppPaths.ServerExe, AppPaths.ServerDir, AppPaths.DataDir, AppPaths.LogFile);
            receiver.KillLeftovers();
            var services = new TrayServices
            {
                Receiver = receiver,
                SettingsStore = new IniSettingsStore(AppPaths.SettingsFile),
                Startup = new RunKeyStartup(Application.ExecutablePath),
                MirrorWindow = new MirrorWindow(),
                KeepAwake = new KeepAwake(),
                Firewall = new WindowsFirewall(),
                SoundTest = new GStreamerSoundTest(AppPaths.ServerDir, AppPaths.DataDir),
                UpdateCheck = new GitHubUpdateCheck("atrejo26/UxPlay"),
            };
            // Disposing the app removes the tray icon and stops uxplay.exe on Quit.
            using (var app = new TrayApp(services))
                Application.Run(app);
            GC.KeepAlive(mutex);
        }

        /// <summary>Saves an unexpected error for troubleshooting and tells the user.</summary>
        static void ReportError(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.AppendAllText(AppPaths.ErrorLog,
                    DateTime.Now + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception) { }
            MessageBox.Show("Something went wrong in UxPlay:\n" + ex?.Message +
                "\n\nDetails were saved in " + AppPaths.ErrorLog,
                "UxPlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
