using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UxPlayTray;
using Xunit;

static class SettingsExtensions
{
    public static string Build(this Settings s) => UxPlayCommandLine.Build(s, @"C:\Data");
}

public class CommandLineTests
{
    static Settings Defaults() => new Settings { Name = "Living Room PC" };

    [Fact]
    public void KeyAndRegisterLiveInTheDataFolder()
    {
        var s = Defaults();
        s.RequirePin = true;
        string args = s.Build();
        Assert.Contains("-key \"C:\\Data", args);
        Assert.Contains("-reg \"C:\\Data", args);
    }

    [Fact]
    public void DefaultsUseDirect3DWithToggleAnd60Fps()
    {
        string args = Defaults().Build();
        Assert.StartsWith("-n \"Living Room PC\" -nh -key ", args);
        Assert.Contains("-nohold", args);
        Assert.Contains("-vs \"d3d11videosink fullscreen-toggle-mode=alt-enter+property\"", args);
        Assert.Contains("-fps 60", args);
        Assert.DoesNotContain("fullscreen=true", args);
        Assert.DoesNotContain("-pin", args);
        Assert.DoesNotContain("-as", args);
        Assert.DoesNotContain("-s ", args);
    }

    [Fact]
    public void FullScreenStartsInFullScreen()
    {
        var s = Defaults();
        s.FullScreen = true;
        Assert.Contains("alt-enter+property fullscreen=true\"", s.Build());
    }

    [Fact]
    public void OwnVideoSinkIsLeftAlone()
    {
        var s = Defaults();
        s.FullScreen = true;
        s.ExtraArgs = "-vs d3d12videosink";
        string args = s.Build();
        Assert.DoesNotContain("d3d11videosink", args);
        Assert.Contains(" -fs ", args);
        Assert.EndsWith("-vs d3d12videosink", args);
    }

    [Fact]
    public void PinUsesRandomCodeAndRegister()
    {
        var s = Defaults();
        s.RequirePin = true;
        string args = s.Build();
        Assert.Contains("-pin -reg \"", args);
        Assert.Contains("uxplay.register\"", args);
    }

    [Fact]
    public void NoSoundTurnsAudioOff()
    {
        var s = Defaults();
        s.Audio = false;
        s.AudioSink = "wasapisink";
        string args = s.Build();
        Assert.Contains("-as 0", args);
        Assert.DoesNotContain("wasapisink", args);
    }

    [Fact]
    public void SoundOutputIsPassed()
    {
        var s = Defaults();
        s.AudioSink = "directsoundsink";
        Assert.Contains("-as directsoundsink", s.Build());
        s.ExtraArgs = "-as wasapi2sink";
        Assert.DoesNotContain("directsoundsink", s.Build());
    }

    [Fact]
    public void QualityAndSmoothness()
    {
        var s = Defaults();
        s.Resolution = "1280x720";
        s.Fps = 30;
        string args = s.Build();
        Assert.Contains("-s 1280x720", args);
        Assert.Contains("-fps 30", args);
    }

    [Theory]
    [InlineData("My \"PC\"", "My PC")]
    [InlineData("Back\\slash", "Backslash")]
    [InlineData("  Tabs\tand\nnewlines  ", "Tabsandnewlines")]
    public void NamesAreCleaned(string name, string expected)
    {
        Assert.Equal(expected, Settings.CleanName(name));
    }

    [Fact]
    public void EmptyNameFallsBackToComputerName()
    {
        Assert.Equal(System.Environment.MachineName, Settings.CleanName(" \" "));
    }
}

public class OutputPatternTests
{
    [Fact]
    public void PinRequest()
    {
        var m = OutputPatterns.PinRequest.Match("*** CLIENT MUST NOW ENTER PIN = \"0427\" AS AIRPLAY PASSWORD");
        Assert.True(m.Success);
        Assert.Equal("0427", m.Groups[1].Value);
    }

    [Fact]
    public void ConnectRequest()
    {
        var m = OutputPatterns.ConnectRequest.Match(
            "connection request from Alex's iPhone (iPhone17,3) with deviceID = 11:22:33:44:55:66");
        Assert.True(m.Success);
        Assert.Equal("Alex's iPhone", m.Groups[1].Value);
        Assert.Equal("iPhone17,3", m.Groups[2].Value);
    }

