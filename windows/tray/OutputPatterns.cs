using System.Text.RegularExpressions;

namespace UxPlayTray
{
    /// <summary>Messages from uxplay.exe that the tray app reacts to.</summary>
    static class OutputPatterns
    {
        /// <summary>An iPhone has paired and is starting to mirror: name (1) and model (2).</summary>
        public static readonly Regex ConnectRequest =
            new Regex(@"connection request from (.+?) \((.*?)\) with deviceID");

        /// <summary>uxplay asks the iPhone for a code (with "Ask for a PIN"): the code (1).</summary>
        public static readonly Regex PinRequest =
            new Regex(@"CLIENT MUST NOW ENTER PIN = ""(\d{4})""");

        /// <summary>A network connection from an iPhone opened or closed: socket number (1).</summary>
        public static readonly Regex SocketOpened =
            new Regex(@"^Accepted IPv[46] client on socket (\d+)");

        public static readonly Regex SocketClosed =
            new Regex(@"^Connection closed on socket (\d+)");
    }
}
