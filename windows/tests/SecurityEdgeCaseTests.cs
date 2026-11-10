using System;
using System.Collections.Generic;
using UxPlayTray;
using Xunit;

// Attempts to get a command run as administrator, or to slip past the network check.

public class FirewallRuleEdgeCases
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("uxplay.exe")]                                     // relative: would be looked up somewhere
    [InlineData(@"\uxplay.exe")]
    [InlineData(@"\\server\share\uxplay.exe")]                     // a network share someone else controls
    [InlineData(@"C:\a\..\Windows\System32\cmd.exe")]              // climbing out of the folder
    [InlineData("C:\\a\" dir=out action=allow program=\"C:\\x.exe")]  // a quote ends the path: more netsh options
    [InlineData("C:\\a\\uxplay.exe\" profile=any \"")]
    [InlineData(@"C:\%TEMP%\uxplay.exe")]                         // shell variables
    [InlineData("C:\\a\\uxplay.exe\r\nnetsh advfirewall set allprofiles state off")]
    [InlineData("C:\\a|calc\\uxplay.exe")]
    [InlineData("C:\\a>out\\uxplay.exe")]
    [InlineData(@"C:\a\uxplay.bat")]                               // only programs
    [InlineData(@"C:\a\uxplay.exe.txt")]
    [InlineData("C:\\a\tb\\uxplay.exe")]
    public void AnythingButAPlainProgramPathIsRefused(string path)
    {
        Assert.False(FirewallRule.IsSafeProgramPath(path));
        Assert.Throws<ArgumentException>(() => FirewallRule.NetshArguments(path));
    }

    [Fact]
    public void TheElevatedSwitchTakesNoPathFromTheCommandLine()
    {
        // the copy running as administrator works out uxplay.exe's path itself
        Assert.DoesNotContain(" ", FirewallRule.ElevatedSwitch);
        Assert.StartsWith("--", FirewallRule.ElevatedSwitch);
    }

    [Fact]
    public void RandomTextNeverGetsAQuoteIntoTheRule()
    {
        var random = new Random(7);
        const string chars = "abcXYZ019 .\\:\"'&|<>%^!()[]{};,=\r\n\t-_~$#@";
        for (int i = 0; i < 20000; i++)
        {
            var sb = new System.Text.StringBuilder(i % 3 == 0 ? "C:\\" : "");
            int len = random.Next(1, 40);
            for (int j = 0; j < len; j++)
                sb.Append(chars[random.Next(chars.Length)]);
            if (i % 2 == 0)
                sb.Append(".exe");
            string path = sb.ToString();
            if (!FirewallRule.IsSafeProgramPath(path))
                continue;
            foreach (string add in FirewallRule.NetshArguments(path))
            {
                if (!FirewallRule.MustSucceed(add))
                    continue;
                // exactly the two quotes around the name and the two around the path
                Assert.Equal(4, add.Split('"').Length - 1);
                Assert.DoesNotContain("\n", add);
                Assert.DoesNotContain("%", add);
            }
        }
    }
}

public class NetworkSafetyEdgeCases
{
    static ConnectedNetwork Net(string adapter, NetworkCategory category) => new ConnectedNetwork(adapter, category);

    [Fact]
    public void VirtualAdaptersDontHideAPublicWiFi()
    {
        // WSL's or a VPN's adapter being Private doesn't let an iPhone in
        Assert.True(NetworkSafety.OnlyPublic(new[]
        {
            Net("Wi-Fi", NetworkCategory.Public),
            Net("vEthernet (WSL (Hyper-V firewall))", NetworkCategory.Private),
            Net("Tailscale", NetworkCategory.Private),
            Net("VMware Network Adapter VMnet8", NetworkCategory.Private),
        }));
    }

    [Fact]
    public void OnlyVirtualAdaptersIsNoReasonToWarn()
    {
        Assert.False(NetworkSafety.OnlyPublic(new[] { Net("vEthernet (Default Switch)", NetworkCategory.Public) }));
    }

    [Fact]
    public void AnyPrivateRealNetworkIsEnough()
    {
        Assert.False(NetworkSafety.OnlyPublic(new[]
        {
            Net("Wi-Fi", NetworkCategory.Public),
            Net("Ethernet", NetworkCategory.Private),
        }));
    }

    [Fact]
    public void AnAdapterWithNoNameCountsAsReal()
    {
        Assert.True(NetworkSafety.OnlyPublic(new[] { Net(null, NetworkCategory.Public) }));
    }
}

public class PinEdgeCases
{
    [Fact]
    public void AnOldSettingsFileKeepsItsChoice()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".ini");
        try
        {
            System.IO.File.WriteAllText(path, "pin=false\n");
            Assert.False(new IniSettingsStore(path).Load().RequirePin);
            System.IO.File.WriteAllText(path, "name=PC\n");  // older file without the line: the safe default
            Assert.True(new IniSettingsStore(path).Load().RequirePin);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
