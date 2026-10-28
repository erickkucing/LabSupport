using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Agent.Services;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;
using Xunit;

namespace LabSupport.Tests;

public class DynamicDiscoveryAndPerformanceTests
{
    [Fact]
    public void ManagerBeacon_SerializationAndDeserialization_Matches()
    {
        var beacon = new ManagerBeaconMessage
        {
            ManagerId = "LAB-TEST-99",
            ManagerIp = "192.168.1.150",
            HttpPort = 8080,
            CommandPort = 5902,
            Timestamp = DateTime.UtcNow
        };

        string json = MessageSerializer.Serialize(beacon);
        Assert.Contains("ManagerBeacon", json);
        Assert.Contains("192.168.1.150", json);
        Assert.Contains("LAB-TEST-99", json);

        var deserialized = MessageSerializer.Deserialize<ManagerBeaconMessage>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("LAB-TEST-99", deserialized!.ManagerId);
        Assert.Equal("192.168.1.150", deserialized.ManagerIp);
        Assert.Equal(8080, deserialized.HttpPort);
    }

    [Fact]
    public void AgentDiscoveryClient_ProcessBeacon_UpdatesEndpointWhenChanged()
    {
        var client = new AgentDiscoveryClient("LAB-01", "192.168.1.10", 8080);
        string? discoveredIp = null;
        int discoveredPort = 0;

        client.OnManagerEndpointDiscovered += (ip, port) =>
        {
            discoveredIp = ip;
            discoveredPort = port;
        };

        var beacon = new ManagerBeaconMessage
        {
            ManagerId = "LAB-01",
            ManagerIp = "192.168.1.88",
            HttpPort = 8080
        };

        bool result = client.ProcessBeacon(beacon);

        Assert.True(result);
        Assert.Equal("192.168.1.88", client.CurrentManagerIp);
        Assert.Equal("192.168.1.88", discoveredIp);
        Assert.Equal(8080, discoveredPort);
    }

    [Fact]
    public void AgentDiscoveryClient_ProcessBeacon_RejectsMismatchedManagerId()
    {
        var client = new AgentDiscoveryClient("LAB-ALPHA", "192.168.1.10", 8080);
        bool fired = false;
        client.OnManagerEndpointDiscovered += (ip, port) => fired = true;

        var beacon = new ManagerBeaconMessage
        {
            ManagerId = "LAB-BETA", // Different lab pairing code
            ManagerIp = "192.168.1.99",
            HttpPort = 8080
        };

        bool result = client.ProcessBeacon(beacon);

        Assert.False(result);
        Assert.False(fired);
        Assert.Equal("192.168.1.10", client.CurrentManagerIp);
    }

    [Fact]
    public void AgentDiscoveryClient_SavesEndpointToAppSettings()
    {
        string tempConfig = Path.GetTempFileName();
        try
        {
            string initialJson = @"{
  ""Manager"": {
    ""Ip"": ""192.168.1.5"",
    ""Port"": 8080,
    ""ManagerId"": ""LAB-DEFAULT""
  }
}";
            File.WriteAllText(tempConfig, initialJson, Encoding.UTF8);

            var client = new AgentDiscoveryClient("LAB-DEFAULT", "192.168.1.5", 8080, configFilePath: tempConfig);
            bool saved = client.SaveEndpointToAppSettings("192.168.1.200", 8081);

            Assert.True(saved);
            string updated = File.ReadAllText(tempConfig);
            Assert.Contains("192.168.1.200", updated);
            Assert.Contains("8081", updated);
        }
        finally
        {
            if (File.Exists(tempConfig)) File.Delete(tempConfig);
        }
    }

    [Fact]
    public void HeartbeatService_UpdateManagerEndpoint_ChangesTargetUrl()
    {
        using var heartbeat = new HeartbeatService("127.0.0.1", 8080);
        Assert.Equal("127.0.0.1", heartbeat.ManagerIp);
        Assert.Equal(8080, heartbeat.ManagerPort);

        heartbeat.UpdateManagerEndpoint("192.168.1.75", 8088);
        Assert.Equal("192.168.1.75", heartbeat.ManagerIp);
        Assert.Equal(8088, heartbeat.ManagerPort);
    }

    [Fact]
    public async Task CommandListener_UpdateManagerEndpoint_InvokesCallbackAndReturnsSuccess()
    {
        using var listener = new CommandListener(5999, "LAB-SYNC");
        string? updatedIp = null;
        int updatedPort = 0;

        listener.OnUpdateManagerEndpointRequested += (ip, port) =>
        {
            updatedIp = ip;
            updatedPort = port;
        };

        var payload = new UpdateManagerEndpointPayload
        {
            ManagerIp = "10.0.0.50",
            ManagerPort = 8080
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.UpdateManagerEndpoint,
            ManagerId = "LAB-SYNC",
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await listener.ProcessCommandAsync(cmd);

        Assert.True(response.Success);
        Assert.Equal("10.0.0.50", updatedIp);
        Assert.Equal(8080, updatedPort);
    }

    [Fact]
    public async Task DatabaseService_WalMode_ExecutesConcurrentOperationsWithoutLockError()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"labsupport_test_wal_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(tempDb);
            await db.InitializeAsync();

            var tasks = new List<Task>();
            for (int i = 0; i < 30; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    var pc = new ComputerInfo
                    {
                        Hostname = $"LAB-PC-{index:D2}",
                        IpAddress = $"192.168.1.{100 + index}",
                        MacAddress = $"AA:BB:CC:DD:EE:{index:X2}",
                        LastSeen = DateTime.UtcNow
                    };
                    await db.UpsertComputerAsync(pc);
                }));
            }

            await Task.WhenAll(tasks);

            var all = await db.GetAllComputersAsync();
            Assert.Equal(30, all.Count);
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public async Task DatabaseService_Upsert_MatchesByMacAddress()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"labsupport_test_mac_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(tempDb);
            await db.InitializeAsync();

            // 1. Initial computer insertion
            var pc1 = new ComputerInfo
            {
                Hostname = "CLONE-PC-OLD",
                IpAddress = "192.168.1.101",
                MacAddress = "00:11:22:33:44:55",
                LastSeen = DateTime.UtcNow
            };
            await db.UpsertComputerAsync(pc1);

            // 2. Same hardware MAC, but hostname was renamed on student PC
            var pcRenamed = new ComputerInfo
            {
                Hostname = "LAB-PC-DESK-01",
                IpAddress = "192.168.1.101",
                MacAddress = "00:11:22:33:44:55",
                LastSeen = DateTime.UtcNow
            };
            await db.UpsertComputerAsync(pcRenamed);

            var list = await db.GetAllComputersAsync();
            Assert.Single(list);
            Assert.Equal("LAB-PC-DESK-01", list[0].Hostname);
            Assert.Equal("00:11:22:33:44:55", list[0].MacAddress);
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public void ThumbnailResizing_Bilinear_ProducesValidJpeg()
    {
        var screenCapture = new ScreenCaptureService();
        byte[] thumbJpeg = screenCapture.CaptureThumbnailJpeg(320, 180, 60);

        Assert.NotNull(thumbJpeg);
        Assert.True(thumbJpeg.Length > 0);
        // Standard JPEG SOI marker (0xFF, 0xD8)
        Assert.Equal(0xFF, thumbJpeg[0]);
        Assert.Equal(0xD8, thumbJpeg[1]);
    }

    [Fact]
    public void HttpFileServer_StartAndExposeBindingState_Works()
    {
        using var server = new HttpFileServer(8199);
        server.Start();

        Assert.True(server.IsRunning);
        Assert.NotNull(server.BoundPrefixes);
    }
}
