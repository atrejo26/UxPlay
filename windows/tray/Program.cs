using System;
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
            Application.Run(new TrayApp());
            GC.KeepAlive(mutex);
        }
    }
}
