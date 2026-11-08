using System;
using System.Collections.Generic;
using System.Linq;
using UxPlayTray;
using Xunit;

// The hard cases: odd, broken or hostile input, unusual sequences of events, and
// randomised "never crashes, never loses a setting" checks. TrayTests.cs has the
// everyday cases.

public class CommandLineEdgeCases
{
    [Fact]
    public void LowDelayIsNotMistakenForAVideoOutput()
    {
        // "-vsync" starts with "-vs" but isn't the video output option
        var s = new Settings { ExtraArgs = "-vsync no" };
        Assert.False(s.HasOwnVideoSink);
        Assert.Contains("d3d11videosink", s.Build());
    }

    [Theory]
    [InlineData("-vs\td3d12videosink")]
    [InlineData("  -vs   d3d12videosink  ")]
    [InlineData("-d\r\n-vs d3d12videosink")]
    public void OptionsAreFoundWhateverTheSpacing(string extra)
    {
        var s = new Settings { ExtraArgs = extra };
        Assert.True(s.HasOwnVideoSink);
        Assert.DoesNotContain("d3d11videosink", s.Build());
    }

    [Fact]
    public void AnOptionMustBeAWholeWord()
    {
        var s = new Settings { ExtraArgs = "-asx -hlsfoo" };
        Assert.False(s.HasOption("-as"));
        Assert.False(s.HasOption("-hls"));
        Assert.False(new Settings().HasOption("-as"));
    }

    [Fact]
    public void EverySettingTogether()
    {
        var s = new Settings
        {
            Name = "Den", FullScreen = true, Resolution = "2560x1440", Fps = 30, AudioSink = "wasapisink",
            RequirePin = true, LowLatency = true, YouTubeVideo = true, Record = true, ExtraArgs = "-d",
        };
        string args = UxPlayCommandLine.Build(s, @"C:\D", @"C:\V\UxPlay 2026-01-01 00-00-00");
        foreach (var part in new[] { "-n \"Den\"", "fullscreen=true\"", "-s 2560x1440", "-fps 30", "-vsync no", "-hls",
                                     "-as wasapisink", "-pin -reg", "-mp4 \"C:\\V\\UxPlay 2026-01-01 00-00-00\"" })
            Assert.Contains(part, args);
        Assert.EndsWith(" -d", args);  // the expert's own options come last, so they win
    }

    [Fact]
    public void ExpertOptionsReplaceTheAppsOwn()
    {
        var s = new Settings
        {
            LowLatency = true, YouTubeVideo = true, Record = true, AudioSink = "wasapisink",
            ExtraArgs = "-vsync yes -hls 2 -mp4 own -as pulsesink",
        };
        string args = UxPlayCommandLine.Build(s, "D", "R");
        Assert.DoesNotContain("-vsync no", args);
        Assert.DoesNotContain("wasapisink", args);
        Assert.DoesNotContain("\"R\"", args);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(args, " -hls"));
    }

    [Fact]
    public void RecordingNeedsAName()
    {
        // the recordings folder couldn't be created: uxplay must still start, without -mp4
        Assert.DoesNotContain("-mp4", UxPlayCommandLine.Build(new Settings { Record = true }, "D", null));
    }

    [Theory]
    [InlineData("1280x720 -as 0")]
    [InlineData("\" -evil \"")]
    [InlineData("big")]
    [InlineData("x720")]
    public void ABadQualityValueIsIgnored(string resolution)
    {
        // a hand-edited settings.ini must not be able to add options
        var s = IniSettingsStore.Parse(new[] { "resolution=" + resolution });
        Assert.Equal("", s.Resolution);
        Assert.DoesNotContain("-s ", s.Build());
    }

    [Theory]
    [InlineData("1280x720")]
    [InlineData("3840x2160@120")]
    public void ACustomQualityIsKept(string resolution) =>
        Assert.Equal(resolution, IniSettingsStore.Parse(new[] { "resolution=" + resolution }).Resolution);

    [Theory]
    [InlineData("\"; rm -rf /")]
    [InlineData("\u0000\u0007bell\u001b")]
    [InlineData("Living Room \\\" -pin")]
    public void ANameCannotBreakOutOfItsQuotes(string name)
    {
        string args = new Settings { Name = name }.Build();
        int end = args.IndexOf('"', 4);
        Assert.StartsWith("-n \"", args);
        Assert.StartsWith("\" -nh ", args.Substring(end));  // the name ends where it should
    }

    [Fact]
    public void LongAndForeignNamesAreKept()
    {
        Assert.Equal("Wohnzimmer-PC ü 客厅 🎬", Settings.CleanName("Wohnzimmer-PC ü 客厅 🎬"));
        string longName = new string('a', 500);
        Assert.Equal(longName, Settings.CleanName(longName));
    }
}

