using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

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

        /// <summary>
        /// The icon with a coloured dot in its lower right corner, for the tray's
        /// status at a glance (green: mirroring, grey: stopped). Dispose it when done.
        /// </summary>
        public static Icon WithDot(Icon icon, Color color)
        {
            using var bitmap = new Bitmap(icon.Width, icon.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawIcon(icon, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                float d = bitmap.Width * 0.5f;
                var dot = new RectangleF(bitmap.Width - d, bitmap.Height - d, d - 0.5f, d - 0.5f);
                using (var fill = new SolidBrush(color))
                    g.FillEllipse(fill, dot);
                using (var ring = new Pen(Color.White, Math.Max(1f, bitmap.Width / 16f)))
                    g.DrawEllipse(ring, dot);
            }
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using var borrowed = Icon.FromHandle(handle);
                return (Icon)borrowed.Clone();  // a copy that owns its own handle
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
    }
}
