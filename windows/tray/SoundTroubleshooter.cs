using System.Threading.Tasks;

namespace UxPlayTray
{
    /// <summary>Asks the user something or tells them something. Message boxes in the app, a script in tests.</summary>
    interface IPrompt
    {
        /// <summary>A yes/no question; true for yes.</summary>
        bool Ask(string title, string question);

        void Tell(string title, string message, bool warning = false);
    }

    /// <summary>What the sound test found out.</summary>
    enum SoundTestOutcome
    {
        /// <summary>This copy of UxPlay has no test-sound player.</summary>
        Unavailable,
        /// <summary>The beep was heard: the PC's sound works, so the network or the iPhone is to blame.</summary>
        PcSoundWorks,
        /// <summary>Only compatibility mode was heard: the receiver should switch to it.</summary>
        UseCompatibilityMode,
        /// <summary>No beep was heard at all: the PC's own sound needs fixing.</summary>
        PcSoundProblem,
    }

    /// <summary>
    /// Helps fix silent mirroring: plays a beep through the receiver's sound output and,
    /// if it isn't heard, again in compatibility mode. Says what to do next; changing the
    /// setting is left to the caller.
    /// </summary>
    sealed class SoundTroubleshooter
    {
        public const string CompatibilitySink = "directsoundsink";
        const string Title = "UxPlay sound test";

        readonly ISoundTest soundTest;
        readonly IPrompt prompt;

        public SoundTroubleshooter(ISoundTest soundTest, IPrompt prompt)
        {
            this.soundTest = soundTest;
            this.prompt = prompt;
        }

        public async Task<SoundTestOutcome> RunAsync(string sink)
        {
            var result = await soundTest.PlayAsync(sink);
            if (result == SoundTestResult.Unavailable)
            {
                prompt.Tell("UxPlay", "This copy of UxPlay can't play a test sound. Please install the latest version.");
                return SoundTestOutcome.Unavailable;
            }
            if (result == SoundTestResult.Played && prompt.Ask(Title,
                    "UxPlay just played a short beep through \"" + Settings.AudioOutputLabel(sink) + "\".\n\nDid you hear it?"))
            {
                prompt.Tell(Title,
                    "Sound on this PC works.\n\nIf mirroring is still silent:\n" +
                    "- Turn up the volume on your iPhone while mirroring (it controls UxPlay's volume).\n" +
                    "- Check that Settings > Play iPhone sound on this PC is ticked.\n" +
                    "- Sound comes over the network separately from the picture. If you use a firewall " +
                    "or security suite other than Windows Firewall, allow uxplay.exe there for UDP too.");
                return SoundTestOutcome.PcSoundWorks;
            }
            if (sink != CompatibilitySink &&
                await soundTest.PlayAsync(CompatibilitySink) == SoundTestResult.Played &&
                prompt.Ask(Title, "UxPlay played the beep again, this time in compatibility mode.\n\nDid you hear it now?"))
            {
                prompt.Tell(Title, "UxPlay now uses compatibility mode for sound. You can change this in Settings > Sound output.");
                return SoundTestOutcome.UseCompatibilityMode;
            }
            prompt.Tell(Title,
                "The beep couldn't be heard, so the problem is with this PC's sound, not with your iPhone.\n\n" +
                "- Check that the PC isn't muted and that the right speakers or headphones are chosen " +
                "(click the speaker icon near the clock).\n" +
                "- Make sure the volume mixer doesn't have UxPlay or gst-launch muted.\n" +
                "- Then try the sound test again.", warning: true);
            return SoundTestOutcome.PcSoundProblem;
        }
    }
}
