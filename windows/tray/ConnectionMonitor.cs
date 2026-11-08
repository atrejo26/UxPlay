using System;
using System.Collections.Generic;

namespace UxPlayTray
{
    /// <summary>
    /// Follows uxplay.exe's messages to tell when an iPhone asks for a PIN, connects and
    /// disconnects. Only reads text, so it works the same in tests; callers decide which
    /// thread to call it on (the tray app uses the UI thread).
    /// </summary>
    sealed class ConnectionMonitor
    {
        readonly HashSet<long> openSockets = new HashSet<long>();

        /// <summary>The iPhone asks for a code: the 4 digits to type on it.</summary>
        public event Action<string> PinRequested;

        /// <summary>
        /// An iPhone paired and started mirroring or playing sound: its name, and whether
        /// nothing was connected before (false when a device reconnects or takes over).
        /// </summary>
        public event Action<string, bool> Connected;

        /// <summary>The last iPhone has gone.</summary>
        public event Action Disconnected;

        public bool IsConnected { get; private set; }

        public void OnLine(string line)
        {
            if (line == null)
                return;
            var m = OutputPatterns.PinRequest.Match(line);
            if (m.Success)
            {
                PinRequested?.Invoke(m.Groups[1].Value);
                return;
            }
            m = OutputPatterns.ConnectRequest.Match(line);
            if (m.Success)
            {
                bool isNew = !IsConnected;
                IsConnected = true;
                Connected?.Invoke(m.Groups[1].Value.Trim(), isNew);
                return;
            }
            m = OutputPatterns.SocketOpened.Match(line);
            if (m.Success)
            {
                if (long.TryParse(m.Groups[1].Value, out long opened))
                    openSockets.Add(opened);
                return;
            }
            m = OutputPatterns.SocketClosed.Match(line);
            if (m.Success)
            {
                if (long.TryParse(m.Groups[1].Value, out long closed))
                    openSockets.Remove(closed);
                if (openSockets.Count == 0 && IsConnected)
                {
                    IsConnected = false;
                    Disconnected?.Invoke();
                }
            }
        }

        /// <summary>Forgets everything, for a new copy of uxplay.exe.</summary>
        public void Reset()
        {
            openSockets.Clear();
            IsConnected = false;
        }
    }
}
