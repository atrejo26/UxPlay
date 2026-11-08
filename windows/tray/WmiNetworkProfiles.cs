using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Management;

namespace UxPlayTray
{
    /// <summary>Asks Windows (MSFT_NetConnectionProfile, as Get-NetConnectionProfile does).</summary>
    [ExcludeFromCodeCoverage]  // needs Windows
    sealed class WmiNetworkProfiles : INetworkProfiles
    {
        public IReadOnlyList<ConnectedNetwork> Connected()
        {
            var list = new List<ConnectedNetwork>();
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\StandardCimv2",
                    "SELECT InterfaceAlias, NetworkCategory FROM MSFT_NetConnectionProfile");
                foreach (ManagementObject o in searcher.Get())
                    using (o)
                        list.Add(new ConnectedNetwork(o["InterfaceAlias"] as string,
                            (NetworkCategory)Convert.ToInt32(o["NetworkCategory"])));
                return list;
            }
            catch (Exception ex) when (ex is ManagementException || ex is System.Runtime.InteropServices.COMException ||
                                       ex is UnauthorizedAccessException || ex is InvalidCastException || ex is FormatException)
            {
                return null;
            }
        }
    }
}