public class SettingsFileEdgeCases
{
    [Fact]
    public void ValuesMayContainEqualsSigns() =>
        Assert.Equal("-d -as=x", IniSettingsStore.Parse(new[] { "extra=-d -as=x" }).ExtraArgs);

    [Fact]
    public void TheLastOfTwoLinesWins() =>
        Assert.Equal(30, IniSettingsStore.Parse(new[] { "fps=60", "fps=30" }).Fps);

    [Fact]
    public void SpacesAroundKeysAndValuesAreIgnored()
    {
        var s = IniSettingsStore.Parse(new[] { "  name =  Den  ", "\tfullscreen\t=\t1" });
        Assert.Equal("Den", s.Name);
        Assert.True(s.FullScreen);
    }

    [Theory]
    [InlineData("fps=0")]
    [InlineData("fps=-30")]
    [InlineData("fps=99999999999999999999")]
    [InlineData("fps=30.5")]
    [InlineData("fps=")]
    public void ImpossibleFrameRatesFallBackTo60(string line) =>
        Assert.Equal(60, IniSettingsStore.Parse(new[] { line }).Fps);

    [Fact]
    public void UnknownAndBrokenLinesAreSkipped()
    {
        var s = IniSettingsStore.Parse(new[] { "", "   ", "# comment", "[section]", "=", "==", "newkey=1", "fullscreen" });
        Assert.Equal(new Settings().Build(), s.Build());
    }

    [Fact]
    public void AFileFromAnOlderVersionKeepsItsSettingsAndGetsNewDefaults()
    {
        // written by v1.74-win1, before sound output, updates, recording and the rest existed
        var s = IniSettingsStore.Parse(new[] { "name=Den", "fullscreen=1", "resolution=1280x720", "audio=1", "pin=0", "extra=" });
        Assert.Equal("Den", s.Name);
        Assert.True(s.FullScreen);
        Assert.True(s.CheckForUpdates);
        Assert.False(s.Record);
        Assert.Equal(60, s.Fps);
    }

    [Fact]
    public void RandomSettingsSurviveSavingAndLoading()
    {
        var random = new Random(1234);  // fixed seed: the same cases on every run
        for (int i = 0; i < 2000; i++)
        {
            var s = RandomSettings(random);
            var back = IniSettingsStore.Parse(IniSettingsStore.Format(s));
            Assert.Equal(IniSettingsStore.Format(s), IniSettingsStore.Format(back));
            Assert.Equal(UxPlayCommandLine.Build(s, "D", "R"), UxPlayCommandLine.Build(back, "D", "R"));
        }
    }

    [Fact]
    public void RandomGarbageNeverCrashesLoading()
    {
        var random = new Random(99);
        for (int i = 0; i < 2000; i++)
        {
            var lines = Enumerable.Range(0, random.Next(20)).Select(_ => RandomText(random)).ToArray();
            var s = IniSettingsStore.Parse(lines);
            Assert.InRange(s.Fps, 1, 60);
            Assert.Contains(Settings.AudioOutputs, o => o.Sink == s.AudioSink);
            UxPlayCommandLine.Build(s, "D", "R");
        }
    }

    internal static Settings RandomSettings(Random r) => new Settings
    {
        Name = "PC " + r.Next(1000),
        FullScreen = r.Next(2) == 0,
        Resolution = Settings.Resolutions[r.Next(Settings.Resolutions.Length)].Size,
        Fps = Settings.FrameRates[r.Next(Settings.FrameRates.Length)].Fps,
        Audio = r.Next(2) == 0,
        AudioSink = Settings.AudioOutputs[r.Next(Settings.AudioOutputs.Length)].Sink,
        RequirePin = r.Next(2) == 0,
        ExtraArgs = r.Next(3) == 0 ? "-d" : "",
        CheckForUpdates = r.Next(2) == 0,
        KeepOnTop = r.Next(2) == 0,
        Record = r.Next(2) == 0,
        LowLatency = r.Next(2) == 0,
        YouTubeVideo = r.Next(2) == 0,
    };

