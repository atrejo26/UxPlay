using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace UxPlayTray
{
    /// <summary>
    /// The video window uxplay.exe opens while mirroring. Its Direct3D output switches
    /// between a window and borderless full screen on Alt+Enter, so this finds the
    /// window and sends it that key press when its state needs to change.
    /// </summary>
    sealed class MirrorWindow : IMirrorWindow
    {
        static List<IntPtr> Find(int processId)
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

        public bool Exists(int processId) => Find(processId).Count > 0;

        /// <summary>Whether the mirror window is in full screen; null when no window is open.</summary>
        public bool? IsFullScreen(int processId)
        {
            var windows = Find(processId);
            return windows.Count > 0 ? IsFullScreen(windows[0]) : (bool?)null;
        }

        /// <summary>
        /// Puts every mirror window of the process into (or out of) full screen.
        /// Returns false when there is no window to change.
        /// </summary>
        public bool SetFullScreen(int processId, bool fullScreen)
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

        public bool SetTopMost(int processId, bool topMost)
        {
            var windows = Find(processId);
            foreach (var hwnd in windows)
                SetWindowPos(hwnd, topMost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
                             SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            return windows.Count > 0;
        }

        public bool SaveScreenshot(int processId, string path)
        {
            var windows = Find(processId);
            if (windows.Count == 0)
                return false;
            IntPtr hwnd = windows[0];
            if (!GetClientRect(hwnd, out RECT client) || client.Right <= 0 || client.Bottom <= 0)
                return false;
            using (var bitmap = new Bitmap(client.Right, client.Bottom, PixelFormat.Format32bppRgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    // The Direct3D picture is drawn by the GPU: PrintWindow can fetch it only
                    // with PW_RENDERFULLCONTENT. If it still comes back black, copy the screen.
                    IntPtr hdc = g.GetHdc();
                    bool printed = PrintWindow(hwnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
                    g.ReleaseHdc(hdc);
                    if (!printed || IsBlack(bitmap))
                    {
                        var origin = new POINT();
                        if (!ClientToScreen(hwnd, ref origin))
                            return false;
                        g.CopyFromScreen(origin.X, origin.Y, 0, 0, bitmap.Size);
                    }
                }
                bitmap.Save(path, ImageFormat.Png);
            }
            return true;
        }

        /// <summary>A quick look at a grid of pixels: all black means nothing was captured.</summary>
        static bool IsBlack(Bitmap bitmap)
        {
            for (int y = 1; y < 8; y++)
                for (int x = 1; x < 8; x++)
                    if ((bitmap.GetPixel(bitmap.Width * x / 8, bitmap.Height * y / 8).ToArgb() & 0xFFFFFF) != 0)
                        return false;
            return true;
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

        const uint PW_CLIENTONLY = 0x1, PW_RENDERFULLCONTENT = 0x2;
        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const ushort VK_RETURN = 0x0D;
        const ushort VK_MENU = 0x12;  // Alt
        const uint MONITOR_DEFAULTTONEAREST = 2;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X, Y;
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

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}
