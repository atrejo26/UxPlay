using System;
using System.Drawing;
using System.Windows.Forms;

namespace UxPlayTray
{
    /// <summary>
    /// Shows the 4-digit AirPlay code to type on the iPhone. uxplay.exe only prints it
    /// to its own (hidden) output, so without this window there is nothing to read.
    /// </summary>
    sealed class PinForm : Form
    {
        // An unanswered code stops being useful; the iPhone asks for a new one next time.
        const int CloseAfterMs = 3 * 60 * 1000;

        readonly Label digits;
        readonly Timer closeTimer = new Timer { Interval = CloseAfterMs };

        public PinForm()
        {
            Text = "UxPlay - AirPlay code";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(16);
            Icon = AppIcon.Default;

            var layout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Dock = DockStyle.Fill,
            };
            layout.Controls.Add(new Label
            {
                Text = "Your iPhone is asking for an AirPlay code.\nType this code on the iPhone:",
                AutoSize = true,
            });
            digits = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 44f, FontStyle.Bold),
                Margin = new Padding(3, 12, 3, 12),
                Anchor = AnchorStyles.None,
            };
            layout.Controls.Add(digits);
            layout.Controls.Add(new Label
            {
                Text = "UxPlay remembers your iPhone, so you only need to do this once.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 0, 3, 12),
            });
            var close = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right };
            close.Click += (s, e) => Close();
            CancelButton = close;
            layout.Controls.Add(close);
            Controls.Add(layout);

            closeTimer.Tick += (s, e) => Close();
        }

        /// <summary>Shows (or updates) the window with a new code.</summary>
        public void ShowPin(string pin)
        {
            // Spaced out so each digit is easy to read from across the room.
            digits.Text = string.Join(" ", pin.ToCharArray());
            closeTimer.Stop();
            closeTimer.Start();
            if (!Visible)
                Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void Dispose(bool disposing)
        {
            var font = digits.Font;
            if (disposing)
                closeTimer.Dispose();
            base.Dispose(disposing);
            if (disposing)
                font.Dispose();
        }
    }
}
