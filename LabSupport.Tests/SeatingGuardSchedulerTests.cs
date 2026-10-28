using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Agent.Services;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class SeatingGuardSchedulerTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly DatabaseService _dbService;

    public SeatingGuardSchedulerTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"labsupport_test_{Guid.NewGuid():N}.db");
        _dbService = new DatabaseService(_testDbPath);
        _dbService.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _dbService.Dispose();
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
    }

    [Fact]
    public async Task Database_ComputerPosition_PersistsAndLoadsCorrectly()
    {
        var comp = new ComputerInfo
        {
            Hostname = "LAB-PC-01",
            IpAddress = "192.168.1.101",
            MacAddress = "00:11:22:33:44:55",
            OsVersion = "Windows 11 Pro",
            PositionX = 50.0,
            PositionY = 120.0
        };

        await _dbService.UpsertComputerAsync(comp);

        // Update position via Floor Plan dragging
        await _dbService.UpdateComputerPositionAsync("LAB-PC-01", 340.5, 480.2);

        var list = await _dbService.GetAllComputersAsync();
        var retrieved = list.FirstOrDefault(c => c.Hostname == "LAB-PC-01");

        Assert.NotNull(retrieved);
        Assert.Equal(340.5, retrieved.PositionX, 2);
        Assert.Equal(480.2, retrieved.PositionY, 2);
    }

    [Fact]
    public async Task Database_SchedulerAndAppGuardSettings_PersistAndRetrieve()
    {
        // 1. Power Scheduler settings
        await _dbService.SaveSchedulerSettingsAsync(true, new TimeSpan(17, 30, 0));
        var (schedEnabled, schedTime) = await _dbService.GetSchedulerSettingsAsync();

        Assert.True(schedEnabled);
        Assert.Equal(new TimeSpan(17, 30, 0), schedTime);

        // 2. App Guard settings
        string testBlacklist = "steam,roblox,discord,customgame";
        await _dbService.SaveAppGuardSettingsAsync(true, testBlacklist);
        var (guardEnabled, guardCsv) = await _dbService.GetAppGuardSettingsAsync();

        Assert.True(guardEnabled);
        Assert.Equal(testBlacklist, guardCsv);
    }

    [Fact]
    public void ProcessGuard_SanitizationAndMatching_WorksAccurately()
    {
        // Test process name sanitization
        Assert.Equal("steam", ProcessGuardService.CleanProcessName("steam.exe"));
        Assert.Equal("roblox", ProcessGuardService.CleanProcessName("C:\\Games\\Roblox.exe"));
        Assert.Equal("discord", ProcessGuardService.CleanProcessName("  discord  "));
        Assert.Equal("cheatengine", ProcessGuardService.CleanProcessName("CheatEngine.EXE"));
        Assert.Equal(string.Empty, ProcessGuardService.CleanProcessName("   "));

        // Test matching with active rules
        using var guard = new ProcessGuardService(10000);
        guard.Configure(true, new[] { "steam.exe", "Roblox", "discord", "cheatengine.exe" });

        Assert.True(guard.IsEnabled);
        Assert.True(guard.IsBlacklisted("steam"));
        Assert.True(guard.IsBlacklisted("STEAM.EXE"));
        Assert.True(guard.IsBlacklisted("roblox"));
        Assert.True(guard.IsBlacklisted("Roblox.exe"));
        Assert.True(guard.IsBlacklisted("Discord"));
        Assert.True(guard.IsBlacklisted("CheatEngine"));

        // Non-blacklisted processes
        Assert.False(guard.IsBlacklisted("chrome"));
        Assert.False(guard.IsBlacklisted("devenv"));
        Assert.False(guard.IsBlacklisted("notepad"));
    }

    [Fact]
    public void ProcessGuard_Disabled_DoesNotMatchOrScan()
    {
        using var guard = new ProcessGuardService(10000);
        guard.Configure(false, new[] { "steam", "roblox" });

        Assert.False(guard.IsEnabled);
        Assert.Equal(0, guard.ScanAndKill());
    }

    [Fact]
    public void ClipboardPayload_Serialization_RoundTripsCorrectly()
    {
        var original = new ClipboardPayload
        {
            Text = "https://classroom.google.com/c/lab101?token=XYZ899"
        };

        string json = MessageSerializer.Serialize(original);
        var deserialized = MessageSerializer.Deserialize<ClipboardPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Text, deserialized.Text);

        var cmd = new CommandMessage
        {
            Type = CommandType.SetClipboardText,
            Payload = json
        };

        string cmdJson = MessageSerializer.Serialize(cmd);
        var deserializedCmd = MessageSerializer.Deserialize<CommandMessage>(cmdJson);

        Assert.NotNull(deserializedCmd);
        Assert.NotNull(deserializedCmd.Payload);
        Assert.Equal(CommandType.SetClipboardText, deserializedCmd.Type);

        var innerPayload = MessageSerializer.Deserialize<ClipboardPayload>(deserializedCmd.Payload!);
        Assert.NotNull(innerPayload);
        Assert.Equal(original.Text, innerPayload.Text);
    }

    [Fact]
    public void ClipboardInjectorService_StoresLastSetTextInMockEnvironment()
    {
        var injector = new ClipboardInjectorService();
        string testText = "print('Hello Lab Students!')";

        bool result = injector.SetClipboardText(testText);

        // On Linux test runners, SetClipboardText returns true and sets LastSetText safely
        Assert.True(result);
        Assert.Equal(testText, injector.LastSetText);
    }

    [Fact]
    public void FloorPlan_AutoArrangeAlgorithm_EnsuresDistinctNonOverlappingCoordinates()
    {
        int totalPCs = 15;
        int columns = 5;
        double startX = 40;
        double startY = 40;
        double gapX = 260;
        double gapY = 300;

        var positions = new List<(double X, double Y)>();
        for (int i = 0; i < totalPCs; i++)
        {
            int col = i % columns;
            int row = i / columns;
            double x = startX + (col * gapX);
            double y = startY + (row * gapY);
            positions.Add((x, y));
        }

        Assert.Equal(15, positions.Count);

        // First item
        Assert.Equal(40, positions[0].X);
        Assert.Equal(40, positions[0].Y);

        // Second item (next column)
        Assert.Equal(300, positions[1].X);
        Assert.Equal(40, positions[1].Y);

        // Sixth item (second row, first column)
        Assert.Equal(40, positions[5].X);
        Assert.Equal(340, positions[5].Y);

        // Verify all 15 positions are unique and spaced
        var distinctPositions = positions.Distinct().ToList();
        Assert.Equal(15, distinctPositions.Count);
    }

    [Fact]
    public void PowerScheduler_TimeMatchingAndSingleDailyExecution_LogicValid()
    {
        var scheduledTime = new TimeSpan(17, 0, 0);
        var currentTimeMatching = new DateTime(2026, 10, 27, 17, 0, 15);
        var currentTimeDifferentMinute = new DateTime(2026, 10, 27, 17, 1, 0);

        bool isHourMinuteMatch1 = currentTimeMatching.Hour == scheduledTime.Hours && currentTimeMatching.Minute == scheduledTime.Minutes;
        bool isHourMinuteMatch2 = currentTimeDifferentMinute.Hour == scheduledTime.Hours && currentTimeDifferentMinute.Minute == scheduledTime.Minutes;

        Assert.True(isHourMinuteMatch1);
        Assert.False(isHourMinuteMatch2);

        // Verify date tracking prevents duplicate trigger on same day
        DateTime lastTriggerDate = currentTimeMatching.Date;
        bool shouldTriggerAgainToday = lastTriggerDate != currentTimeMatching.Date;

        Assert.False(shouldTriggerAgainToday);
    }
}
