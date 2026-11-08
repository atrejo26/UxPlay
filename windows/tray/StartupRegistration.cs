using Microsoft.Win32;

namespace UxPlayTray
{
    /// <summary>Whether UxPlay starts when the user signs in to Windows.</summary>
    interface IStartupRegistration
    {
        /// <summary>Setting it can throw UnauthorizedAccessException or SecurityException.</summary>
        bool Enabled { get; set; }
    }

    /// <summary>The HKCU Run key value (also written by the installer's "start at sign-in" task).</summary>
    sealed class RunKeyStartup : IStartupRegistration
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "UxPlay";

        readonly string exePath;

        public RunKeyStartup(string exePath)
        {
            this.exePath = exePath;
        }

        public bool Enabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) != null;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                    key.SetValue(RunValue, "\"" + exePath + "\"");
                else
                    key.DeleteValue(RunValue, false);
            }
        }
    }
}
