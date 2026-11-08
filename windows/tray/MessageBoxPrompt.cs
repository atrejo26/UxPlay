using System.Windows.Forms;

namespace UxPlayTray
{
    /// <summary><see cref="IPrompt"/> with ordinary Windows message boxes.</summary>
    sealed class MessageBoxPrompt : IPrompt
    {
        public bool Ask(string title, string question) =>
            MessageBox.Show(question, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        public void Tell(string title, string message, bool warning = false) =>
            MessageBox.Show(message, title, MessageBoxButtons.OK,
                warning ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }
}