    internal static string RandomText(Random r)
    {
        const string chars = "=abcdefghinoprstuxy0123456789 -\"\\\t\u00e9\u5ba2";
        string[] keys = { "name", "fps", "audiosink", "resolution", "extra", "pin", "record", "hls" };
        string text = new string(Enumerable.Range(0, r.Next(30)).Select(_ => chars[r.Next(chars.Length)]).ToArray());
        return r.Next(2) == 0 ? keys[r.Next(keys.Length)] + "=" + text : text;
    }
}

public class ConnectionEdgeCases
{
    sealed class Recorder
    {
        public readonly List<string> Events = new List<string>();
        public Recorder(ConnectionMonitor m)
        {
            m.PinRequested += p => Events.Add("pin " + p);
            m.Connected += (d, n) => Events.Add((n ? "new " : "again ") + d);
            m.Disconnected += () => Events.Add("gone");
        }
    }

    [Theory]
    [InlineData("connection request from Sam's (Work) iPhone (iPhone17,1) with deviceID = 1", "Sam's (Work) iPhone")]
    [InlineData("connection request from iPad (2) (iPad13,4) with deviceID = 2", "iPad (2)")]
    [InlineData("connection request from  Spaces  (iPhone15,2) with deviceID = 3", "Spaces")]
    [InlineData("connection request from 客厅的 iPhone (iPhone16,1) with deviceID = 4", "客厅的 iPhone")]
    public void DeviceNamesWithBracketsAndSpaces(string line, string expected)
    {
        string device = null;
        var m = new ConnectionMonitor();
        m.Connected += (d, n) => device = d;
        m.OnLine(line);
        Assert.Equal(expected, device);
    }

    [Theory]
    [InlineData("Accepted IPv4 client on socket 99999999999999, port 7000")]
    [InlineData("Connection closed on socket 99999999999999")]
    public void HugeSocketNumbersDontCrash(string line) => new ConnectionMonitor().OnLine(line);

    [Theory]
    [InlineData("*** CLIENT MUST NOW ENTER PIN = \"123\" AS AIRPLAY PASSWORD")]
    [InlineData("*** CLIENT MUST NOW ENTER PIN = \"12345\" AS AIRPLAY PASSWORD")]
    [InlineData("*** CLIENT MUST NOW ENTER PIN = \"abcd\" AS AIRPLAY PASSWORD")]
    public void OnlyAFourDigitPinIsShown(string line)
    {
        var m = new ConnectionMonitor();
        var r = new Recorder(m);
        m.OnLine(line);
        Assert.Empty(r.Events);
    }

    [Fact]
    public void ANewPinReplacesTheOld()
    {
        var m = new ConnectionMonitor();
        var r = new Recorder(m);
        m.OnLine("*** CLIENT MUST NOW ENTER PIN = \"1111\" AS AIRPLAY PASSWORD");
        m.OnLine("*** CLIENT MUST NOW ENTER PIN = \"2222\" AS AIRPLAY PASSWORD");
        Assert.Equal(new[] { "pin 1111", "pin 2222" }, r.Events);
    }

    [Fact]
    public void ASocketClosedTwiceDisconnectsOnce()
    {
        var m = new ConnectionMonitor();
        var r = new Recorder(m);
        m.OnLine("Accepted IPv4 client on socket 5, port 7000");
        m.OnLine("connection request from A (iPhone) with deviceID = 1");
        m.OnLine("Connection closed on socket 5");
        m.OnLine("Connection closed on socket 5");
        Assert.Equal(new[] { "new A", "gone" }, r.Events);
    }

    [Fact]
    public void TwoDevicesTakingTurns()
    {
        var m = new ConnectionMonitor();
        var r = new Recorder(m);
        m.OnLine("Accepted IPv4 client on socket 5, port 7000");
        m.OnLine("connection request from A (iPhone) with deviceID = 1");
        m.OnLine("Accepted IPv6 client on socket 6, port 7000");
        m.OnLine("connection request from B (iPad) with deviceID = 2");   // B takes over (-nohold)
        m.OnLine("Connection closed on socket 5");                         // A's socket goes
        Assert.True(m.IsConnected);
        m.OnLine("Connection closed on socket 6");
        Assert.False(m.IsConnected);
        m.OnLine("Accepted IPv4 client on socket 7, port 7000");
        m.OnLine("connection request from A (iPhone) with deviceID = 1");  // A comes back
        Assert.Equal(new[] { "new A", "again B", "gone", "new A" }, r.Events);
    }

