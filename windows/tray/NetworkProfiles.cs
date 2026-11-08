using System;
using System.Collections.Generic;

namespace UxPlayTray
{
    /// <summary>What Windows calls a network (Settings → Network → Properties).</summary>
    enum NetworkCategory
    {
        Public = 0,
        Private = 1,
        Domain = 2,
    }

    /// <summary>A network this PC is connected to.</summary>
    struct ConnectedNetwork
    {
        public string Adapter;
        public NetworkCategory Category;

        public ConnectedNetwork(string adapter, NetworkCategory category)
        {
            Adapter = adapter;
            Category = category;
        }
    }

    /// <summary>The networks this PC is connected to.</summary>
    interface INetworkProfiles
    {
        /// <summary>null when Windows can't say.</summary>
        IReadOnlyList<ConnectedNetwork> Connected();
    }

    static class NetworkSafety
    {
        // Adapters of virtual machines, WSL and VPNs: an iPhone never connects through them.
        static readonly string[] VirtualAdapters =
        {
            "vEthernet", "VirtualBox", "VMware", "Hyper-V", "WSL", "Tailscale", "WireGuard", "ZeroTier", "TAP-", "OpenVPN", "Loopback",
        };

        static bool IsVirtual(string adapter)
        {
            foreach (string name in VirtualAdapters)
                if ((adapter ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>
        /// True when every real network is Public: then Windows Firewall keeps iPhones out
        /// (as it should on a café's Wi-Fi), and a user at home needs to know why.
        /// </summary>
        public static bool OnlyPublic(IReadOnlyList<ConnectedNetwork> networks)
        {
            if (networks == null)
                return false;
            bool any = false;
            foreach (var n in networks)
            {
                if (IsVirtual(n.Adapter))
                    continue;
                if (n.Category != NetworkCategory.Public)
                    return false;
                any = true;
            }
            return any;
        }
    }
}
