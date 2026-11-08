using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace UxPlayTray
{
    /// <summary>
    /// The video window uxplay.exe opens while mirroring. Its Direct3D output switches
    /// between a window and borderless full screen on Alt+Enter, so this finds the
    /// window and sends it that key press when its state needs to change.
    /// </summary>
    static class MirrorWindow
    {
        public static List<IntPtr> Find(int processId)
        {
            var found = new List<IntPtr>();
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == (uint)processId && IsWindowVisible(hwnd))
                    found.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static bool Exists(int processId) => Find(processId).Count > 0;

        /// <summary>Whether the mirror window is in full screen; null when no window is open.</summary>
        public static bool? IsFullScreen(int processId)
        {
            var windows = Find(processId);
            return windows.Count > 0 ? IsFullScreen(windows[0]) : (bool?)null;
        }

        /// <summary>
        /// Puts every mirror window of the process into (or out of) full screen.
        /// Returns false when there is no window to change.
        /// </summary>
        public static bool SetFullScreen(int processId, bool fullScreen)
        {
            var windows = Find(processId);
            foreach (var hwnd in windows)
            {
                if (IsFullScreen(hwnd) == fullScreen)
                    continue;
                SetForegroundWindow(hwnd);
                Thread.Sleep(50);  // let the window become the keyboard target
                SendAltEnter();
            }
            return windows.Count > 0;
        }

        /// <summary>A window covering its whole monitor is in full screen.</summary>
        static bool IsFullScreen(IntPtr hwnd)
        {
            if (!GetWindowRect(hwnd, out RECT window))
                return false;
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info))
                return false;
            RECT m = info.rcMonitor;
            return window.Left <= m.Left && window.Top <= m.Top &&
                   window.Right >= m.Right && window.Bottom >= m.Bottom;
        }

        static void SendAltEnter()
        {
            var inputs = new[]
            {
                Key(VK_MENU, false),
                Key(VK_RETURN, false),
                Key(VK_RETURN, true),
                Key(VK_MENU, true),
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        static INPUT Key(ushort vk, bool up) => new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
        };

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const ushort VK_RETURN = 0x0D;
        const ushort VK_MENU = 0x12;  // Alt
        const uint MONITOR_DEFAULTTONEAREST = 2;

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}
