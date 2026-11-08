using UxPlayTray;
using Xunit;

public class SettingsTests
{
    static Settings Defaults() => new Settings { Name = "Living Room PC" };

    [Fact]
    public void DefaultsUseDirect3DWithToggleAnd60Fps()
    {
        string args = Defaults().BuildArguments();
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
        Assert.Contains("alt-enter+property fullscreen=true\"", s.BuildArguments());
    }

    [Fact]
    public void OwnVideoSinkIsLeftAlone()
    {
        var s = Defaults();
        s.FullScreen = true;
        s.ExtraArgs = "-vs d3d12videosink";
        string args = s.BuildArguments();
        Assert.DoesNotContain("d3d11videosink", args);
        Assert.Contains(" -fs ", args);
        Assert.EndsWith("-vs d3d12videosink", args);
    }

    [Fact]
    public void PinUsesRandomCodeAndRegister()
    {
        var s = Defaults();
        s.RequirePin = true;
        string args = s.BuildArguments();
        Assert.Contains("-pin -reg \"", args);
        Assert.Contains("uxplay.register\"", args);
    }

    [Fact]
    public void NoSoundTurnsAudioOff()
    {
        var s = Defaults();
        s.Audio = false;
        s.AudioSink = "wasapisink";
        string args = s.BuildArguments();
        Assert.Contains("-as 0", args);
        Assert.DoesNotContain("wasapisink", args);
    }

    [Fact]
    public void SoundOutputIsPassed()
    {
        var s = Defaults();
        s.AudioSink = "directsoundsink";
        Assert.Contains("-as directsoundsink", s.BuildArguments());
        s.ExtraArgs = "-as wasapi2sink";
        Assert.DoesNotContain("directsoundsink", s.BuildArguments());
    }

    [Fact]
    public void QualityAndSmoothness()
    {
        var s = Defaults();
        s.Resolution = "1280x720";
        s.Fps = 30;
        string args = s.BuildArguments();
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
