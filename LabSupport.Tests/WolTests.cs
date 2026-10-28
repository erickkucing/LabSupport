using System;
using Xunit;
using LabSupport.Shared.Protocol;

namespace LabSupport.Tests;

public class WolTests
{
    [Theory]
    [InlineData("00:11:22:33:44:55")]
    [InlineData("00-11-22-33-44-55")]
    [InlineData("001122334455")]
    [InlineData("aa:bb:cc:dd:ee:ff")]
    public void CreateMagicPacket_ShouldGenerate102BytesWithCorrectHeaderAndPayload(string mac)
    {
        byte[] packet = WolHelper.CreateMagicPacket(mac);

        Assert.Equal(102, packet.Length);

        // First 6 bytes must be 0xFF
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(0xFF, packet[i]);
        }

        // Following 16 blocks must repeat the MAC bytes
        byte[] expectedMac = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };
        if (mac.StartsWith("aa", StringComparison.OrdinalIgnoreCase))
        {
            expectedMac = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        }

        for (int rep = 0; rep < 16; rep++)
        {
            for (int b = 0; b < 6; b++)
            {
                Assert.Equal(expectedMac[b], packet[6 + (rep * 6) + b]);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-mac")]
    [InlineData("00:11:22:33")]
    public void CreateMagicPacket_ShouldThrowOnInvalidMac(string invalidMac)
    {
        Assert.ThrowsAny<ArgumentException>(() => WolHelper.CreateMagicPacket(invalidMac));
    }

    [Theory]
    [InlineData("192.168.1.100", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.0.5.20", "255.255.0.0", "10.0.255.255")]
    [InlineData("172.16.10.5", "255.255.240.0", "172.16.15.255")]
    public void CalculateSubnetBroadcast_CalculatesCorrectly(string ip, string mask, string expectedBroadcast)
    {
        var broadcast = WolHelper.CalculateSubnetBroadcast(
            System.Net.IPAddress.Parse(ip),
            System.Net.IPAddress.Parse(mask));

        Assert.Equal(expectedBroadcast, broadcast.ToString());
    }

    [Fact]
    public void GetBroadcastAddresses_IncludesGlobalAndDirectedBroadcast()
    {
        var list = WolHelper.GetBroadcastAddresses("192.168.5.50");

        Assert.Contains(System.Net.IPAddress.Broadcast, list);
        Assert.Contains(System.Net.IPAddress.Parse("192.168.5.255"), list);
    }

    [Fact]
    public void ScriptGenerator_Includes_HiberbootAndWakeOnLanCommands()
    {
        string script = LabSupport.Shared.Services.ScriptGenerator.GenerateSingleBatchInstaller("192.168.1.10", 8080, "LAB-TEST");

        Assert.Contains("HiberbootEnabled", script);
        Assert.Contains("WakeOnMagicPacket", script);
        Assert.Contains("LabSupport WoL Inbound", script);
        Assert.Contains("Enable-NetAdapterPowerManagement", script);
    }

    [Fact]
    public async System.Threading.Tasks.Task WolService_WakeComputerAsync_PassesTargetIpAndLogs()
    {
        string testDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wol_test_{Guid.NewGuid():N}.db");
        using var db = new LabSupport.Shared.Services.DatabaseService(testDbPath);
        await db.InitializeAsync();

        var wolService = new LabSupport.Shared.Services.WolService(db);
        bool success = await wolService.WakeComputerAsync("TEST-PC-01", "00:11:22:33:44:55", "127.0.0.1");

        Assert.True(success);

        var logs = await db.GetActivityLogsAsync(10);
        Assert.NotEmpty(logs);
        var log = logs.First();
        Assert.Equal("TEST-PC-01", log.TargetHost);
        Assert.Equal("Wake-on-LAN", log.Action);
        Assert.True(log.IsSuccess);

        try { if (System.IO.File.Exists(testDbPath)) System.IO.File.Delete(testDbPath); } catch { }
    }
}
