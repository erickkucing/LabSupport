using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Shared.Models;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class DatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _dbService;

    public DatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lab_test_db_{Guid.NewGuid():N}.db");
        _dbService = new DatabaseService(_dbPath);
    }

    [Fact]
    public async Task InitializeAndUpsertComputer_ShouldPersistDataCorrectly()
    {
        await _dbService.InitializeAsync();

        var pc1 = new ComputerInfo
        {
            Hostname = "LAB-PC-01",
            IpAddress = "192.168.1.10",
            MacAddress = "00:11:22:33:44:55",
            OsVersion = "Windows 10",
            LastSeen = DateTime.UtcNow,
            Notes = "Row 1 Seat 1"
        };

        await _dbService.UpsertComputerAsync(pc1);

        var list = await _dbService.GetAllComputersAsync();
        Assert.Single(list);
        Assert.Equal("LAB-PC-01", list[0].Hostname);
        Assert.Equal("192.168.1.10", list[0].IpAddress);
        Assert.Equal("00:11:22:33:44:55", list[0].MacAddress);
        Assert.Equal("Row 1 Seat 1", list[0].Notes);

        // Update with new IP
        pc1.IpAddress = "192.168.1.99";
        await _dbService.UpsertComputerAsync(pc1);

        var updatedList = await _dbService.GetAllComputersAsync();
        Assert.Single(updatedList);
        Assert.Equal("192.168.1.99", updatedList[0].IpAddress);
    }

    [Fact]
    public async Task ActivityLog_ShouldRecordAndRetrieveLogs()
    {
        await _dbService.InitializeAsync();

        await _dbService.LogActivityAsync("LAB-PC-02", "Shutdown", "Success", true);
        await _dbService.LogActivityAsync("LAB-PC-03", "ExecuteScript", "Timed out", false);

        var logs = await _dbService.GetActivityLogsAsync();
        Assert.Equal(2, logs.Count);

        Assert.Equal("LAB-PC-03", logs[0].TargetHost);
        Assert.Equal("ExecuteScript", logs[0].Action);
        Assert.False(logs[0].IsSuccess);

        Assert.Equal("LAB-PC-02", logs[1].TargetHost);
        Assert.Equal("Shutdown", logs[1].Action);
        Assert.True(logs[1].IsSuccess);
    }

    [Fact]
    public async Task Settings_ShouldSetAndGetValues()
    {
        await _dbService.InitializeAsync();

        string initial = await _dbService.GetSettingAsync("Language", "default-val");
        Assert.Equal("default-val", initial);

        await _dbService.SetSettingAsync("Language", "en-US");
        string updated = await _dbService.GetSettingAsync("Language", "default-val");
        Assert.Equal("en-US", updated);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
