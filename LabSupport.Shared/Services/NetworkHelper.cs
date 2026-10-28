using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LabSupport.Shared.Services;

public static class NetworkHelper
{
    private static readonly string[] VirtualKeywords =
    {
        "vethernet",
        "virtualbox",
        "vmware",
        "wsl",
        "loopback",
        "hyper-v",
        "docker",
        "wireguard",
        "openvpn",
        "tailscale",
        "zerotier",
        "virtual"
    };

    public static bool IsVirtualAdapter(string name, string description)
    {
        var n = name ?? string.Empty;
        var d = description ?? string.Empty;

        foreach (var k in VirtualKeywords)
        {
            if (n.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                d.Contains(k, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (n.StartsWith("veth", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("veth", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (n.Contains("tap", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("tap", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool IsVirtualAdapter(NetworkInterface ni)
    {
        return IsVirtualAdapter(ni.Name, ni.Description);
    }

    public static List<string> GetLocalIPv4Addresses()
    {
        var physicalEthernet = new List<string>();
        var wifi = new List<string>();
        var otherNonVirtual = new List<string>();
        var virtualOrFallback = new List<string>();

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                bool isVirtual = IsVirtualAdapter(ni);

                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    string ipStr = addr.Address.ToString();
                    if (ipStr == "127.0.0.1")
                        continue;

                    if (isVirtual)
                    {
                        virtualOrFallback.Add(ipStr);
                    }
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    {
                        physicalEthernet.Add(ipStr);
                    }
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        wifi.Add(ipStr);
                    }
                    else
                    {
                        otherNonVirtual.Add(ipStr);
                    }
                }
            }
        }
        catch
        {
            // Fallback
        }

        static IEnumerable<string> PrioritizeNonApipa(IEnumerable<string> list) =>
            list.OrderBy(ip => ip.StartsWith("169.254.") ? 1 : 0);

        var result = new List<string>();
        result.AddRange(PrioritizeNonApipa(physicalEthernet));
        result.AddRange(PrioritizeNonApipa(wifi));
        result.AddRange(PrioritizeNonApipa(otherNonVirtual));

        if (result.Count == 0)
        {
            result.AddRange(PrioritizeNonApipa(virtualOrFallback));
        }

        var distinct = result.Distinct().ToList();
        if (distinct.Count == 0)
        {
            distinct.Add("127.0.0.1");
        }

        return distinct;
    }

    public static string GetPrimaryLanIPv4Address()
    {
        var ips = GetLocalIPv4Addresses();
        var primary = ips.FirstOrDefault(ip => !ip.StartsWith("127.") && !ip.StartsWith("169.254."));
        if (!string.IsNullOrEmpty(primary))
        {
            return primary;
        }

        return ips.FirstOrDefault() ?? "127.0.0.1";
    }

    public static string GetDefaultMacAddress()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var bytes = ni.GetPhysicalAddress().GetAddressBytes();
                if (bytes.Length == 6 && bytes.Any(b => b != 0))
                {
                    return string.Join(":", bytes.Select(b => b.ToString("X2")));
                }
            }
        }
        catch
        {
            // Fallback
        }

        return "00:00:00:00:00:00";
    }

    public static async Task<bool> PingHostAsync(string host, int timeoutMs = 2000)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
