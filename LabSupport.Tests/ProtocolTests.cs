using System;
using Xunit;
using LabSupport.Shared.Protocol;

namespace LabSupport.Tests;

public class ProtocolTests
{
    [Fact]
    public void HeartbeatMessage_ShouldSerializeAndDeserializeCorrectly()
    {
        var original = new HeartbeatMessage
        {
            Hostname = "LAB-PC-01",
            IpAddress = "192.168.1.50",
            MacAddress = "00:1A:2B:3C:4D:5E",
            OsVersion = "Windows 11 Pro",
            CpuUsage = 24.5,
            RamUsageMb = 4096,
            RamTotalMb = 16384,
            DiskFreeGb = 120.4,
            ScreenshotBase64 = "dGVzdC1pbWFnZQ=="
        };

        string json = MessageSerializer.Serialize(original);
        Assert.NotNull(json);
        Assert.Contains("LAB-PC-01", json);

        var deserialized = MessageSerializer.Deserialize<HeartbeatMessage>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(original.Hostname, deserialized.Hostname);
        Assert.Equal(original.IpAddress, deserialized.IpAddress);
        Assert.Equal(original.MacAddress, deserialized.MacAddress);
        Assert.Equal(original.CpuUsage, deserialized.CpuUsage);
        Assert.Equal(original.RamUsageMb, deserialized.RamUsageMb);
        Assert.Equal(original.RamTotalMb, deserialized.RamTotalMb);
        Assert.Equal(original.DiskFreeGb, deserialized.DiskFreeGb);
        Assert.Equal(original.ScreenshotBase64, deserialized.ScreenshotBase64);
    }

    [Fact]
    public void CommandMessage_ShouldSerializeAndDeserializeCorrectly()
    {
        var original = new CommandMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = CommandType.Shutdown,
            Payload = "Shutdown message payload",
            Timestamp = DateTime.UtcNow
        };

        string json = MessageSerializer.Serialize(original);
        Assert.Contains("Shutdown", json);

        var deserialized = MessageSerializer.Deserialize<CommandMessage>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Payload, deserialized.Payload);
    }

    [Fact]
    public void ScriptPayload_ShouldSerializeAndDeserializeCorrectly()
    {
        var original = new ScriptPayload
        {
            Type = ScriptType.PowerShell,
            ScriptContent = "Write-Host 'Hello Lab'",
            TimeoutSeconds = 45
        };

        string json = MessageSerializer.Serialize(original);
        var deserialized = MessageSerializer.Deserialize<ScriptPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.ScriptContent, deserialized.ScriptContent);
        Assert.Equal(original.TimeoutSeconds, deserialized.TimeoutSeconds);
    }

    [Fact]
    public void CommandResponse_ShouldSerializeAndDeserializeCorrectly()
    {
        var original = new CommandResponse
        {
            CommandId = "cmd-12345",
            Success = true,
            Output = "Command completed successfully",
            Error = null,
            ExecutionTimeMs = 150
        };

        string json = MessageSerializer.Serialize(original);
        var deserialized = MessageSerializer.Deserialize<CommandResponse>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.CommandId, deserialized.CommandId);
        Assert.True(deserialized.Success);
        Assert.Equal(original.Output, deserialized.Output);
        Assert.Null(deserialized.Error);
        Assert.Equal(150, deserialized.ExecutionTimeMs);
    }
}
