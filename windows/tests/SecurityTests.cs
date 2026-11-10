using System;
using System.Collections.Generic;
using System.IO;
using UxPlayTray;
using Xunit;

// What keeps UxPlay safe on any network: the firewall rule, the PIN and the public-network warning.

public class FirewallRuleTests
{
    [Fact]
    public void OnlyPrivateAndDomainNetworks()
    {
        var args = FirewallRule.NetshArguments(@"C:\Program Files\UxPlay\uxplay\bin\uxplay.exe");
        Assert.Equal("advfirewall firewall delete rule name=\"UxPlay\"", args[0]);
        Assert.Equal("advfirewall firewall add rule name=\"UxPlay\" dir=in action=allow enable=yes profile=private,domain " +
                     "program=\"C:\\Program Files\\UxPlay\\uxplay\\bin\\uxplay.exe\"", args[1]);
        Assert.DoesNotContain("profile=any", args[1]);
        Assert.DoesNotContain("public", args[1]);
    }

    [Theory]
    [InlineData(@"C:\Program Files\UxPlay\uxplay\bin\uxplay.exe")]
    [InlineData(@"D:\Apps\UxPlay (portable)\uxplay\bin\uxplay.exe")]
    [InlineData(@"C:\Users\Zoë\Desktop\UxPlay\uxplay\bin\uxplay.exe")]
    [InlineData(@"E:\Tom & Jerry's\uxplay.exe")]  // & is fine: no shell reads it
    public void NormalFoldersAreAccepted(string path)
    {
        Assert.True(FirewallRule.IsSafeProgramPath(path));
        Assert.Equal(5, FirewallRule.NetshArguments(path).Length);
    }

    [Fact]
    public void ThePinIsOnForNewInstalls()
    {
        Assert.True(new Settings().RequirePin);
        Assert.Contains("-pin", UxPlayCommandLine.Build(new Settings(), @"C:\data"));
    }
}

public class NetworkSafetyTests
{
    static ConnectedNetwork Net(string adapter, NetworkCategory category) => new ConnectedNetwork(adapter, category);

    [Fact]
    public void WarnsWhenTheWiFiIsPublic()
    {
        Assert.True(NetworkSafety.OnlyPublic(new[] { Net("Wi-Fi", NetworkCategory.Public) }));
    }

    [Theory]
    [InlineData(1)]  // Private
    [InlineData(2)]  // Domain (a work network)
    public void QuietOnAPrivateOrWorkNetwork(int category)
    {
        Assert.False(NetworkSafety.OnlyPublic(new[] { Net("Wi-Fi", (NetworkCategory)category) }));
    }

    [Fact]
    public void QuietWhenWindowsCantSayOrNothingIsConnected()
    {
        Assert.False(NetworkSafety.OnlyPublic(null));
        Assert.False(NetworkSafety.OnlyPublic(new ConnectedNetwork[0]));
    }
}

// For networks that keep phones from finding PCs (campus, school, office): the iPhone joins
// the PC's own Mobile hotspot instead, which Windows calls a Public network.
public class HotspotFirewallTests
{
    const string Exe = @"C:\Program Files\UxPlay\uxplay\bin\uxplay.exe";

    [Fact]
    public void HotspotRulesLetInOnlyTheHotspot()
    {
        var args = FirewallRule.NetshArguments(Exe);
        Assert.Equal("advfirewall firewall delete rule name=\"UxPlay (Mobile hotspot)\"", args[2]);
        Assert.Equal("advfirewall firewall add rule name=\"UxPlay (Mobile hotspot)\" dir=in action=allow enable=yes profile=public " +
                     "remoteip=192.168.137.0/24 localip=192.168.137.1 program=\"" + Exe + "\"", args[3]);
        Assert.Equal("advfirewall firewall add rule name=\"UxPlay (Mobile hotspot)\" dir=in action=allow enable=yes profile=public " +
                     "remoteip=192.168.137.0/24 protocol=udp localport=5353 program=\"" + Exe + "\"", args[4]);
    }

    [Fact]
    public void NoRuleOpensAPublicNetworkToEveryone()
    {
        foreach (string a in FirewallRule.NetshArguments(Exe))
        {
            Assert.DoesNotContain("profile=any", a);
            if (!a.Contains("profile=public"))
                continue;
            // only devices on the hotspot, and only to its address or the mDNS port
            Assert.Contains(" remoteip=192.168.137.0/24 ", a);
            Assert.True(a.Contains(" localip=192.168.137.1 ") || a.Contains(" protocol=udp localport=5353 "), a);
            Assert.Contains("name=\"UxPlay (Mobile hotspot)\"", a);
        }
    }

    [Fact]
    public void EachRuleIsReplacedNotAddedTwice()
    {
        var args = FirewallRule.NetshArguments(Exe);
        foreach (string name in new[] { FirewallRule.Name, FirewallRule.HotspotName })
        {
            string tag = "name=\"" + name + "\"";
            int delete = Array.FindIndex(args, a => a.Contains(" delete rule " + tag));
            int firstAdd = Array.FindIndex(args, a => a.Contains(" add rule " + tag));
            Assert.True(delete >= 0 && firstAdd > delete, name);
        }
    }

    [Theory]
    [InlineData("advfirewall firewall add rule name=\"UxPlay\" dir=in", true)]
    [InlineData("advfirewall firewall delete rule name=\"UxPlay\"", false)]
    [InlineData("add rule", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAddingMustSucceed(string args, bool must)
    {
        Assert.Equal(must, FirewallRule.MustSucceed(args));
    }

    [Theory]
    [InlineData(new[] { 0, 0, 0, 0, 0 }, 0)]
    [InlineData(new[] { 1, 0, 1, 0, 0 }, 0)]      // no old rules to delete: fine
    [InlineData(new[] { 0, 5, 0, 0, 0 }, 5)]      // the main rule failed
    [InlineData(new[] { 0, 0, 0, 0, 7 }, 7)]      // a hotspot rule failed
    [InlineData(new[] { 0, 3, 0, 4, 0 }, 3)]      // the first failure is reported
    [InlineData(new[] { 0, 0 }, -1)]              // netsh didn't run for them all
    public void TheResultIsTheFirstFailedAdd(int[] codes, int expected)
    {
        Assert.Equal(expected, FirewallRule.CombinedExitCode(FirewallRule.NetshArguments(Exe), codes));
    }

    [Fact]
    public void TheInstallerAddsTheSameRules()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "windows", "installer.iss")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        string iss = File.ReadAllText(Path.Combine(dir, "windows", "installer.iss"));
        const string installed = @"{app}\uxplay\bin\uxplay.exe";
        foreach (string a in FirewallRule.NetshArguments(@"C:\X\uxplay.exe"))
        {
            // Inno Setup doubles quotes inside a parameter
            string line = "Parameters: \"" + a.Replace(@"C:\X\uxplay.exe", installed).Replace("\"", "\"\"") + "\"";
            Assert.Contains(line, iss);
        }
        // and removes both when uninstalled
        Assert.Contains("RunOnceId: \"RemoveFirewallRule\"", iss);
        Assert.Contains("delete rule name=\"\"UxPlay (Mobile hotspot)\"\"\"; Flags: runhidden; RunOnceId: \"RemoveHotspotFirewallRule\"", iss);
    }
}
