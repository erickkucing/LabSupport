using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

public class AgentLifecycleAndUninstallTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly DatabaseService _dbService;

    public AgentLifecycleAndUninstallTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"labsupport_uninst_{Guid.NewGuid():N}.db");
        _dbService = new DatabaseService(_tempDbPath);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        try { if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath); } catch { }
    }

    [Fact]
    public async Task DatabaseService_PendingDeletions_AddCheckAndRemove_Works()
    {
        // Arrange
        await _dbService.InitializeAsync();
        string hostname = "PC-LAB-OFFLINE-01";
        string mac = "11:22:33:44:55:66";

        // Act & Assert 1: Belum ada
        bool initiallyPending = await _dbService.IsPendingDeletionAsync(hostname, mac);
        Assert.False(initiallyPending);

        // Act & Assert 2: Tambah ke pending deletions
        await _dbService.AddPendingDeletionAsync(hostname, mac, "LAB-TEST");
        bool isPending = await _dbService.IsPendingDeletionAsync(hostname, mac);
        Assert.True(isPending);

        // Bisa dicari lewat hostname saja atau MAC saja
        Assert.True(await _dbService.IsPendingDeletionAsync(hostname, ""));
        Assert.True(await _dbService.IsPendingDeletionAsync("DIFFERENT-NAME", mac));

        // Act & Assert 3: Hapus dari pending deletions
        await _dbService.RemovePendingDeletionAsync(hostname, mac);
        bool stillPending = await _dbService.IsPendingDeletionAsync(hostname, mac);
        Assert.False(stillPending);
    }

    [Fact]
    public async Task DatabaseService_DeleteComputer_RemovesFromTableAndKeepsLogs()
    {
        // Arrange
        await _dbService.InitializeAsync();
        var pc = new ComputerInfo
        {
            Hostname = "PC-DELETE-TEST",
            IpAddress = "192.168.1.50",
            MacAddress = "00:AA:BB:CC:DD:EE"
        };
        await _dbService.UpsertComputerAsync(pc);
        await _dbService.LogActivityAsync("PC-DELETE-TEST", "Session Start", "User logged in", true);

        // Act: Hapus komputer
        await _dbService.DeleteComputerAsync("PC-DELETE-TEST", "00:AA:BB:CC:DD:EE");
        var computers = await _dbService.GetAllComputersAsync();
        var logs = await _dbService.GetActivityLogsAsync();

        // Assert
        Assert.DoesNotContain(computers, c => c.Hostname == "PC-DELETE-TEST");
        Assert.Contains(logs, l => l.TargetHost == "PC-DELETE-TEST"); // Riwayat audit tetap ada
    }

    [Fact]
    public async Task HttpFileServer_UnregisterEndpoint_TriggersEvent()
    {
        // Arrange
        int port = 9085;
        using var server = new HttpFileServer(port);
        server.Start();

        UnregisterAgentMessage? receivedMsg = null;
        using var evt = new ManualResetEventSlim(false);

        server.OnAgentUnregisterReceived += msg =>
        {
            receivedMsg = msg;
            evt.Set();
        };

        var unreg = new UnregisterAgentMessage
        {
            Hostname = "CLIENT-UNREG-01",
            MacAddress = "AA:BB:CC:11:22:33",
            ManagerId = "LAB-DEFAULT",
            Reason = "Manual test unregister"
        };

        using var client = new HttpClient();
        string json = MessageSerializer.Serialize(unreg);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var resp = await client.PostAsync($"http://127.0.0.1:{port}/agent/unregister", content);

        // Assert
        Assert.True(resp.IsSuccessStatusCode);
        bool signaled = evt.Wait(4000);
        Assert.True(signaled);
        Assert.NotNull(receivedMsg);
        Assert.Equal("CLIENT-UNREG-01", receivedMsg.Hostname);
        Assert.Equal("AA:BB:CC:11:22:33", receivedMsg.MacAddress);
    }

    [Fact]
    public async Task HttpFileServer_Heartbeat_ReturnsUninstallDirectiveWhenPending()
    {
        // Arrange
        int port = 9086;
        using var server = new HttpFileServer(port);
        server.HeartbeatDirectiveEvaluator = hb =>
        {
            if (hb.Hostname == "PC-SHOULD-UNINSTALL") return "uninstall";
            return null;
        };
        server.Start();

        var hbMsg = new HeartbeatMessage
        {
            Hostname = "PC-SHOULD-UNINSTALL",
            IpAddress = "127.0.0.1",
            MacAddress = "99:88:77:66:55:44"
        };

        using var client = new HttpClient();
        string json = MessageSerializer.Serialize(hbMsg);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var resp = await client.PostAsync($"http://127.0.0.1:{port}/agent/heartbeat", content);
        string respBody = await resp.Content.ReadAsStringAsync();
        var respObj = MessageSerializer.Deserialize<HeartbeatResponsePayload>(respBody);

        // Assert
        Assert.True(resp.IsSuccessStatusCode);
        Assert.NotNull(respObj);
        Assert.Equal("uninstall", respObj.Directive);
    }

    [Fact]
    public async Task CommandListener_UninstallAgentCommand_ReturnsSuccessAndFiresEvent()
    {
        // Arrange
        using var listener = new CommandListener(5988, "LAB-TEST");
        listener.Start();

        bool eventFired = false;
        listener.OnUninstallRequested += () => { eventFired = true; };

        var cmd = new CommandMessage
        {
            Id = Guid.NewGuid().ToString(),
            Type = CommandType.UninstallAgent,
            ManagerId = "LAB-TEST"
        };

        var client = new AgentCommandService(5988);

        // Act
        var result = await client.SendCommandAsync("127.0.0.1", cmd, timeoutMs: 3000);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Agent uninstall initiated.", result.Output);
        Assert.True(eventFired);
    }

    [Fact]
    public void ScriptGenerator_UninstallBatchScript_ContainsCleanRemovalCommands()
    {
        // Act
        string bat = ScriptGenerator.GenerateUninstallBatchScript("192.168.1.100", 8080, "LAB-RPL", "LabSupportAgent", @"C:\ProgramData\LabSupport");

        // Assert
        Assert.Contains("title LabSupport Agent Uninstaller", bat);
        Assert.Contains("/agent/unregister", bat);
        Assert.Contains("sc.exe stop \"LabSupportAgent\"", bat);
        Assert.Contains("sc.exe delete \"LabSupportAgent\"", bat);
        Assert.Contains("schtasks.exe /delete /tn \"LabSupportAgent\" /f", bat);
        Assert.Contains("netsh advfirewall firewall delete rule", bat);
        Assert.Contains("rmdir /s /q \"C:\\ProgramData\\LabSupport\"", bat);
    }

    [Fact]
    public void ScriptGenerator_InstallerContainsEmbeddedUninstallBatCreation()
    {
        // Act
        string bat = ScriptGenerator.GenerateSingleBatchInstaller("192.168.1.100", 8080, "LAB-DEFAULT");

        // Assert: Pastikan di dalam deployment 1 skrip bat, file uninstall_agent.bat ikut dipasang
        Assert.Contains("uninstall_agent.bat", bat);
        Assert.Contains("/agent/unregister", bat);
    }

    [Fact]
    public async Task DatabaseService_DeleteComputer_RemovesTargetComputer_PreservesOthers()
    {
        // Arrange
        await _dbService.InitializeAsync();
        var pc1 = new ComputerInfo { Hostname = "STUDENT-PC-01", IpAddress = "192.168.1.20", MacAddress = "AA:BB:CC:01" };
        var pc2 = new ComputerInfo { Hostname = "STUDENT-PC-02", IpAddress = "192.168.1.21", MacAddress = "AA:BB:CC:02" };
        await _dbService.UpsertComputerAsync(pc1);
        await _dbService.UpsertComputerAsync(pc2);

        // Act: Hapus PC-01
        await _dbService.DeleteComputerAsync(pc1.Hostname, pc1.MacAddress);
        var remaining = await _dbService.GetAllComputersAsync();

        // Assert
        Assert.DoesNotContain(remaining, c => c.Hostname == "STUDENT-PC-01");
        Assert.Contains(remaining, c => c.Hostname == "STUDENT-PC-02");
    }

    [Fact]
    public async Task PendingDeletion_Lifecycle_WithHeartbeatEvaluator()
    {
        // Arrange
        await _dbService.InitializeAsync();
        string hostname = "OFFLINE-PC-02";
        string mac = "AA:BB:CC:02";

        // Step 1: Admin deletes offline PC -> added to pending deletions
        await _dbService.AddPendingDeletionAsync(hostname, mac, "LAB-DEFAULT");
        Assert.True(await _dbService.IsPendingDeletionAsync(hostname, mac));

        // Step 2: PC wakes up later and sends heartbeat -> evaluator triggers uninstall directive
        var hb = new HeartbeatMessage { Hostname = hostname, MacAddress = mac };
        string? directive = null;
        if (await _dbService.IsPendingDeletionAsync(hb.Hostname, hb.MacAddress))
        {
            await _dbService.RemovePendingDeletionAsync(hb.Hostname, hb.MacAddress);
            directive = "uninstall";
        }

        // Assert
        Assert.Equal("uninstall", directive);
        Assert.False(await _dbService.IsPendingDeletionAsync(hostname, mac));
    }
}
