using System;
using System.Drawing;
using System.Windows.Forms;

namespace UxPlayTray
{
    /// <summary>The Settings window. Changes are copied into the Settings object only when OK is pressed.</summary>
    sealed class SettingsForm : Form
    {
        readonly Settings settings;
        readonly TextBox nameBox = new TextBox { Width = 260 };
        readonly ComboBox resolutionBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly ComboBox frameRateBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly CheckBox fullScreenBox = new CheckBox { Text = "Start in full screen (no borders) - switch any time with Alt+Enter", AutoSize = true };
        readonly CheckBox audioBox = new CheckBox { Text = "Play iPhone sound on this PC", AutoSize = true };
        readonly CheckBox pinBox = new CheckBox { Text = "Ask for a PIN the first time a new device connects", AutoSize = true };
        readonly CheckBox startupBox = new CheckBox { Text = "Start UxPlay when I sign in to Windows", AutoSize = true };
        readonly TextBox extraBox = new TextBox { Width = 260 };

        public SettingsForm(Settings settings)
        {
            this.settings = settings;

            Text = "UxPlay Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(12);
            Icon = TrayApp.AppIcon;

            foreach (var r in Settings.Resolutions)
                resolutionBox.Items.Add(r.Label);
            foreach (var f in Settings.FrameRates)
                frameRateBox.Items.Add(f.Label);

            nameBox.Text = settings.Name;
            int index = Array.FindIndex(Settings.Resolutions, r => r.Size == settings.Resolution);
            resolutionBox.SelectedIndex = index >= 0 ? index : 0;
            int fpsIndex = Array.FindIndex(Settings.FrameRates, f => f.Fps == settings.Fps);
            frameRateBox.SelectedIndex = fpsIndex >= 0 ? fpsIndex : 0;
            fullScreenBox.Checked = settings.FullScreen;
            audioBox.Checked = settings.Audio;
            pinBox.Checked = settings.RequirePin;
            startupBox.Checked = Settings.StartWithWindows;
            extraBox.Text = settings.ExtraArgs;

            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
            };
            AddRow(grid, "Name shown on iPhone:", nameBox);
            AddRow(grid, "Quality:", resolutionBox);
            AddRow(grid, "Smoothness:", frameRateBox);
            AddRow(grid, null, fullScreenBox);
            AddRow(grid, null, audioBox);
            AddRow(grid, null, pinBox);
            AddRow(grid, null, startupBox);
            AddRow(grid, "Advanced options:", extraBox);
            var hint = new Label
            {
                Text = "Extra uxplay command-line options, for experts. Leave empty normally.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 0, 3, 8),
            };
            AddRow(grid, null, hint);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            ok.Click += (s, e) => Apply();
            AcceptButton = ok;
            CancelButton = cancel;
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 8, 0, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            grid.Controls.Add(buttons, 0, grid.RowCount);
            grid.SetColumnSpan(buttons, 2);

            Controls.Add(grid);
        }

        static void AddRow(TableLayoutPanel grid, string label, Control control)
        {
            int row = grid.RowCount++;
            if (label != null)
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            control.Anchor = AnchorStyles.Left;
            grid.Controls.Add(control, 1, row);
        }

        void Apply()
        {
            settings.Name = Settings.CleanName(nameBox.Text);
            settings.Resolution = Settings.Resolutions[resolutionBox.SelectedIndex].Size;
            settings.Fps = Settings.FrameRates[frameRateBox.SelectedIndex].Fps;
            settings.FullScreen = fullScreenBox.Checked;
            settings.Audio = audioBox.Checked;
            settings.RequirePin = pinBox.Checked;
            settings.ExtraArgs = extraBox.Text.Trim();
            try
            {
                Settings.StartWithWindows = startupBox.Checked;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                MessageBox.Show("Could not change the start-up setting: " + ex.Message, "UxPlay");
            }
        }
    }
}
