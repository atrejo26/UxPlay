using System.Runtime.InteropServices;

namespace UxPlayTray
{
    /// <summary>
    /// Stops Windows turning the screen off or going to sleep while an iPhone is
    /// mirroring, as video players do. Only while connected, so the PC still sleeps
    /// normally when UxPlay is just waiting.
    /// </summary>
    static class KeepAwake
    {
        const uint ES_CONTINUOUS = 0x80000000;
        const uint ES_SYSTEM_REQUIRED = 0x00000001;
        const uint ES_DISPLAY_REQUIRED = 0x00000002;

        /// <summary>Call from the UI thread: the request belongs to the calling thread.</summary>
        public static void Set(bool awake)
        {
            SetThreadExecutionState(awake
                ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
                : ES_CONTINUOUS);
        }

        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint flags);
    }
}
