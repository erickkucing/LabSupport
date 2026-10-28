using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LabSupport.Shared.Protocol;

public static class WolHelper
{
    public static byte[] CreateMagicPacket(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
        {
            throw new ArgumentException("MAC address cannot be empty.", nameof(macAddress));
        }

        // Clean MAC address (remove colons, dashes, spaces)
        string cleaned = Regex.Replace(macAddress, "[:\\-\\s]", "");
        if (cleaned.Length != 12)
        {
            throw new ArgumentException($"Invalid MAC address format: '{macAddress}'. Must contain 12 hex digits.", nameof(macAddress));
        }

        byte[] macBytes = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            macBytes[i] = byte.Parse(cleaned.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // 6 bytes of 0xFF + 16 repetitions of macBytes = 102 bytes
        byte[] packet = new byte[102];
        for (int i = 0; i < 6; i++)
        {
            packet[i] = 0xFF;
        }

        for (int i = 0; i < 16; i++)
        {
            Buffer.BlockCopy(macBytes, 0, packet, 6 + (i * 6), 6);
        }

        return packet;
    }

    public static IPAddress CalculateSubnetBroadcast(IPAddress ip, IPAddress mask)
    {
        byte[] ipBytes = ip.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        if (ipBytes.Length != maskBytes.Length)
        {
            return IPAddress.Broadcast;
        }

        byte[] broadcastBytes = new byte[ipBytes.Length];
        for (int i = 0; i < ipBytes.Length; i++)
        {
            broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
        }
        return new IPAddress(broadcastBytes);
    }

    public static List<IPAddress> GetBroadcastAddresses(string? targetIp = null)
    {
        var broadcastAddresses = new HashSet<IPAddress> { IPAddress.Broadcast };

        // 1. Gather all active local IPv4 interface broadcasts
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var ipProps = ni.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        unicast.IPv4Mask != null &&
                        !unicast.Address.Equals(IPAddress.Any) &&
                        !unicast.Address.Equals(IPAddress.Loopback))
                    {
                        var bcast = CalculateSubnetBroadcast(unicast.Address, unicast.IPv4Mask);
                        broadcastAddresses.Add(bcast);
                    }
                }
            }
        }
        catch { }

        // 2. If target IP is specified, calculate directed broadcast for target IP
        if (!string.IsNullOrWhiteSpace(targetIp) && IPAddress.TryParse(targetIp, out var parsedIp) &&
            parsedIp.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = parsedIp.GetAddressBytes();
            // Default class C / typical /24 subnet broadcast (e.g., 192.168.1.255)
            bytes[3] = 255;
            broadcastAddresses.Add(new IPAddress(bytes));
        }

        return new List<IPAddress>(broadcastAddresses);
    }

    public static List<IPAddress> GetLocalIPv4Addresses()
    {
        var localIps = new List<IPAddress>();
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var ipProps = ni.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !unicast.Address.Equals(IPAddress.Any) &&
                        !unicast.Address.Equals(IPAddress.Loopback))
                    {
                        localIps.Add(unicast.Address);
                    }
                }
            }
        }
        catch { }

        return localIps;
    }

    public static async Task SendMagicPacketAsync(
        string macAddress,
        string? targetIp = null,
        string broadcastAddress = "255.255.255.255",
        int port = 9,
        int burstCount = 3)
    {
        byte[] packet = CreateMagicPacket(macAddress);

        // Collect all target broadcast endpoints
        var targets = GetBroadcastAddresses(targetIp);
        if (IPAddress.TryParse(broadcastAddress, out var explicitBcast) && !targets.Contains(explicitBcast))
        {
            targets.Add(explicitBcast);
        }

        var ports = (port == 9) ? new[] { 9, 7 } : new[] { port };
        var localIps = GetLocalIPv4Addresses();

        for (int burst = 0; burst < Math.Max(1, burstCount); burst++)
        {
            // A. Send via general broadcast client
            try
            {
                using var client = new UdpClient();
                client.EnableBroadcast = true;

                foreach (var bcast in targets)
                {
                    foreach (int p in ports)
                    {
                        try
                        {
                            var ep = new IPEndPoint(bcast, p);
                            await client.SendAsync(packet, packet.Length, ep);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // B. Send bound to each local active network card to guarantee physical adapter egress
            foreach (var localIp in localIps)
            {
                try
                {
                    using var boundClient = new UdpClient(new IPEndPoint(localIp, 0));
                    boundClient.EnableBroadcast = true;

                    foreach (var bcast in targets)
                    {
                        foreach (int p in ports)
                        {
                            try
                            {
                                var ep = new IPEndPoint(bcast, p);
                                await boundClient.SendAsync(packet, packet.Length, ep);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            if (burst < burstCount - 1)
            {
                await Task.Delay(40);
            }
        }
    }
}
