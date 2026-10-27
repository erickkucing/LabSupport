using System;
using System.Globalization;
using System.Net;
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

    public static async Task SendMagicPacketAsync(string macAddress, string broadcastAddress = "255.255.255.255", int port = 9)
    {
        byte[] packet = CreateMagicPacket(macAddress);
        using var client = new UdpClient();
        client.EnableBroadcast = true;
        var endPoint = new IPEndPoint(IPAddress.Parse(broadcastAddress), port);
        await client.SendAsync(packet, packet.Length, endPoint);
    }
}