    [Fact]
    public void UxPlayRestartingMidMirror()
    {
        var m = new ConnectionMonitor();
        var r = new Recorder(m);
        m.OnLine("Accepted IPv4 client on socket 5, port 7000");
        m.OnLine("connection request from A (iPhone) with deviceID = 1");
        m.Reset();  // the old uxplay.exe has gone: its sockets mean nothing now
        m.OnLine("Connection closed on socket 5");
        m.OnLine("Accepted IPv4 client on socket 5, port 7000");
        m.OnLine("connection request from A (iPhone) with deviceID = 1");
        Assert.Equal(new[] { "new A", "new A" }, r.Events);
    }

    [Fact]
    public void LinesThatOnlyLookAlikeAreIgnored()
    {
        var m = new ConnectionMonitor();
        var r = new Recorder(m);
        m.OnLine("Accepted IPv4 client on socket 5, port 7000");
        m.OnLine("connection request from A (iPhone) with deviceID = 1");
        m.OnLine("raop_rtp: Connection closed on socket 5");    // not at the start of the line
        m.OnLine("connection request from nobody");             // no model or deviceID
        m.OnLine("");
        Assert.Equal(new[] { "new A" }, r.Events);
        Assert.True(m.IsConnected);
    }

    [Fact]
    public void RandomLinesNeverCrashAndStayConsistent()
    {
        var random = new Random(7);
        string[] lines =
        {
            "Accepted IPv4 client on socket {0}, port 7000", "Connection closed on socket {0}",
            "connection request from D{0} (iPhone) with deviceID = {0}",
            "*** CLIENT MUST NOW ENTER PIN = \"{0:D4}\" AS AIRPLAY PASSWORD", "noise {0}",
        };
        var m = new ConnectionMonitor();
        bool connected = false;
        m.Connected += (d, n) => { Assert.Equal(!connected, n); connected = true; };
        m.Disconnected += () => { Assert.True(connected); connected = false; };
        for (int i = 0; i < 20000; i++)
        {
            if (random.Next(500) == 0)
            {
                m.Reset();
                connected = false;
            }
            m.OnLine(string.Format(lines[random.Next(lines.Length)], random.Next(8)));
            Assert.Equal(connected, m.IsConnected);
        }
    }
}

public class ReleaseTagEdgeCases
{
    [Theory]
    [InlineData("v1.74.1-win1", "v1.74-win9", true)]   // a point release of UxPlay itself
    [InlineData("v2-win1", "v1.74-win9", true)]
    [InlineData("v1.74-win3-rc1", "v1.74-win3-beta", false)]  // two pre-releases of the same build
    [InlineData("v1.74-win3-beta", "v1.74-win2", true)]
    [InlineData("V1.74-win3", "v1.74-win2", false)]    // not our tag format
    [InlineData("v1.74-win", "v1.74-win2", false)]
    [InlineData("v1.74-win99999999999", "v1.74-win2", false)]
    [InlineData("v99999999999.1-win1", "v1.74-win2", false)]
    [InlineData("", "", false)]
    [InlineData(" v1.74-win3", "v1.74-win2", false)]
    public void UnusualTags(string latest, string current, bool newer) =>
        Assert.Equal(newer, ReleaseTag.IsNewer(latest, current));

    [Fact]
    public void NewerIsNeverBothWays()
    {
        string[] tags = { "v1.74-win1", "v1.74-win2", "v1.74-win2-beta", "v1.74-win10", "v1.75-win1", "v1.74.1-win1", "x" };
        foreach (var a in tags)
            foreach (var b in tags)
                Assert.False(ReleaseTag.IsNewer(a, b) && ReleaseTag.IsNewer(b, a), a + " / " + b);
    }

    [Theory]
    [InlineData("{\n  \"tag_name\" :\n  \"v1.74-win4\",\n  \"draft\": false\n}", "v1.74-win4")]
    [InlineData("{\"name\":\"tag_name\",\"tag_name\":\"v1.74-win4\"}", "v1.74-win4")]
    [InlineData("{\"tag_name\":\"\"}", null)]
    [InlineData("<html>Bad gateway</html>", null)]
    [InlineData("", null)]
    public void UnusualGitHubReplies(string json, string tag) =>
        Assert.Equal(tag, GitHubUpdateCheck.ParseTag(json));
}

