using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Agent.Services;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class WorkspaceCleanerAndTelemetryTests
{
    [Fact]
    public void CleanDirectory_PreservesShortcuts_DeletesOtherFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"cleaner_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string lnk1 = Path.Combine(tempDir, "Visual Studio Code.lnk");
            string lnk2 = Path.Combine(tempDir, "Google Chrome.LNK");
            string docx = Path.Combine(tempDir, "Laporan_Tugas.docx");
            string zip = Path.Combine(tempDir, "Archive.zip");
            string txt = Path.Combine(tempDir, "notes.txt");
            string subDir = Path.Combine(tempDir, "Projects");
            Directory.CreateDirectory(subDir);
            string subFile = Path.Combine(subDir, "main.py");

            File.WriteAllText(lnk1, "fake shortcut 1");
            File.WriteAllText(lnk2, "fake shortcut 2");
            File.WriteAllText(docx, new string('A', 2048));
            File.WriteAllText(zip, new string('B', 4096));
            File.WriteAllText(txt, "test note");
            File.WriteAllText(subFile, "print('hello')");

            int deletedCount = 0;
            long bytesFreed = 0;

            WorkspaceCleanerService.CleanDirectory(tempDir, preserveShortcuts: true, ref deletedCount, ref bytesFreed);

            // Shortcuts MUST be preserved
            Assert.True(File.Exists(lnk1), "Visual Studio Code.lnk should NOT be deleted!");
            Assert.True(File.Exists(lnk2), "Google Chrome.LNK should NOT be deleted!");

            // Other files must be deleted
            Assert.False(File.Exists(docx), "Laporan_Tugas.docx should be deleted.");
            Assert.False(File.Exists(zip), "Archive.zip should be deleted.");
            Assert.False(File.Exists(txt), "notes.txt should be deleted.");
            Assert.False(Directory.Exists(subDir), "Subdirectory Projects should be cleaned and deleted.");

            Assert.True(deletedCount >= 4, $"Expected at least 4 files deleted, got {deletedCount}");
            Assert.True(bytesFreed > 6000, $"Expected > 6000 bytes freed, got {bytesFreed}");
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
    public void CleanDirectory_DeletesAllFiles_WhenPreserveShortcutsFalse()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"cleaner_all_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string lnk = Path.Combine(tempDir, "shortcut.lnk");
            string exe = Path.Combine(tempDir, "installer.exe");
            File.WriteAllText(lnk, "shortcut data");
            File.WriteAllText(exe, "dummy installer");

            int count = 0;
            long bytes = 0;

            WorkspaceCleanerService.CleanDirectory(tempDir, preserveShortcuts: false, ref count, ref bytes);

            Assert.False(File.Exists(lnk), "Shortcut should be deleted when preserveShortcuts is false.");
            Assert.False(File.Exists(exe), "Installer should be deleted.");
            Assert.Equal(2, count);
            Assert.True(bytes > 0);
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
    public void CleanWorkspacePayload_And_Result_Serialization()
    {
        var payload = new CleanWorkspacePayload
        {
            CleanDownloads = true,
            CleanTemp = true,
            CleanRecycleBin = false,
            CleanDesktopDocs = true,
            CleanSsh = true,
            CleanVirtualBoxVms = true
        };

        string json = MessageSerializer.Serialize(payload);
        var deserialized = MessageSerializer.Deserialize<CleanWorkspacePayload>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.CleanDownloads);
        Assert.True(deserialized.CleanTemp);
        Assert.False(deserialized.CleanRecycleBin);
        Assert.True(deserialized.CleanDesktopDocs);
        Assert.True(deserialized.CleanSsh);
        Assert.True(deserialized.CleanVirtualBoxVms);

        var result = new CleanWorkspaceResult
        {
            Success = true,
            FilesDeletedCount = 42,
            BytesFreed = 104857600, // 100 MB
            SummaryText = "Pembersihan selesai: 42 file dihapus (100 MB dipulihkan)."
        };

        string resJson = MessageSerializer.Serialize(result);
        var desResult = MessageSerializer.Deserialize<CleanWorkspaceResult>(resJson);

        Assert.NotNull(desResult);
        Assert.True(desResult.Success);
        Assert.Equal(42, desResult.FilesDeletedCount);
        Assert.Equal(104857600, desResult.BytesFreed);
        Assert.Contains("42 file dihapus", desResult.SummaryText);
    }

    [Fact]
    public void ComputerInfo_NetworkSpeedFormatting_And_BandwidthHogDetection()
    {
        var comp = new ComputerInfo
        {
            Hostname = "PC-LAB-01",
            NetworkDownloadKbps = 250.0,
            NetworkUploadKbps = 45.0
        };

        Assert.Equal("⬇ 250 KB/s  ⬆ 45 KB/s", comp.NetworkFormatted);
        Assert.False(comp.IsBandwidthHog);

        comp.NetworkDownloadKbps = 4500.5; // ~4.4 MB/s (Bandwidth Hog > 3000 KB/s)
        comp.NetworkUploadKbps = 1200.0; // ~1.2 MB/s

        Assert.Equal("⬇ 4.4 MB/s  ⬆ 1.2 MB/s", comp.NetworkFormatted);
        Assert.True(comp.IsBandwidthHog);
    }

    [Fact]
    public void ComputerInfo_IdleFormatting_And_InactivityDetection()
    {
        var comp = new ComputerInfo
        {
            Hostname = "PC-LAB-02",
            IdleSeconds = 25
        };

        Assert.Equal("Aktif", comp.IdleFormatted);
        Assert.False(comp.IsIdle);

        comp.IdleSeconds = 480; // 8 minutes (>= 300s -> Idle)
        Assert.Equal("💤 Pasif 8m", comp.IdleFormatted);
        Assert.True(comp.IsIdle);

        comp.IdleSeconds = 7500; // > 2 hours
        Assert.Equal("💤 Pasif 2j", comp.IdleFormatted);
        Assert.True(comp.IsIdle);
    }

    [Fact]
    public void HeartbeatMessage_TelemetryProperties_RoundtripSerialization()
    {
        var hb = new HeartbeatMessage
        {
            Hostname = "CLIENT-10",
            IpAddress = "192.168.1.110",
            MacAddress = "00:11:22:33:44:55",
            NetworkDownloadKbps = 1024.5,
            NetworkUploadKbps = 512.2,
            IdleSeconds = 600
        };

        string json = MessageSerializer.Serialize(hb);
        var des = MessageSerializer.Deserialize<HeartbeatMessage>(json);

        Assert.NotNull(des);
        Assert.Equal(1024.5, des.NetworkDownloadKbps);
        Assert.Equal(512.2, des.NetworkUploadKbps);
        Assert.Equal(600, des.IdleSeconds);
    }

    [Fact]
    public async Task WorkspaceCleanerService_CleanWorkspaceAsync_ExecutesAndReturnsResult()
    {
        var cleaner = new WorkspaceCleanerService();
        var payload = new CleanWorkspacePayload
        {
            CleanDownloads = false,
            CleanTemp = false,
            CleanRecycleBin = false,
            CleanDesktopDocs = false,
            CleanSsh = false,
            CleanVirtualBoxVms = false
        };

        var result = await cleaner.CleanWorkspaceAsync(payload);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Contains("dibersihkan", result.SummaryText);
    }

    [Fact]
    public async Task WorkspaceCleanActivity_LogsToDatabase()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"clean_db_test_{Guid.NewGuid():N}.db");
        using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        string summary = "Pembersihan selesai: 1/1 PC sukses. 15 file dihapus (45.2 MB ruang dipulihkan).";
        await db.LogActivityAsync("PC-SISWA-01", "CleanWorkspace", summary, true);

        var logs = await db.GetActivityLogsAsync(5);
        Assert.NotEmpty(logs);
        var log = logs.First();
        Assert.Equal("PC-SISWA-01", log.TargetHost);
        Assert.Equal("CleanWorkspace", log.Action);
        Assert.True(log.IsSuccess);
        Assert.Contains("45.2 MB", log.Result);
    }
}
