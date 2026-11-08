using System;
using System.Collections.Generic;
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
        Assert.Equal(2, FirewallRule.NetshArguments(path).Length);
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