public class OnTopWatcherEdgeCases
{
    [Fact]
    public void GivesUpWhenTheWindowNeverOpens()
    {
        var window = new FakeMirrorWindow { OpensAfter = int.MaxValue };
        var watcher = new OnTopWatcher(window);
        watcher.Start();
        int ticks = 0;
        while (watcher.Tick(true, true, 42))
            ticks++;
        Assert.Equal(OnTopWatcher.MaxTries, window.Tries);
        Assert.Equal(OnTopWatcher.MaxTries - 1, ticks);
        Assert.False(watcher.IsWatching);
    }

    [Theory]
    [InlineData(false, true)]   // the setting was turned off meanwhile
    [InlineData(true, false)]   // uxplay.exe stopped meanwhile
    public void StopsWithoutTouchingTheWindow(bool wanted, bool running)
    {
        var window = new FakeMirrorWindow();
        var watcher = new OnTopWatcher(window);
        watcher.Start();
        Assert.False(watcher.Tick(wanted, running, 42));
        Assert.Equal(0, window.Tries);
    }

    [Fact]
    public void ARestartGivesAFreshThirtySeconds()
    {
        var window = new FakeMirrorWindow { OpensAfter = int.MaxValue };
        var watcher = new OnTopWatcher(window);
        watcher.Start();
        for (int i = 0; i < OnTopWatcher.MaxTries - 5; i++)
            watcher.Tick(true, true, 42);
        watcher.Start();  // a second iPhone connected
        int ticks = 0;
        while (watcher.Tick(true, true, 42))
            ticks++;
        Assert.Equal(OnTopWatcher.MaxTries - 1, ticks);
    }

    [Fact]
    public void StopIsFinal()
    {
        var window = new FakeMirrorWindow { OpensAfter = int.MaxValue };
        var watcher = new OnTopWatcher(window);
        watcher.Start();
        watcher.Stop();
        Assert.False(watcher.Tick(true, true, 42));
        Assert.Equal(0, window.Tries);
    }
}

public class RestartPolicyEdgeCases
{
    static readonly DateTime T0 = new DateTime(2026, 1, 1, 20, 0, 0);
    static readonly TimeSpan LongRun = TimeSpan.FromMinutes(1);

    [Fact]
    public void ACrashLoopIsStoppedAfterThreeRestarts()
    {
        var p = new RestartPolicy();
        for (int i = 0; i < RestartPolicy.MaxRestarts; i++)
            Assert.Equal(RestartDecision.Restart, p.Decide(LongRun, T0.AddMinutes(i)));
        Assert.Equal(RestartDecision.StopsTooOften, p.Decide(LongRun, T0.AddMinutes(3)));
        Assert.Equal(RestartDecision.StopsTooOften, p.Decide(LongRun, T0.AddMinutes(4)));  // and stays stopped
    }

    [Fact]
    public void OldCrashesAreForgotten()
    {
        var p = new RestartPolicy();
        for (int i = 0; i < RestartPolicy.MaxRestarts; i++)
            p.Decide(LongRun, T0.AddMinutes(i));
        // ten minutes after the first crash it no longer counts
        Assert.Equal(RestartDecision.Restart, p.Decide(LongRun, T0 + RestartPolicy.Period));
    }

    [Fact]
    public void OneCrashADayIsAlwaysRestarted()
    {
        var p = new RestartPolicy();
        for (int day = 0; day < 365; day++)
            Assert.Equal(RestartDecision.Restart, p.Decide(TimeSpan.FromHours(20), T0.AddDays(day)));
    }

    [Fact]
    public void StartingAgainByHandGivesAFreshAllowance()
    {
        var p = new RestartPolicy();
        for (int i = 0; i <= RestartPolicy.MaxRestarts; i++)
            p.Decide(LongRun, T0.AddMinutes(i));
        p.Reset();
        Assert.Equal(RestartDecision.Restart, p.Decide(LongRun, T0.AddMinutes(5)));
    }

    [Theory]
    [InlineData(20, false)]   // exactly the limit is still "at start"
    [InlineData(21, true)]
    [InlineData(0, false)]
    public void TheStartLimit(int seconds, bool restarted) =>
        Assert.Equal(restarted ? RestartDecision.Restart : RestartDecision.StoppedAtStart,
            new RestartPolicy().Decide(TimeSpan.FromSeconds(seconds), T0));