    [Fact]
    public void Sockets()
    {
        Assert.Equal("12", OutputPatterns.SocketOpened.Match("Accepted IPv4 client on socket 12, port 7000").Groups[1].Value);
        Assert.Equal("640", OutputPatterns.SocketOpened.Match("Accepted IPv6 client on socket 640, port 7000").Groups[1].Value);
        Assert.Equal("12", OutputPatterns.SocketClosed.Match("Connection closed on socket 12").Groups[1].Value);
        Assert.False(OutputPatterns.SocketClosed.Match("httpd error in CLOSESOCKET: 9").Success);
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void SaveAndLoadKeepEverything()
    {
        var s = new Settings
        {
            Name = "Den", FullScreen = true, Resolution = "1280x720", Fps = 30,
            Audio = false, AudioSink = "directsoundsink", RequirePin = true, ExtraArgs = "-d",
        };
        var back = IniSettingsStore.Parse(IniSettingsStore.Format(s));
        Assert.Equal(UxPlayCommandLine.Build(s, "x"), UxPlayCommandLine.Build(back, "x"));
        Assert.Equal("directsoundsink", back.AudioSink);
    }

    [Fact]
    public void BadValuesFallBackToDefaults()
    {
        var s = IniSettingsStore.Parse(new[] { "fps=500", "audiosink=evilsink", "garbage", "=x", "name=" });
        Assert.Equal(60, s.Fps);
        Assert.Equal("", s.AudioSink);
        Assert.Equal(System.Environment.MachineName, s.Name);
    }

    [Fact]
    public void OldFullScreenWorkaroundIsDropped()
    {
        Assert.Equal("", IniSettingsStore.Parse(new[] { "extra=-vs d3d11videosink" }).ExtraArgs);
        Assert.Equal("-vs d3d12videosink", IniSettingsStore.Parse(new[] { "extra=-vs d3d12videosink" }).ExtraArgs);
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var store = new IniSettingsStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid() + ".ini"));
        Assert.Equal(60, store.Load().Fps);
    }

    [Fact]
    public void SaveWritesAFileLoadReads()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid() + "/settings.ini");
        var store = new IniSettingsStore(path);
        store.Save(new Settings { Name = "Office", Fps = 30 });
        var s = store.Load();
        Assert.Equal("Office", s.Name);
        Assert.Equal(30, s.Fps);
        System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(path), true);
    }
}

public class ConnectionMonitorTests
{
    [Fact]
    public void PairConnectAndDisconnect()
    {
        var m = new ConnectionMonitor();
        string pin = null, device = null;
        bool? isNew = null;
        int disconnects = 0;
        m.PinRequested += p => pin = p;
        m.Connected += (d, n) => { device = d; isNew = n; };
        m.Disconnected += () => disconnects++;

        m.OnLine("Accepted IPv4 client on socket 7, port 7000");
        m.OnLine("*** CLIENT MUST NOW ENTER PIN = \"1234\" AS AIRPLAY PASSWORD");
        Assert.Equal("1234", pin);
        Assert.False(m.IsConnected);

        m.OnLine("connection request from Sam's iPhone (iPhone17,1) with deviceID = 1");
        Assert.Equal("Sam's iPhone", device);
        Assert.True(isNew);
        Assert.True(m.IsConnected);

        m.OnLine("Accepted IPv4 client on socket 9, port 7000");
        m.OnLine("Connection closed on socket 7");
        Assert.Equal(0, disconnects);  // socket 9 is still open
        m.OnLine("Connection closed on socket 9");
        Assert.Equal(1, disconnects);
        Assert.False(m.IsConnected);
    }

    [Fact]
    public void TakeoverIsNotNew()
    {
        var m = new ConnectionMonitor();
        bool? isNew = null;
        m.Connected += (d, n) => isNew = n;
        m.OnLine("connection request from A (iPhone) with deviceID = 1");
        m.OnLine("connection request from B (iPad) with deviceID = 2");
        Assert.False(isNew);
    }

    [Fact]
    public void ClosingWithoutAConnectionSaysNothing()
    {
        var m = new ConnectionMonitor();
        int disconnects = 0;
        m.Disconnected += () => disconnects++;
        m.OnLine("Accepted IPv4 client on socket 3, port 7000");
        m.OnLine("Connection closed on socket 3");
        m.OnLine(null);
        Assert.Equal(0, disconnects);
    }

    [Fact]
    public void ResetForgetsTheConnection()
    {
        var m = new ConnectionMonitor();
        m.OnLine("Accepted IPv4 client on socket 3, port 7000");
        m.OnLine("connection request from A (iPhone) with deviceID = 1");
        m.Reset();
        Assert.False(m.IsConnected);
    }
}

