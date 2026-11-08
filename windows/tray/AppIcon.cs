using System.Drawing;

namespace UxPlayTray
{
    /// <summary>The app icon from the embedded uxplay.ico.</summary>
    static class AppIcon
    {
        /// <summary>Shared by every window (windows don't dispose their icon).</summary>
        public static readonly Icon Default = Load(null);

        public static Icon Load(Size? size)
        {
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("UxPlayTray.uxplay.ico");
            return size.HasValue ? new Icon(stream, size.Value) : new Icon(stream);
        }
    }
}
