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
            // Disposing the app removes the tray icon and stops uxplay.exe on Quit.
            using (var app = new TrayApp())
                Application.Run(app);
            GC.KeepAlive(mutex);
        }

        /// <summary>Saves an unexpected error for troubleshooting and tells the user.</summary>
        static void ReportError(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Settings.DataDir);
                File.AppendAllText(Path.Combine(Settings.DataDir, "tray-error.log"),
                    DateTime.Now + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception) { }
            MessageBox.Show("Something went wrong in UxPlay:\n" + ex?.Message +
                "\n\nDetails were saved in " + Path.Combine(Settings.DataDir, "tray-error.log"),
                "UxPlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
