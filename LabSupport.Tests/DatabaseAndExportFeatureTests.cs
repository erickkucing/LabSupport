using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class DatabaseAndExportFeatureTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _dbService;

    public DatabaseAndExportFeatureTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"feature_test_{Guid.NewGuid():N}.db");
        _dbService = new DatabaseService(_dbPath);
    }

    [Fact]
    public async Task GroupManagement_DatabaseOperations_PersistAndFilter()
    {
        await _dbService.InitializeAsync();

        var pc1 = new ComputerInfo { Hostname = "PC-G1", IpAddress = "10.0.0.1", GroupName = "Baris-1", LastSeen = DateTime.UtcNow };
        var pc2 = new ComputerInfo { Hostname = "PC-G2", IpAddress = "10.0.0.2", GroupName = "Baris-2", LastSeen = DateTime.UtcNow };
        var pc3 = new ComputerInfo { Hostname = "PC-G3", IpAddress = "10.0.0.3", GroupName = "Baris-1", LastSeen = DateTime.UtcNow };

        await _dbService.UpsertComputerAsync(pc1);
        await _dbService.UpsertComputerAsync(pc2);
        await _dbService.UpsertComputerAsync(pc3);

        var all = await _dbService.GetAllComputersAsync();
        Assert.Equal(3, all.Count);

        var baris1 = all.Where(c => c.GroupName == "Baris-1").ToList();
        Assert.Equal(2, baris1.Count);

        var baris2 = all.Where(c => c.GroupName == "Baris-2").ToList();
        Assert.Single(baris2);

        // Update Group
        await _dbService.UpdateComputerGroupAsync("PC-G3", "Baris-3");
        var updated = await _dbService.GetAllComputersAsync();
        Assert.Equal("Baris-3", updated.Find(c => c.Hostname == "PC-G3")?.GroupName);

        var distinctGroups = await _dbService.GetGroupsAsync();
        Assert.Contains("Baris-1", distinctGroups);
        Assert.Contains("Baris-2", distinctGroups);
        Assert.Contains("Baris-3", distinctGroups);
    }

    [Fact]
    public void TelemetryExport_GeneratesAccurateCsv()
    {
        var pcs = new List<ComputerInfo>
        {
            new ComputerInfo
            {
                Hostname = "LAB-PC-01",
                IpAddress = "192.168.1.101",
                MacAddress = "00:1A:2B:3C:4D:5E",
                OsVersion = "Windows 11 Pro",
                GroupName = "Row-A",
                CpuUsage = 35.8,
                RamUsageMb = 4120,
                RamTotalMb = 16384,
                DiskFreeGb = 250.4,
                IsOnline = true
            },
            new ComputerInfo
            {
                Hostname = "LAB-PC-02",
                IpAddress = "192.168.1.102",
                MacAddress = "00:1A:2B:3C:4D:5F",
                OsVersion = "Windows 10 Pro",
                GroupName = "Row-B",
                CpuUsage = 12.1,
                RamUsageMb = 2048,
                RamTotalMb = 8192,
                DiskFreeGb = 100.0,
                IsOnline = false
            }
        };

        string csv = DatabaseService.ExportTelemetryToCsvString(pcs);
        Assert.Contains("Hostname,IpAddress,MacAddress,OsVersion,Group,CpuUsage,RamUsageMb,RamTotalMb,DiskFreeGb,IsOnline,LastSeen", csv);
        Assert.Contains("\"LAB-PC-01\"", csv);
        Assert.Contains("\"Row-A\"", csv);
        Assert.Contains("35.8,4120,16384,250.4,True", csv);
        Assert.Contains("\"LAB-PC-02\"", csv);
        Assert.Contains("\"Row-B\"", csv);
        Assert.Contains("12.1,2048,8192,100.0,False", csv);
    }

    [Fact]
    public async Task ActivityLogExport_WritesFileAndPreservesSpecialCharacters()
    {
        await _dbService.InitializeAsync();

        await _dbService.LogActivityAsync("PC-EXAM-01", "ExamLockdown", "Enabled: blocked Win keys, Alt+Tab, TaskMgr", true);
        await _dbService.LogActivityAsync("PC-EXAM-02", "FirewallBlock", "Blocked public 80/443; LAN & DNS preserved", true);

        string tempFile = Path.Combine(Path.GetTempPath(), $"logs_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            await _dbService.ExportActivityLogsToCsvFileAsync(tempFile);
            Assert.True(File.Exists(tempFile));

            string content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("ID,TargetHost,Action,Result,IsSuccess,Timestamp", content);
            Assert.Contains("\"PC-EXAM-01\"", content);
            Assert.Contains("\"ExamLockdown\"", content);
            Assert.Contains("\"Enabled: blocked Win keys, Alt+Tab, TaskMgr\"", content);
            Assert.Contains("\"PC-EXAM-02\"", content);
        }
        finally
        {
            if (File.Exists(tempFile)) try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task LabRoom_CrudOperations_WorkCorrectly()
    {
        await _dbService.InitializeAsync();

        var roomA = new LabRoom { Name = "Lab 209A", Capacity = 30, Description = "Lab Komputer A" };
        var roomB = new LabRoom { Name = "Lab 209B", Capacity = 20, Description = "Lab Komputer B" };

        await _dbService.UpsertRoomAsync(roomA);
        await _dbService.UpsertRoomAsync(roomB);

        var rooms = await _dbService.GetAllRoomsAsync();
        Assert.Equal(2, rooms.Count);
        Assert.Contains(rooms, r => r.Name == "Lab 209A" && r.Capacity == 30);
        Assert.Contains(rooms, r => r.Name == "Lab 209B" && r.Capacity == 20);

        // Update capacity
        roomA.Capacity = 32;
        await _dbService.UpsertRoomAsync(roomA);
        var updated = await _dbService.GetAllRoomsAsync();
        var foundA = updated.First(r => r.Name == "Lab 209A");
        Assert.Equal(32, foundA.Capacity);

        // Delete
        await _dbService.DeleteRoomAsync("Lab 209B");
        var afterDelete = await _dbService.GetAllRoomsAsync();
        Assert.Single(afterDelete);
        Assert.DoesNotContain(afterDelete, r => r.Name == "Lab 209B");
    }

    [Fact]
    public async Task RoomManagement_DatabaseOperations_PersistAndFilter()
    {
        await _dbService.InitializeAsync();

        var pc1 = new ComputerInfo
        {
            Hostname = "PC-ROOM-01",
            DisplayName = "PC-01",
            IpAddress = "192.168.1.11",
            RoomName = "Lab 209A",
            GroupName = "Baris-1",
            LastSeen = DateTime.UtcNow
        };
        var pc2 = new ComputerInfo
        {
            Hostname = "PC-ROOM-02",
            DisplayName = "PC-02",
            IpAddress = "192.168.1.12",
            RoomName = "Lab 209B",
            GroupName = "Baris-1",
            LastSeen = DateTime.UtcNow
        };

        await _dbService.UpsertComputerAsync(pc1);
        await _dbService.UpsertComputerAsync(pc2);

        var all = await _dbService.GetAllComputersAsync();
        Assert.Equal(2, all.Count);

        var found1 = all.First(c => c.Hostname == "PC-ROOM-01");
        Assert.Equal("Lab 209A", found1.RoomName);
        Assert.Equal("PC-01", found1.DisplayName);
        Assert.Equal("PC-01", found1.EffectiveName);

        var found2 = all.First(c => c.Hostname == "PC-ROOM-02");
        Assert.Equal("Lab 209B", found2.RoomName);
        Assert.Equal("PC-02", found2.DisplayName);

        // Reassign room
        await _dbService.UpdateComputerRoomAsync("PC-ROOM-01", "Lab 209C");
        var afterRoomUpdate = await _dbService.GetAllComputersAsync();
        Assert.Equal("Lab 209C", afterRoomUpdate.First(c => c.Hostname == "PC-ROOM-01").RoomName);

        // Update display name
        await _dbService.UpdateComputerDisplayNameAsync("PC-ROOM-01", "PC-CUSTOM-99");
        var afterNameUpdate = await _dbService.GetAllComputersAsync();
        Assert.Equal("PC-CUSTOM-99", afterNameUpdate.First(c => c.Hostname == "PC-ROOM-01").DisplayName);
        Assert.Equal("PC-CUSTOM-99", afterNameUpdate.First(c => c.Hostname == "PC-ROOM-01").EffectiveName);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (File.Exists(_dbPath)) try { File.Delete(_dbPath); } catch { }
    }
}