public class SoundTestTests
{
    [Fact]
    public void AutomaticOutputUsesAutoaudiosink()
    {
        string args = GStreamerSoundTest.Arguments("");
        Assert.StartsWith("-q audiotestsrc num-buffers=", args);
        Assert.EndsWith("! autoaudiosink", args);
    }

    [Fact]
    public void ChosenOutputIsUsed()
    {
        foreach (var (_, sink) in Settings.AudioOutputs)
            if (sink.Length > 0)
                Assert.EndsWith("! " + sink, GStreamerSoundTest.Arguments(sink));
    }

    [Fact]
    public async Task MissingLauncherIsReportedNotThrown()
    {
        var test = new GStreamerSoundTest(Path.Combine(Path.GetTempPath(), "no-such-uxplay"), Path.GetTempPath());
        Assert.Equal(SoundTestResult.Unavailable, await test.PlayAsync(""));
    }
}

public class ReleaseTagTests
{
    [Theory]
    [InlineData("v1.74-win3", "v1.74-win2", true)]
    [InlineData("v1.74-win10", "v1.74-win9", true)]
    [InlineData("v1.75-win1", "v1.74-win9", true)]
    [InlineData("v1.74-win3", "v1.74-win3-beta", true)]   // the finished release of this beta
    [InlineData("v1.74-win2", "v1.74-win2", false)]
    [InlineData("v1.74-win2", "v1.74-win3-beta", false)]  // a beta is newer than the last release
    [InlineData("v1.74-win3-beta", "v1.74-win3", false)]
    [InlineData("v1.73-win9", "v1.74-win1", false)]
    [InlineData("nightly", "v1.74-win2", false)]
    [InlineData("v1.74-win3", null, false)]
    public void ComparesReleases(string latest, string current, bool newer)
    {
        Assert.Equal(newer, ReleaseTag.IsNewer(latest, current));
    }

    [Fact]
    public void ReadsTagFromGitHubJson()
    {
        Assert.Equal("v1.74-win2", GitHubUpdateCheck.ParseTag("{\"url\":\"x\",\"tag_name\": \"v1.74-win2\",\"name\":\"y\"}"));
        Assert.Null(GitHubUpdateCheck.ParseTag("{\"message\":\"API rate limit exceeded\"}"));
        Assert.Null(GitHubUpdateCheck.ParseTag(null));
    }

    [Fact]
    public void UpdatesSettingIsSaved()
    {
        var s = new Settings { CheckForUpdates = false };
        Assert.False(IniSettingsStore.Parse(IniSettingsStore.Format(s)).CheckForUpdates);
        Assert.True(IniSettingsStore.Parse(new string[0]).CheckForUpdates);
    }

    [Fact]
    public void KeepOnTopIsSavedAndOffByDefault()
    {
        Assert.False(IniSettingsStore.Parse(new string[0]).KeepOnTop);
        Assert.True(IniSettingsStore.Parse(IniSettingsStore.Format(new Settings { KeepOnTop = true })).KeepOnTop);
    }

    [Fact]
    public void KeepOnTopDoesNotChangeTheCommandLine()
    {
        // switching it must never restart uxplay (and disconnect the iPhone)
        var s = new Settings();
        string before = UxPlayCommandLine.Build(s, @"C:\data");
        s.KeepOnTop = true;
        s.CheckForUpdates = false;
        Assert.Equal(before, UxPlayCommandLine.Build(s, @"C:\data"));
    }
}

public class RecordingTests
{
    [Fact]
    public void RecordingIsAddedOnlyWhenOnAndNamed()
    {
        var s = new Settings();
        Assert.DoesNotContain("-mp4", UxPlayCommandLine.Build(s, @"C:\data", @"C:\Videos\UxPlay\UxPlay 1"));
        s.Record = true;
        Assert.DoesNotContain("-mp4", UxPlayCommandLine.Build(s, @"C:\data"));  // e.g. the console's options
        Assert.Contains("-mp4 \"C:\\Videos\\UxPlay\\UxPlay 1\"", UxPlayCommandLine.Build(s, @"C:\data", @"C:\Videos\UxPlay\UxPlay 1"));
    }

