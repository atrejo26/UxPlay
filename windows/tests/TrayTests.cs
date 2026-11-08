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
