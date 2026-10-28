using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Agent.Services;
using LabSupport.Shared.Services;
using LabSupport.Shared.Protocol;

namespace LabSupport.Tests;

public class NotificationAndConnectionCheckTests : IDisposable
{
    private readonly int _testPort = 15904;
    private readonly CommandListener _listener;
    private readonly AgentCommandService _client;

    public NotificationAndConnectionCheckTests()
    {
        _listener = new CommandListener(_testPort);
        _listener.Start();

        _client = new AgentCommandService(_testPort);
    }

    [Fact]
    public void NotificationPayload_Serialization_RoundTrip()
    {
        var payload = new NotificationPayload
        {
            Title = "Status Koneksi Lab",
            Message = "PC terverifikasi terhubung ke Manager.",
            Type = NotificationType.ConnectionCheck,
            DurationSeconds = 4,
            ShowHostInfo = true,
            Hostname = "LAB-PC-01",
            IpAddress = "192.168.1.50",
            RoomName = "Lab Komputer A",
            ManagerHost = "192.168.1.10"
        };

        string json = MessageSerializer.Serialize(payload);
        Assert.False(string.IsNullOrWhiteSpace(json));

        var deserialized = MessageSerializer.Deserialize<NotificationPayload>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("Status Koneksi Lab", deserialized.Title);
        Assert.Equal("PC terverifikasi terhubung ke Manager.", deserialized.Message);
        Assert.Equal(NotificationType.ConnectionCheck, deserialized.Type);
        Assert.Equal(4, deserialized.DurationSeconds);
        Assert.True(deserialized.ShowHostInfo);
        Assert.Equal("LAB-PC-01", deserialized.Hostname);
        Assert.Equal("192.168.1.50", deserialized.IpAddress);
        Assert.Equal("Lab Komputer A", deserialized.RoomName);
        Assert.Equal("192.168.1.10", deserialized.ManagerHost);
    }

    [Fact]
    public void NotificationPayload_Defaults_AreValid()
    {
        var payload = new NotificationPayload();
        Assert.Equal(NotificationType.Info, payload.Type);
        Assert.Equal(5, payload.DurationSeconds);
        Assert.False(payload.ShowHostInfo);
        Assert.Equal(string.Empty, payload.Title);
        Assert.Equal(string.Empty, payload.Message);
    }

    [Theory]
    [InlineData(NotificationType.Info)]
    [InlineData(NotificationType.Warning)]
    [InlineData(NotificationType.Urgent)]
    [InlineData(NotificationType.ConnectionCheck)]
    public void NotificationType_AllValues_AreSerializable(NotificationType type)
    {
        var payload = new NotificationPayload
        {
            Title = "Test Title",
            Message = "Test Message",
            Type = type,
            DurationSeconds = 3
        };

        string json = MessageSerializer.Serialize(payload);
        var result = MessageSerializer.Deserialize<NotificationPayload>(json);
        Assert.NotNull(result);
        Assert.Equal(type, result.Type);
    }

    [Fact]
    public async Task ShowNotification_CommandListener_ShouldReturnSuccess()
    {
        var payload = new NotificationPayload
        {
            Title = "Pengumuman Praktikum",
            Message = "Waktu praktikum tersisa 10 menit. Segera simpan pekerjaan Anda.",
            Type = NotificationType.Warning,
            DurationSeconds = 3,
            ShowHostInfo = false
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.ShowNotification,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _client.SendCommandAsync("127.0.0.1", cmd, _testPort, timeoutMs: 3000);

        Assert.True(response.Success);
        Assert.NotNull(response.Output);
        Assert.Contains("Notification", response.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShowNotification_ConnectionCheck_ShouldIncludeHostInfo()
    {
        var payload = new NotificationPayload
        {
            Title = "LabSupport Agent Terhubung",
            Message = "PC terverifikasi terpasang dan terhubung dengan PC Manager.",
            Type = NotificationType.ConnectionCheck,
            DurationSeconds = 2,
            ShowHostInfo = true,
            ManagerHost = "192.168.1.10"
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.ShowNotification,
            Payload = MessageSerializer.Serialize(payload)
        };

        var response = await _client.SendCommandAsync("127.0.0.1", cmd, _testPort, timeoutMs: 3000);

        Assert.True(response.Success);
        Assert.NotNull(response.Output);
    }

    [Fact]
    public async Task ShowMessage_LegacyString_ShouldReturnSuccess()
    {
        var cmd = new CommandMessage
        {
            Type = CommandType.ShowMessage,
            Payload = "Halo siswa lab!"
        };

        var response = await _client.SendCommandAsync("127.0.0.1", cmd, _testPort, timeoutMs: 3000);

        Assert.True(response.Success);
        Assert.NotNull(response.Output);
    }

    [Fact]
    public void NotificationOverlayService_Lifecycle_ShowAndClose_Directly()
    {
        using var service = new NotificationOverlayService();
        var payload = new NotificationPayload
        {
            Title = "Unit Test Notification",
            Message = "Pengujian overlay service langsung.",
            Type = NotificationType.Info,
            DurationSeconds = 1,
            ShowHostInfo = true
        };

        bool ok = service.ShowNotification(payload);
        Assert.True(ok);

        // Explicitly close
        service.Close();
    }

    [Fact]
    public async Task DatabaseLogging_NotificationAndConnectionCheck_ShouldPersistInDb()
    {
        string tempDbPath = Path.Combine(Path.GetTempPath(), $"test_notif_log_{Guid.NewGuid():N}.db");
        try
        {
            var db = new DatabaseService(tempDbPath);
            await db.InitializeAsync();

            await db.LogActivityAsync("PC-STUDENT-01", "Notification Sent", "Warning: Waktu sisa 10 menit", true);
            await db.LogActivityAsync("PC-STUDENT-01", "Connection Check", "Agent verified connected (12ms)", true);

            var logs = await db.GetActivityLogsAsync(10);
            Assert.NotNull(logs);
            Assert.True(logs.Count >= 2);

            var notifLog = logs.FirstOrDefault(l => l.Action == "Notification Sent");
            Assert.NotNull(notifLog);
            Assert.Contains("Warning", notifLog.Result);

            var checkLog = logs.FirstOrDefault(l => l.Action == "Connection Check");
            Assert.NotNull(checkLog);
            Assert.Contains("12ms", checkLog.Result);
        }
        finally
        {
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { }
            }
        }
    }

    public void Dispose()
    {
        _listener.Stop();
    }
}