    [Fact]
    public void OwnMp4OptionWins()
    {
        var s = new Settings { Record = true, ExtraArgs = "-mp4 D:\\mine" };
        string args = UxPlayCommandLine.Build(s, @"C:\data", @"C:\Videos\x");
        Assert.DoesNotContain("C:\\Videos\\x", args);
        Assert.EndsWith("-mp4 D:\\mine", args);
    }

    [Fact]
    public void RecordSettingIsSaved()
    {
        Assert.False(IniSettingsStore.Parse(new string[0]).Record);
        Assert.True(IniSettingsStore.Parse(IniSettingsStore.Format(new Settings { Record = true })).Record);
    }
}

public class LowLatencyTests
{
    [Fact]
    public void LowLatencySwitchesOffTimestampSync()
    {
        var s = new Settings();
        Assert.DoesNotContain("-vsync", s.Build());
        s.LowLatency = true;
        Assert.Contains("-vsync no", s.Build());
        Assert.True(IniSettingsStore.Parse(IniSettingsStore.Format(s)).LowLatency);
    }

    [Fact]
    public void OwnVsyncOptionWins()
    {
        var s = new Settings { LowLatency = true, ExtraArgs = "-vsync 0.2" };
        Assert.DoesNotContain("-vsync no", s.Build());
    }
}

public class YouTubeVideoTests
{
    [Fact]
    public void YouTubeVideoAddsHls()
    {
        var s = new Settings();
        Assert.DoesNotContain("-hls", s.Build());
        s.YouTubeVideo = true;
        Assert.Contains(" -hls", s.Build());
        Assert.True(IniSettingsStore.Parse(IniSettingsStore.Format(s)).YouTubeVideo);
    }

    [Fact]
    public void OwnHlsOptionWins()
    {
        var s = new Settings { YouTubeVideo = true, ExtraArgs = "-hls 2" };
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(s.Build(), "-hls"));
    }
}

public class SoundTroubleshooterTests
{
    /// <summary>Plays what it is told to and records which outputs were tried.</summary>
    sealed class FakeSoundTest : ISoundTest
    {
        readonly SoundTestResult result;
        public readonly List<string> Played = new List<string>();
        public FakeSoundTest(SoundTestResult result) { this.result = result; }
        public Task<SoundTestResult> PlayAsync(string sink) { Played.Add(sink); return Task.FromResult(result); }
    }

    /// <summary>Answers the yes/no questions in order and records what was said.</summary>
    sealed class ScriptedPrompt : IPrompt
    {
        readonly Queue<bool> answers;
        public readonly List<string> Told = new List<string>();
        public bool LastWasWarning;
        public ScriptedPrompt(params bool[] answers) { this.answers = new Queue<bool>(answers); }
        public bool Ask(string title, string question) => answers.Dequeue();
        public void Tell(string title, string message, bool warning = false) { Told.Add(message); LastWasWarning = warning; }
    }

    [Fact]
    public async Task HeardBeepPointsAtTheNetwork()
    {
        var test = new FakeSoundTest(SoundTestResult.Played);
        var prompt = new ScriptedPrompt(true);
        Assert.Equal(SoundTestOutcome.PcSoundWorks, await new SoundTroubleshooter(test, prompt).RunAsync(""));
        Assert.Equal(new[] { "" }, test.Played);
        Assert.Contains("UDP", prompt.Told[0]);
    }

    [Fact]
    public async Task OnlyCompatibilityModeHeardSwitchesToIt()
    {
        var test = new FakeSoundTest(SoundTestResult.Played);
        var prompt = new ScriptedPrompt(false, true);
        Assert.Equal(SoundTestOutcome.UseCompatibilityMode, await new SoundTroubleshooter(test, prompt).RunAsync("wasapisink"));
        Assert.Equal(new[] { "wasapisink", SoundTroubleshooter.CompatibilitySink }, test.Played);
    }

    [Fact]
    public async Task NothingHeardPointsAtThePcSound()
    {
        var test = new FakeSoundTest(SoundTestResult.Played);
        var prompt = new ScriptedPrompt(false, false);
        Assert.Equal(SoundTestOutcome.PcSoundProblem, await new SoundTroubleshooter(test, prompt).RunAsync(""));
        Assert.True(prompt.LastWasWarning);
    }

    [Fact]
    public async Task CompatibilityModeIsNotTriedTwice()
    {
        var test = new FakeSoundTest(SoundTestResult.Played);
        var prompt = new ScriptedPrompt(false);
        Assert.Equal(SoundTestOutcome.PcSoundProblem,
            await new SoundTroubleshooter(test, prompt).RunAsync(SoundTroubleshooter.CompatibilitySink));
        Assert.Single(test.Played);
    }

