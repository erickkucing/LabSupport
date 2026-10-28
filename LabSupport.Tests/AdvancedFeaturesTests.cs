using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Agent.Services;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class AdvancedFeaturesTests : IDisposable
{
    private readonly int _testPort = 15910;
    private readonly CommandListener _listener;
    private readonly AgentCommandService _client;

    public AdvancedFeaturesTests()
    {
        _listener = new CommandListener(_testPort);
        _listener.Start();
        _client = new AgentCommandService(_testPort);
    }

    // Feature 1: Screen & Input Lock ("Perhatikan Guru")
    [Fact]
    public async Task Feature1_ScreenAndInputLock_ShouldLockAndUnlockSuccessfully()
    {
        var lockCmd = new CommandMessage
        {
            Type = CommandType.LockAttention,
            Payload = MessageSerializer.Serialize(new AttentionPayload
            {
                Lock = true,
                Message = "PERHATIKAN GURU - UJIAN SEDANG BERLANGSUNG"
            })
        };

        var lockResponse = await _client.SendCommandAsync("127.0.0.1", lockCmd, _testPort, timeoutMs: 8000);
        Assert.True(lockResponse.Success);
        Assert.Contains("locked", lockResponse.Output, StringComparison.OrdinalIgnoreCase);

        var unlockCmd = new CommandMessage
        {
            Type = CommandType.UnlockAttention
        };

        var unlockResponse = await _client.SendCommandAsync("127.0.0.1", unlockCmd, _testPort, timeoutMs: 8000);
        Assert.True(unlockResponse.Success);
        Assert.Contains("unlocked", unlockResponse.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Feature1_ScreenLockService_DirectStateTransitions()
    {
        using var service = new ScreenLockService();
        Assert.False(service.IsLocked);

        bool lockOk = service.Lock("UJIAN");
        Assert.True(lockOk);
        Assert.True(service.IsLocked);
        Assert.Equal("UJIAN", service.CurrentMessage);

        bool unlockOk = service.Unlock();
        Assert.True(unlockOk);
        Assert.False(service.IsLocked);
    }

    // Feature 2: Audio Control & Mute All (Pure WASAPI COM Interop)
    [Fact]
    public async Task Feature2_AudioControl_MuteAndVolumeCommands()
    {
        // 1. Mute
        var muteCmd = new CommandMessage
        {
            Type = CommandType.SetAudioMute,
            Payload = MessageSerializer.Serialize(new AudioPayload { Mute = true })
        };
        var muteResp = await _client.SendCommandAsync("127.0.0.1", muteCmd, _testPort, timeoutMs: 3000);
        Assert.True(muteResp.Success);

        // 2. Volume
        var volCmd = new CommandMessage
        {
            Type = CommandType.SetAudioVolume,
            Payload = MessageSerializer.Serialize(new AudioPayload { Volume = 42.0f })
        };
        var volResp = await _client.SendCommandAsync("127.0.0.1", volCmd, _testPort, timeoutMs: 3000);
        Assert.True(volResp.Success);

        // 3. Audio State query
        var stateCmd = new CommandMessage
        {
            Type = CommandType.GetAudioState
        };
        var stateResp = await _client.SendCommandAsync("127.0.0.1", stateCmd, _testPort, timeoutMs: 3000);
        Assert.True(stateResp.Success);
        Assert.NotNull(stateResp.Output);

        var state = MessageSerializer.Deserialize<AudioStateResult>(stateResp.Output);
        Assert.NotNull(state);
    }

    [Fact]
    public void Feature2_AudioService_DirectControl()
    {
        var audioService = new AudioService();

        bool muteOk = audioService.SetMute(true);
        Assert.True(muteOk);
        audioService.GetMute(out bool isMuted);
        Assert.True(isMuted);

        bool volOk = audioService.SetMasterVolume(65.0f);
        Assert.True(volOk);
        audioService.GetMasterVolume(out float vol);
        Assert.InRange(vol, 60.0f, 70.0f);

        var state = audioService.GetAudioState();
        Assert.NotNull(state);
    }

    // Feature 3: Exam Mode / Lockdown
    [Fact]
    public async Task Feature3_ExamLockdown_EnableAndDisableCommands()
    {
        var enableCmd = new CommandMessage
        {
            Type = CommandType.SetExamMode,
            Payload = MessageSerializer.Serialize(new ExamModePayload { Enabled = true })
        };
        var enableResp = await _client.SendCommandAsync("127.0.0.1", enableCmd, _testPort, timeoutMs: 3000);
        Assert.True(enableResp.Success);
        Assert.Contains("enabled", enableResp.Output, StringComparison.OrdinalIgnoreCase);

        var disableCmd = new CommandMessage
        {
            Type = CommandType.SetExamMode,
            Payload = MessageSerializer.Serialize(new ExamModePayload { Enabled = false })
        };
        var disableResp = await _client.SendCommandAsync("127.0.0.1", disableCmd, _testPort, timeoutMs: 3000);
        Assert.True(disableResp.Success);
        Assert.Contains("disabled", disableResp.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Feature3_ExamLockdownService_DirectStateTransitions()
    {
        using var examService = new ExamLockdownService();
        Assert.False(examService.IsExamModeActive);

        bool enabled = examService.EnableExamMode();
        Assert.True(enabled);
        Assert.True(examService.IsExamModeActive);

        bool disabled = examService.DisableExamMode();
        Assert.True(disabled);
        Assert.False(examService.IsExamModeActive);
    }

    // Feature 4: USB Storage Blocker
    [Fact]
    public async Task Feature4_UsbStorageBlocker_CommandToggle()
    {
        var blockCmd = new CommandMessage
        {
            Type = CommandType.SetUsbBlock,
            Payload = MessageSerializer.Serialize(new ToggleBlockPayload { Blocked = true })
        };
        var blockResp = await _client.SendCommandAsync("127.0.0.1", blockCmd, _testPort, timeoutMs: 3000);
        Assert.True(blockResp.Success);

        var unblockCmd = new CommandMessage
        {
            Type = CommandType.SetUsbBlock,
            Payload = MessageSerializer.Serialize(new ToggleBlockPayload { Blocked = false })
        };
        var unblockResp = await _client.SendCommandAsync("127.0.0.1", unblockCmd, _testPort, timeoutMs: 3000);
        Assert.True(unblockResp.Success);
    }

    [Fact]
    public void Feature4_UsbBlockerService_DirectToggle()
    {
        var usbService = new UsbBlockerService();
        bool blockResult = usbService.SetUsbStorageBlocked(true);
        Assert.True(blockResult);
        Assert.True(usbService.IsUsbStorageBlocked());

        bool unblockResult = usbService.SetUsbStorageBlocked(false);
        Assert.True(unblockResult);
        Assert.False(usbService.IsUsbStorageBlocked());
    }

    // Feature 5: Selective Internet Blocker
    [Fact]
    public async Task Feature5_SelectiveInternetBlocker_CommandToggle()
    {
        var blockCmd = new CommandMessage
        {
            Type = CommandType.SetInternetBlock,
            Payload = MessageSerializer.Serialize(new ToggleBlockPayload { Blocked = true })
        };
        var blockResp = await _client.SendCommandAsync("127.0.0.1", blockCmd, _testPort, timeoutMs: 3000);
        Assert.True(blockResp.Success);

        var unblockCmd = new CommandMessage
        {
            Type = CommandType.SetInternetBlock,
            Payload = MessageSerializer.Serialize(new ToggleBlockPayload { Blocked = false })
        };
        var unblockResp = await _client.SendCommandAsync("127.0.0.1", unblockCmd, _testPort, timeoutMs: 3000);
        Assert.True(unblockResp.Success);
    }

    [Fact]
    public void Feature5_FirewallBlockerService_PropertiesAndRules()
    {
        var fwService = new FirewallBlockerService();
        Assert.Equal("LabSupport_Block_Internet", FirewallBlockerService.RuleName);
        Assert.Contains("0.0.0.0-9.255.255.255", FirewallBlockerService.PublicRanges);
        Assert.Contains("192.169.0.0-255.255.255.255", FirewallBlockerService.PublicRanges);

        bool block = fwService.SetInternetBlocked(true);
        Assert.True(block);
        Assert.True(fwService.IsInternetBlocked());

        bool unblock = fwService.SetInternetBlocked(false);
        Assert.True(unblock);
        Assert.False(fwService.IsInternetBlocked());
    }

    // Feature 6: Launch Application & Website Broadcast
    [Fact]
    public async Task Feature6_LaunchProcess_ExecutesSuccessfully()
    {
        var launchCmd = new CommandMessage
        {
            Type = CommandType.LaunchProcess,
            Payload = MessageSerializer.Serialize(new LaunchPayload
            {
                Target = OperatingSystem.IsWindows() ? "cmd.exe" : "echo",
                Arguments = OperatingSystem.IsWindows() ? "/c exit 0" : "hello"
            })
        };

        var response = await _client.SendCommandAsync("127.0.0.1", launchCmd, _testPort, timeoutMs: 3000);
        Assert.True(response.Success);
        Assert.NotNull(response.Output);
    }

    // Feature 7: File Distribution & Collection
    [Fact]
    public async Task Feature7_CollectFiles_ReturnsFolderContents()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"lab_collect_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "tugas1.txt"), "Jawaban soal nomor 1");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "tugas2.txt"), "Jawaban soal nomor 2");

            var collectCmd = new CommandMessage
            {
                Type = CommandType.CollectFiles,
                Payload = MessageSerializer.Serialize(new CollectFilesPayload
                {
                    SourcePath = tempDir,
                    SearchPattern = "*.txt"
                })
            };

            var response = await _client.SendCommandAsync("127.0.0.1", collectCmd, _testPort, timeoutMs: 4000);
            Assert.True(response.Success);
            Assert.NotNull(response.Output);

            var collected = MessageSerializer.Deserialize<CollectFilesResponse>(response.Output);
            Assert.NotNull(collected);
            Assert.Equal(2, collected.Files.Count);

            var file1 = collected.Files.Find(f => f.RelativePath == "tugas1.txt");
            Assert.NotNull(file1);
            string content = Encoding.UTF8.GetString(Convert.FromBase64String(file1.ContentBase64));
            Assert.Equal("Jawaban soal nomor 1", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    // Feature 8: Student Screen Broadcast (MjpegStreamer /view endpoint)
    [Fact]
    public async Task Feature8_StudentScreenBroadcast_ServesViewHtml()
    {
        int streamPort = 15911;
        var captureService = new ScreenCaptureService();
        using var streamer = new MjpegStreamer(captureService, streamPort);
        streamer.Start();

        using var http = new HttpClient();
        var resp = await http.GetAsync($"http://127.0.0.1:{streamPort}/view");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        string html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("/stream", html);
        Assert.Contains("Student Screen Stream", html);
    }

    // Feature 9: Group Management
    [Fact]
    public async Task Feature9_GroupManagement_PersistsAndQueriesGroups()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"lab_groups_test_{Guid.NewGuid():N}.db");
        using var db = new DatabaseService(dbPath);

        try
        {
            await db.InitializeAsync();

            var pc1 = new ComputerInfo { Hostname = "PC-LAB-01", IpAddress = "192.168.1.11", GroupName = "Baris-1" };
            var pc2 = new ComputerInfo { Hostname = "PC-LAB-02", IpAddress = "192.168.1.12", GroupName = "Baris-2" };
            await db.UpsertComputerAsync(pc1);
            await db.UpsertComputerAsync(pc2);

            var all = await db.GetAllComputersAsync();
            Assert.Equal(2, all.Count);
            Assert.Equal("Baris-1", all.Find(c => c.Hostname == "PC-LAB-01")?.GroupName);
            Assert.Equal("Baris-2", all.Find(c => c.Hostname == "PC-LAB-02")?.GroupName);

            // Update group
            await db.UpdateComputerGroupAsync("PC-LAB-01", "Baris-VIP");
            var updated = await db.GetAllComputersAsync();
            Assert.Equal("Baris-VIP", updated.Find(c => c.Hostname == "PC-LAB-01")?.GroupName);

            var groups = await db.GetGroupsAsync();
            Assert.Contains("Baris-VIP", groups);
            Assert.Contains("Baris-2", groups);
        }
        finally
        {
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }

    // Feature 10: Activity Log & Telemetry Export to CSV
    [Fact]
    public async Task Feature10_CsvExport_ActivityLogsAndTelemetry()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"lab_csv_test_{Guid.NewGuid():N}.db");
        using var db = new DatabaseService(dbPath);

        try
        {
            await db.InitializeAsync();

            await db.LogActivityAsync("PC-01", "LockAttention", "Screen locked: \"Perhatikan Guru\", sukses", true);
            await db.LogActivityAsync("PC-02", "BlockUSB", "USB blocked", true);

            string csvLogs = await db.ExportActivityLogsToCsvStringAsync();
            Assert.Contains("ID,TargetHost,Action,Result,IsSuccess,Timestamp", csvLogs);
            Assert.Contains("\"PC-01\"", csvLogs);
            Assert.Contains("\"Screen locked: \"\"Perhatikan Guru\"\", sukses\"", csvLogs);

            var computers = new List<ComputerInfo>
            {
                new ComputerInfo
                {
                    Hostname = "PC-01",
                    IpAddress = "192.168.1.101",
                    MacAddress = "AA:BB:CC:11:22:33",
                    OsVersion = "Windows 11",
                    GroupName = "Baris-A",
                    CpuUsage = 23.5,
                    RamUsageMb = 2048,
                    RamTotalMb = 8192,
                    DiskFreeGb = 120.5,
                    IsOnline = true
                }
            };

            string csvTelemetry = DatabaseService.ExportTelemetryToCsvString(computers);
            Assert.Contains("Hostname,IpAddress,MacAddress,OsVersion,Group,CpuUsage,RamUsageMb,RamTotalMb,DiskFreeGb,IsOnline,LastSeen", csvTelemetry);
            Assert.Contains("\"PC-01\",\"192.168.1.101\"", csvTelemetry);
            Assert.Contains("\"Baris-A\"", csvTelemetry);
            Assert.Contains("23.5,2048,8192,120.5,True", csvTelemetry);
        }
        finally
        {
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public void Feature10_CsvEscaping_HandlesQuotesAndCommas()
    {
        string regular = DatabaseService.EscapeCsv("SimpleText");
        Assert.Equal("\"SimpleText\"", regular);

        string withComma = DatabaseService.EscapeCsv("Hello, World");
        Assert.Equal("\"Hello, World\"", withComma);

        string withQuotes = DatabaseService.EscapeCsv("Say \"Hello\"");
        Assert.Equal("\"Say \"\"Hello\"\"\"", withQuotes);

        string empty = DatabaseService.EscapeCsv(null);
        Assert.Equal("\"\"", empty);
    }

    [Fact]
    public void Feature1_ScreenLockService_ConsecutiveLockCycles()
    {
        using var service = new ScreenLockService();
        Assert.False(service.IsLocked);

        // Cycle 1
        bool lock1 = service.Lock("SESI 1");
        Assert.True(lock1);
        Assert.True(service.IsLocked);
        Assert.Equal("SESI 1", service.CurrentMessage);

        bool unlock1 = service.Unlock();
        Assert.True(unlock1);
        Assert.False(service.IsLocked);

        // Cycle 2 (validates message loop thread restart and no thread leak)
        bool lock2 = service.Lock("SESI 2");
        Assert.True(lock2);
        Assert.True(service.IsLocked);
        Assert.Equal("SESI 2", service.CurrentMessage);

        bool unlock2 = service.Unlock();
        Assert.True(unlock2);
        Assert.False(service.IsLocked);
    }

    [Fact]
    public async Task Feature7_CollectFiles_ReadsFilesLockedByOtherProcesses()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"lab_locked_collect_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string lockedFilePath = Path.Combine(tempDir, "document_sedang_diedit.docx");

        try
        {
            // Simulate Microsoft Word / VSCode holding an open write lock with FileShare.ReadWrite
            using var activeEditorStream = new FileStream(
                lockedFilePath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.ReadWrite);

            byte[] sampleContent = Encoding.UTF8.GetBytes("Data tugas siswa yang sedang aktif diketik di Word");
            await activeEditorStream.WriteAsync(sampleContent);
            await activeEditorStream.FlushAsync();

            var collectCmd = new CommandMessage
            {
                Type = CommandType.CollectFiles,
                Payload = MessageSerializer.Serialize(new CollectFilesPayload
                {
                    SourcePath = tempDir,
                    SearchPattern = "*.docx"
                })
            };

            var response = await _client.SendCommandAsync("127.0.0.1", collectCmd, _testPort, timeoutMs: 4000);
            Assert.True(response.Success);
            Assert.NotNull(response.Output);

            var collected = MessageSerializer.Deserialize<CollectFilesResponse>(response.Output);
            Assert.NotNull(collected);
            Assert.Single(collected.Files);

            var item = collected.Files[0];
            Assert.Equal("document_sedang_diedit.docx", item.RelativePath);
            string readContent = Encoding.UTF8.GetString(Convert.FromBase64String(item.ContentBase64));
            Assert.Equal("Data tugas siswa yang sedang aktif diketik di Word", readContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void Feature10_CsvExport_CultureInvariantNumbers()
    {
        var prevCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Set culture to Indonesian (id-ID) which uses comma as decimal separator
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("id-ID");

            var computers = new List<ComputerInfo>
            {
                new ComputerInfo
                {
                    Hostname = "PC-ID-01",
                    IpAddress = "192.168.1.50",
                    GroupName = "Lab-1",
                    CpuUsage = 78.4,
                    RamUsageMb = 3500.2,
                    RamTotalMb = 8192,
                    DiskFreeGb = 150.8,
                    IsOnline = true
                }
            };

            string csv = DatabaseService.ExportTelemetryToCsvString(computers);
            // Must contain dots (78.4, 150.8) and NOT commas (78,4), preserving CSV column count
            Assert.Contains("78.4,3500,8192,150.8,True", csv);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = prevCulture;
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
