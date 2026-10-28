using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LabSupport.Agent.Services;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;
using Xunit;

#pragma warning disable CA1416

namespace LabSupport.Tests;

public class AdvancedEnhancementsTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _tempHostsPath;
    private readonly DatabaseService _dbService;

    public AdvancedEnhancementsTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"labsupport_adv_{Guid.NewGuid():N}.db");
        _tempHostsPath = Path.Combine(Path.GetTempPath(), $"hosts_test_{Guid.NewGuid():N}.txt");
        _dbService = new DatabaseService(_tempDbPath);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        try { if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath); } catch { }
        try { if (File.Exists(_tempHostsPath)) File.Delete(_tempHostsPath); } catch { }
    }

    [Fact]
    public void WebFilterService_BlacklistMode_WritesSinkholeEntriesAndFlushes()
    {
        // Arrange
        File.WriteAllText(_tempHostsPath, "127.0.0.1 localhost\r\n::1 localhost\r\n");
        var filter = new WebFilterService(_tempHostsPath);

        // Act
        bool ok = filter.ApplyFilter(WebFilterMode.Blacklist, new[] { "roblox.com", "tiktok.com" });
        string hostsContent = File.ReadAllText(_tempHostsPath);

        // Assert
        Assert.True(ok);
        Assert.Contains(WebFilterService.BeginMarker, hostsContent);
        Assert.Contains(WebFilterService.EndMarker, hostsContent);
        Assert.Contains("0.0.0.0 roblox.com", hostsContent);
        Assert.Contains("0.0.0.0 www.roblox.com", hostsContent);
        Assert.Contains("0.0.0.0 tiktok.com", hostsContent);
        Assert.Contains("127.0.0.1 localhost", hostsContent);
    }

    [Fact]
    public void WebFilterService_WhitelistMode_WritesAllowMarkers()
    {
        // Arrange
        File.WriteAllText(_tempHostsPath, "127.0.0.1 localhost\r\n");
        var filter = new WebFilterService(_tempHostsPath);

        // Act
        bool ok = filter.ApplyFilter(WebFilterMode.Whitelist, new[] { "school.edu", "classroom.google.com" });
        string hostsContent = File.ReadAllText(_tempHostsPath);

        // Assert
        Assert.True(ok);
        Assert.Contains("# Whitelist Mode Active", hostsContent);
        Assert.Contains("# ALLOW school.edu", hostsContent);
        Assert.Contains("# ALLOW classroom.google.com", hostsContent);
    }

    [Fact]
    public void WebFilterService_ClearFilter_RestoresOriginalContent()
    {
        // Arrange
        File.WriteAllText(_tempHostsPath, "127.0.0.1 localhost\r\n");
        var filter = new WebFilterService(_tempHostsPath);
        filter.ApplyFilter(WebFilterMode.Blacklist, new[] { "steamcommunity.com" });

        // Act
        bool clearOk = filter.ClearFilter();
        string hostsContent = File.ReadAllText(_tempHostsPath);

        // Assert
        Assert.True(clearOk);
        Assert.DoesNotContain(WebFilterService.BeginMarker, hostsContent);
        Assert.DoesNotContain("steamcommunity.com", hostsContent);
        Assert.Contains("127.0.0.1 localhost", hostsContent);
    }

    [Fact]
    public void WebFilterService_SanitizeDomain_RemovesProtocolsAndPaths()
    {
        // Arrange
        File.WriteAllText(_tempHostsPath, "127.0.0.1 localhost\r\n");
        var filter = new WebFilterService(_tempHostsPath);

        // Act
        filter.ApplyFilter(WebFilterMode.Blacklist, new[]
        {
            "https://www.youtube.com/watch?v=123",
            "http://gaming-site.org:8080/play"
        });
        string content = File.ReadAllText(_tempHostsPath);

        // Assert
        Assert.Contains("0.0.0.0 youtube.com", content);
        Assert.Contains("0.0.0.0 gaming-site.org", content);
        Assert.DoesNotContain("https://", content);
        Assert.DoesNotContain("/watch", content);
    }

    [Fact]
    public void ScreenCaptureAdaptive_DirtyCheck_SkipsUnchangedFrames()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Arrange
        using var bmp1 = new Bitmap(200, 200);
        using (var g = Graphics.FromImage(bmp1))
        {
            g.Clear(Color.Blue);
        }

        using var bmp2 = new Bitmap(200, 200);
        using (var g = Graphics.FromImage(bmp2))
        {
            g.Clear(Color.Blue);
        }

        using var bmp3 = new Bitmap(200, 200);
        using (var g = Graphics.FromImage(bmp3))
        {
            g.Clear(Color.Red);
        }

        // Act
        ulong hash1 = ScreenCaptureService.ComputeBitmapSampleHash(bmp1);
        ulong hash2 = ScreenCaptureService.ComputeBitmapSampleHash(bmp2);
        ulong hash3 = ScreenCaptureService.ComputeBitmapSampleHash(bmp3);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hash3);
    }

    [Fact]
    public void BandwidthHog_Threshold_CorrectlyTriggers()
    {
        var normalPc = new ComputerInfo { NetworkDownloadKbps = 1500 };
        var hogPc = new ComputerInfo { NetworkDownloadKbps = 3500 };

        Assert.False(normalPc.IsBandwidthHog);
        Assert.True(hogPc.IsBandwidthHog);
    }

    [Fact]
    public async Task DatabaseService_HardwareInventory_PersistsAndLoadsAllColumns()
    {
        await _dbService.InitializeAsync();

        // Arrange
        var pc = new ComputerInfo
        {
            Hostname = "LAB-PC-SPEC-01",
            IpAddress = "192.168.1.101",
            MacAddress = "AA:BB:CC:DD:EE:01",
            OsVersion = "Windows 11 Pro 64-bit",
            CpuModel = "Intel Core i7-13700 @ 2.10GHz",
            GpuModel = "NVIDIA GeForce RTX 4060",
            DiskTotalGb = 512.0,
            DiskFreeGb = 8.5,
            MotherboardSerial = "MB-SN-9988776655",
            RoomName = "Lab-Multimedia",
            GroupName = "Multimedia-A"
        };

        // Act
        await _dbService.UpsertComputerAsync(pc);
        var loadedList = await _dbService.GetAllComputersAsync();
        var found = loadedList.FirstOrDefault(c => c.Hostname == "LAB-PC-SPEC-01");

        // Assert
        Assert.NotNull(found);
        Assert.Equal("Intel Core i7-13700 @ 2.10GHz", found.CpuModel);
        Assert.Equal("NVIDIA GeForce RTX 4060", found.GpuModel);
        Assert.Equal(512.0, found.DiskTotalGb);
        Assert.Equal("MB-SN-9988776655", found.MotherboardSerial);
        Assert.Equal("Lab-Multimedia", found.RoomName);
        Assert.Equal("Multimedia-A", found.GroupName);
    }

    [Fact]
    public void HardwareDiagnostics_HealthWarningProperties_Accurate()
    {
        // Low disk: Free < 10 GB
        var lowDiskPc = new ComputerInfo { DiskFreeGb = 5.0, DiskTotalGb = 500.0, RamUsageMb = 4000, RamTotalMb = 16000 };
        Assert.True(lowDiskPc.IsLowDiskSpace);
        Assert.Equal("Sisa 5.0 GB dari 500 GB", lowDiskPc.DiskStorageFormatted);

        // Low disk: Free < 10% of total
        var lowRatioPc = new ComputerInfo { DiskFreeGb = 15.0, DiskTotalGb = 200.0, RamUsageMb = 4000, RamTotalMb = 16000 };
        Assert.True(lowRatioPc.IsLowDiskSpace);

        // Ample disk
        var goodPc = new ComputerInfo { DiskFreeGb = 120.0, DiskTotalGb = 500.0, RamUsageMb = 4000, RamTotalMb = 16000 };
        Assert.False(goodPc.IsLowDiskSpace);

        // Critical RAM: >= 90%
        var highRamPc = new ComputerInfo { RamUsageMb = 15000, RamTotalMb = 16000 };
        Assert.True(highRamPc.IsHighRamUsage);

        var normalRamPc = new ComputerInfo { RamUsageMb = 8000, RamTotalMb = 16000 };
        Assert.False(normalRamPc.IsHighRamUsage);
    }

    [Fact]
    public void HardwareInventoryCsv_ExportString_FormattedAndEscaped()
    {
        // Arrange
        var computers = new List<ComputerInfo>
        {
            new ComputerInfo
            {
                Hostname = "PC-01",
                DisplayName = "PC Guru Lab 1",
                RoomName = "Lab-RPL",
                GroupName = "Kelompok-1",
                IpAddress = "192.168.1.10",
                MacAddress = "00:11:22:33:44:55",
                CpuModel = "Intel Core i5-12400F",
                GpuModel = "NVIDIA GTX 1650, 4GB",
                DiskTotalGb = 512.0,
                DiskFreeGb = 230.5,
                MotherboardSerial = "SN-A1B2C3",
                OsVersion = "Windows 10 Pro"
            }
        };

        // Act
        string csv = DatabaseService.ExportHardwareInventoryCsvString(computers);

        // Assert
        Assert.Contains("Hostname,DisplayName,Room,Group,IpAddress,MacAddress,CpuModel,GpuModel,DiskTotalGb,DiskFreeGb,MotherboardSerial,OsVersion,LastSeen", csv);
        Assert.Contains("\"PC-01\"", csv);
        Assert.Contains("\"PC Guru Lab 1\"", csv);
        Assert.Contains("\"Intel Core i5-12400F\"", csv);
        Assert.Contains("\"NVIDIA GTX 1650, 4GB\"", csv); // Commas inside quotes
        Assert.Contains("512.0", csv);
        Assert.Contains("230.5", csv);
        Assert.Contains("\"SN-A1B2C3\"", csv);
    }
}