    [Fact]
    public async Task FailedPlayerSkipsTheQuestions()
    {
        var test = new FakeSoundTest(SoundTestResult.Failed);
        var prompt = new ScriptedPrompt();  // any question would throw
        Assert.Equal(SoundTestOutcome.PcSoundProblem, await new SoundTroubleshooter(test, prompt).RunAsync(""));
        Assert.Equal(2, test.Played.Count);
    }

    [Fact]
    public async Task MissingPlayerIsReported()
    {
        var prompt = new ScriptedPrompt();
        Assert.Equal(SoundTestOutcome.Unavailable,
            await new SoundTroubleshooter(new FakeSoundTest(SoundTestResult.Unavailable), prompt).RunAsync(""));
        Assert.Single(prompt.Told);
    }

    [Fact]
    public void OutputLabels()
    {
        Assert.Equal("Automatic (recommended)", Settings.AudioOutputLabel(""));
        Assert.Equal("pulsesink", Settings.AudioOutputLabel("pulsesink"));
    }
}

public class NewerReleaseTests
{
    sealed class FixedUpdateCheck : IUpdateCheck
    {
        readonly string tag;
        public FixedUpdateCheck(string tag) { this.tag = tag; }
        public Task<string> LatestReleaseTagAsync() => Task.FromResult(tag);
    }

    [Theory]
    [InlineData("v1.74-win3", "v1.74-win2", "v1.74-win3")]
    [InlineData("v1.74-win2", "v1.74-win2", null)]
    [InlineData("v1.74-win1", "v1.74-win2", null)]
    [InlineData(null, "v1.74-win2", null)]
    [InlineData("v1.74-win3", null, null)]
    public async Task OnlyANewerReleaseIsOffered(string latest, string current, string expected)
    {
        Assert.Equal(expected, await new FixedUpdateCheck(latest).NewerReleaseAsync(current));
    }
}

public class GStreamerEnvironmentTests
{
    [Fact]
    public void UsesOnlyTheBundledGStreamer()
    {
        var psi = new System.Diagnostics.ProcessStartInfo("x");
        psi.EnvironmentVariables["GST_PLUGIN_PATH"] = "C:\\other";
        psi.EnvironmentVariables["GST_PLUGIN_PATH_1_0"] = "C:\\other";
        GStreamerEnvironment.Apply(psi, "S", "D");
        var env = psi.EnvironmentVariables;
        Assert.StartsWith(Path.Combine("S", "bin") + ";", env["PATH"]);
        Assert.Equal(Path.Combine("S", "lib", "gstreamer-1.0"), env["GST_PLUGIN_SYSTEM_PATH_1_0"]);
        Assert.Equal(Path.Combine("D", "gst-registry.bin"), env["GST_REGISTRY_1_0"]);
        Assert.Equal(Path.Combine("S", "lib", "gio", "modules"), env["GIO_MODULE_DIR"]);
        Assert.Equal("D", env["HOME"]);
        Assert.False(env.ContainsKey("GST_PLUGIN_PATH"));
        Assert.False(env.ContainsKey("GST_PLUGIN_PATH_1_0"));
    }
}

public class UpdateCheckSetupTests
{
    [Fact]
    public void AsksForTheLatestRelease() =>
        Assert.Equal("https://api.github.com/repos/atrejo26/UxPlay/releases/latest",
            new GitHubUpdateCheck("atrejo26/UxPlay").ApiUrl);

    [Fact]
    public void TestBuildIsNotARelease() => Assert.Null(ReleaseTag.Current);
}

/// <summary>A mirror window that opens after a given number of tries.</summary>
sealed class FakeMirrorWindow : IMirrorWindow
{
    public int OpensAfter;
    public int Tries;
    public bool? TopMost;
    public bool Exists(int processId) => Tries >= OpensAfter;
    public bool? IsFullScreen(int processId) => null;
    public bool SetFullScreen(int processId, bool fullScreen) => false;
    public bool SaveScreenshot(int processId, string path) => false;
    public bool SetTopMost(int processId, bool topMost)
    {
        if (++Tries <= OpensAfter)
            return false;
        TopMost = topMost;
        return true;
    }
}