    [Fact]
    public void ACrashAtStartDoesNotUseUpTheAllowance()
    {
        var p = new RestartPolicy();
        for (int i = 0; i < 10; i++)
            p.Decide(TimeSpan.FromSeconds(1), T0.AddMinutes(i));
        Assert.Equal(RestartDecision.Restart, p.Decide(LongRun, T0.AddMinutes(10)));
    }

    [Fact]
    public void TheClockGoingBackDoesNotBreakIt()
    {
        // Windows corrected the time, or daylight saving ended
        var p = new RestartPolicy();
        p.Decide(LongRun, T0);
        Assert.Equal(RestartDecision.Restart, p.Decide(LongRun, T0.AddHours(-1)));
        Assert.Equal(RestartDecision.Restart, p.Decide(LongRun, T0.AddHours(-1).AddMinutes(1)));
        Assert.Equal(RestartDecision.StopsTooOften, p.Decide(LongRun, T0.AddHours(-1).AddMinutes(2)));
    }
}

public class LogFileEdgeCases
{
    static string TempLog() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uxplay-test-" + Guid.NewGuid() + ".log");

    [Fact]
    public void AHugeLogStartsAgainInsteadOfFillingTheDisk()
    {
        string path = TempLog();
        string line = new string('x', 1000);
        using (var log = new LogFile(path, "header", append: false))
            for (int i = 0; i < 6000; i++)   // about 6 MB, over the 5 MB limit
                log.WriteLine(line);
        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal("header", lines[0]);
        Assert.Equal("(older messages were removed to keep this log small)", lines[1]);
        Assert.True(new System.IO.FileInfo(path).Length < 6 * 1024 * 1024);
        System.IO.File.Delete(path);
    }

    [Fact]
    public void AHugeOldLogIsNotAppendedTo()
    {
        string path = TempLog();
        System.IO.File.WriteAllText(path, new string('x', 6 * 1024 * 1024));
        using (var log = new LogFile(path, "new run", append: true)) { }
        Assert.Equal(new[] { "new run" }, System.IO.File.ReadAllLines(path));
        System.IO.File.Delete(path);
    }

    [Fact]
    public void AnUnwritableLogIsSkippedNotFatal()
    {
        // the folder doesn't exist: UxPlay must keep working without a log
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString(), "no", "uxplay.log");
        using (var log = new LogFile(path, "header", append: true))
            log.WriteLine("still fine");
        Assert.False(System.IO.File.Exists(path));
    }

    [Fact]
    public void WritingAfterCloseIsIgnored()
    {
        string path = TempLog();
        var log = new LogFile(path, "header", append: false);
        log.Dispose();
        log.WriteLine("late line from a reader thread");
        log.Dispose();
        Assert.Equal(new[] { "header" }, System.IO.File.ReadAllLines(path));
        System.IO.File.Delete(path);
    }

    [Fact]
    public void TwoThreadsWritingAtOnceLoseNothing()
    {
        // stdout and stderr are read on two threads
        string path = TempLog();
        using (var log = new LogFile(path, "header", append: false))
        {
            var a = new System.Threading.Thread(() => { for (int i = 0; i < 5000; i++) log.WriteLine("out " + i); });
            var b = new System.Threading.Thread(() => { for (int i = 0; i < 5000; i++) log.WriteLine("err " + i); });
            a.Start(); b.Start(); a.Join(); b.Join();
        }
        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal(10001, lines.Length);
        Assert.Equal(5000, lines.Count(l => l.StartsWith("out ")));
        Assert.All(lines.Skip(1), l => Assert.Matches(@"^(out|err) \d+$", l));
        System.IO.File.Delete(path);
    }
}

public class ScreenshotNameEdgeCases
{
    [Fact]
    public void SameNameWhateverTheLanguageOfWindows()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Arabic uses another calendar and digits; the file name must not change
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            string path = ScreenshotName.Next("p", new DateTime(2026, 1, 2, 3, 4, 5), _ => false);
            Assert.Equal(System.IO.Path.Combine("p", "UxPlay 2026-01-02 03-04-05.png"), path);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void ManyInTheSameSecondAllGetTheirOwnName()
    {
        var taken = new System.Collections.Generic.HashSet<string>();
        var when = new DateTime(2026, 1, 2, 3, 4, 5);
        for (int i = 0; i < 500; i++)
            Assert.True(taken.Add(ScreenshotName.Next("p", when, taken.Contains)));
        Assert.Equal(500, taken.Count);
    }
}