public class OnTopWatcherTests
{
    [Fact]
    public void SetsTheWindowOnceItOpens()
    {
        var window = new FakeMirrorWindow { OpensAfter = 3 };
        var watcher = new OnTopWatcher(window);
        watcher.Start();
        int ticks = 0;
        while (watcher.Tick(true, true, 42))
            ticks++;
        Assert.Equal(3, ticks);
        Assert.True(window.TopMost);
    }

    [Fact]
    public void DoesNothingUntilStarted()
    {
        var window = new FakeMirrorWindow();
        Assert.False(new OnTopWatcher(window).Tick(true, true, 42));
        Assert.Equal(0, window.Tries);
    }
}

public class RestartPolicyTests
{
    static readonly System.DateTime T0 = new System.DateTime(2026, 1, 1, 20, 0, 0);

    [Fact]
    public void ACrashAfterRunningAWhileIsRestarted() =>
        Assert.Equal(RestartDecision.Restart, new RestartPolicy().Decide(System.TimeSpan.FromHours(2), T0));

    [Fact]
    public void ACrashRightAtStartIsReported() =>
        Assert.Equal(RestartDecision.StoppedAtStart, new RestartPolicy().Decide(System.TimeSpan.FromSeconds(3), T0));
}

public class LogFileTests
{
    static string TempLog() => Path.Combine(Path.GetTempPath(), "uxplay-test-" + System.Guid.NewGuid() + ".log");

    [Fact]
    public void WritesTheHeaderAndLines()
    {
        string path = TempLog();
        using (var log = new LogFile(path, "> uxplay.exe -n PC", append: false))
        {
            log.WriteLine("Initialized server socket(s)");
        }
        Assert.Equal(new[] { "> uxplay.exe -n PC", "Initialized server socket(s)" }, File.ReadAllLines(path));
        File.Delete(path);
    }

    [Fact]
    public void AppendKeepsTheEarlierRun()
    {
        string path = TempLog();
        using (var log = new LogFile(path, "run 1", append: false)) log.WriteLine("a");
        using (var log = new LogFile(path, "run 2", append: true)) log.WriteLine("b");
        Assert.Equal(new[] { "run 1", "a", "run 2", "b" }, File.ReadAllLines(path));
        File.Delete(path);
    }

    [Fact]
    public void ANewRunReplacesTheOldLog()
    {
        string path = TempLog();
        using (var log = new LogFile(path, "run 1", append: false)) log.WriteLine("a");
        using (var log = new LogFile(path, "run 2", append: false)) { }
        Assert.Equal(new[] { "run 2" }, File.ReadAllLines(path));
        File.Delete(path);
    }
}

public class ScreenshotNameTests
{
    static readonly DateTime When = new DateTime(2026, 11, 8, 7, 5, 9);

    [Fact]
    public void NamedByDateAndTime()
    {
        string path = ScreenshotName.Next("pics", When, _ => false);
        Assert.Equal(Path.Combine("pics", "UxPlay 2026-11-08 07-05-09.png"), path);
    }

    [Fact]
    public void NeverOverwritesAnEarlierOne()
    {
        var taken = new HashSet<string>
        {
            Path.Combine("pics", "UxPlay 2026-11-08 07-05-09.png"),
            Path.Combine("pics", "UxPlay 2026-11-08 07-05-09 (2).png"),
        };
        string path = ScreenshotName.Next("pics", When, taken.Contains);
        Assert.Equal(Path.Combine("pics", "UxPlay 2026-11-08 07-05-09 (3).png"), path);
    }
}

public class ErrorLogTests
{
    [Fact]
    public void SavesTheErrorWithItsTime()
    {
        string dir = Path.Combine(Path.GetTempPath(), "uxplay-errlog-" + Guid.NewGuid());
        string path = Path.Combine(dir, "tray-error.log");
        try
        {
            Assert.True(ErrorLog.Append(path, new DateTime(2026, 11, 8, 9, 1, 2), new InvalidOperationException("boom")));
            Assert.True(ErrorLog.Append(path, new DateTime(2026, 11, 8, 9, 1, 3), null));
            string text = File.ReadAllText(path);
            Assert.StartsWith("2026-11-08 09:01:02  System.InvalidOperationException: boom", text);
            Assert.Contains("2026-11-08 09:01:03  unknown error", text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void OldFileNameKeepsTheExtension()
    {
        Assert.Equal(Path.Combine("d", "tray-error.old.log"), ErrorLog.OldPath(Path.Combine("d", "tray-error.log")));
    }
}
