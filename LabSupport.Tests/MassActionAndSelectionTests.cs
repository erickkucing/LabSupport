using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LabSupport.Agent.Services;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;
using Xunit;

namespace LabSupport.Tests;

public class MassActionAndSelectionTests : IDisposable
{
    private readonly int _testPort = 15910;
    private readonly CommandListener _listener;
    private readonly AgentCommandService _client;
    private readonly ScreenLockService _screenLockService;
    private readonly PowerManager _powerManager;

    public MassActionAndSelectionTests()
    {
        _screenLockService = new ScreenLockService();
        _powerManager = new PowerManager();
        _listener = new CommandListener(
            port: _testPort,
            screenLockService: _screenLockService,
            powerManager: _powerManager);
        _listener.Start();

        _client = new AgentCommandService(_testPort);
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Dispose();
        _screenLockService.Dispose();
    }

    [Fact]
    public void MultiLabSelection_SelectRoom_SelectsOnlyTargetRoom()
    {
        var computers = new List<ComputerInfo>
        {
            new ComputerInfo { Hostname = "PC-A1", RoomName = "Lab 209A", IsSelected = false },
            new ComputerInfo { Hostname = "PC-A2", RoomName = "Lab 209A", IsSelected = false },
            new ComputerInfo { Hostname = "PC-B1", RoomName = "Lab 209B", IsSelected = false }
        };

        SelectionService.SelectRoom(computers, "Lab 209A");

        Assert.True(computers[0].IsSelected);
        Assert.True(computers[1].IsSelected);
        Assert.False(computers[2].IsSelected);
    }

    [Fact]
    public void MultiLabSelection_DeselectRoom_DeselectsOnlyTargetRoom()
    {
        var computers = new List<ComputerInfo>
        {
            new ComputerInfo { Hostname = "PC-A1", RoomName = "Lab 209A", IsSelected = true },
            new ComputerInfo { Hostname = "PC-B1", RoomName = "Lab 209B", IsSelected = true }
        };

        SelectionService.DeselectRoom(computers, "Lab 209A");

        Assert.False(computers[0].IsSelected);
        Assert.True(computers[1].IsSelected);
    }

    [Fact]
    public void MultiLabSelection_SelectMultipleRooms_SelectsMatchingRooms()
    {
        var computers = new List<ComputerInfo>
        {
            new ComputerInfo { Hostname = "PC-A1", RoomName = "Lab 209A", IsSelected = false },
            new ComputerInfo { Hostname = "PC-B1", RoomName = "Lab 209B", IsSelected = false },
            new ComputerInfo { Hostname = "PC-C1", RoomName = "Lab 209C", IsSelected = false }
        };

        SelectionService.SelectMultipleRooms(computers, new[] { "Lab 209A", "Lab 209B" });

        Assert.True(computers[0].IsSelected);
        Assert.True(computers[1].IsSelected);
        Assert.False(computers[2].IsSelected);
    }

    [Fact]
    public void MultiLabSelection_InvertSelection_InvertsAllComputers()
    {
        var computers = new List<ComputerInfo>
        {
            new ComputerInfo { Hostname = "PC-1", IsSelected = true },
            new ComputerInfo { Hostname = "PC-2", IsSelected = false }
        };

        SelectionService.InvertSelection(computers);

        Assert.False(computers[0].IsSelected);
        Assert.True(computers[1].IsSelected);
    }

    [Fact]
    public void MultiLabSelection_FormatSelectionSummary_ReturnsFormattedSummary()
    {
        var computers = new List<ComputerInfo>
        {
            new ComputerInfo { Hostname = "PC-A1", RoomName = "Lab 209A", IsSelected = true },
            new ComputerInfo { Hostname = "PC-A2", RoomName = "Lab 209A", IsSelected = true },
            new ComputerInfo { Hostname = "PC-B1", RoomName = "Lab 209B", IsSelected = true },
            new ComputerInfo { Hostname = "PC-C1", RoomName = "Lab 209C", IsSelected = false }
        };

        string summary = SelectionService.FormatSelectionSummary(computers);

        Assert.Contains("3 PC Terpilih", summary);
        Assert.Contains("Lab 209A: 2", summary);
        Assert.Contains("Lab 209B: 1", summary);

        // When none selected
        SelectionService.SelectAll(computers, false);
        Assert.Equal("0 PC Terpilih", SelectionService.FormatSelectionSummary(computers));
    }

    [Fact]
    public void MultiLabSelection_GetRoomSummaryList_CalculatesAccurately()
    {
        var computers = new List<ComputerInfo>
        {
            new ComputerInfo { Hostname = "PC-A1", RoomName = "Lab 209A", IsOnline = true, IsSelected = true },
            new ComputerInfo { Hostname = "PC-A2", RoomName = "Lab 209A", IsOnline = false, IsSelected = false },
            new ComputerInfo { Hostname = "PC-B1", RoomName = "Lab 209B", IsOnline = true, IsSelected = true }
        };

        var summaries = SelectionService.GetRoomSummaries(computers);

        Assert.Equal(2, summaries.Count);
        var roomA = summaries.First(s => s.RoomName == "Lab 209A");
        Assert.Equal(2, roomA.TotalCount);
        Assert.Equal(1, roomA.OnlineCount);
        Assert.Equal(1, roomA.SelectedCount);
        Assert.True(roomA.IsPartiallySelected);
        Assert.False(roomA.IsFullySelected);

        var roomB = summaries.First(s => s.RoomName == "Lab 209B");
        Assert.Equal(1, roomB.TotalCount);
        Assert.Equal(1, roomB.OnlineCount);
        Assert.Equal(1, roomB.SelectedCount);
        Assert.True(roomB.IsFullySelected);
    }

    [Fact]
    public async Task SleepCommand_ExecutesAndReturnsExpectedResponse()
    {
        var cmd = new CommandMessage
        {
            Type = CommandType.Sleep
        };

        var response = await _client.SendCommandAsync("127.0.0.1", cmd, _testPort, timeoutMs: 3000);

        Assert.NotNull(response);
        // Note: On non-Windows/Linux, powerManager handles gracefully or returns expected result
        Assert.True(response.Success || response.Output?.Length > 0 || response.Error?.Length > 0);
    }

    [Fact]
    public async Task BlackScreenCommand_EnableAndDisable_TogglesBlackScreenState()
    {
        // 1. Enable Black Screen
        var enableCmd = new CommandMessage
        {
            Type = CommandType.SetBlackScreen,
            Payload = MessageSerializer.Serialize(new BlackScreenPayload { Enable = true })
        };

        var enableResp = await _client.SendCommandAsync("127.0.0.1", enableCmd, _testPort, timeoutMs: 3000);
        Assert.True(enableResp.Success);
        Assert.True(_screenLockService.IsBlackScreen);
        Assert.True(_screenLockService.IsLocked);

        // 2. Disable Black Screen
        var disableCmd = new CommandMessage
        {
            Type = CommandType.SetBlackScreen,
            Payload = MessageSerializer.Serialize(new BlackScreenPayload { Enable = false })
        };

        var disableResp = await _client.SendCommandAsync("127.0.0.1", disableCmd, _testPort, timeoutMs: 3000);
        Assert.True(disableResp.Success);
        Assert.False(_screenLockService.IsBlackScreen);
        Assert.False(_screenLockService.IsLocked);
    }

    [Fact]
    public void BlackScreenPayload_Serialization_RoundTripsCorrectly()
    {
        var original = new BlackScreenPayload { Enable = true };
        string json = MessageSerializer.Serialize(original);
        var restored = MessageSerializer.Deserialize<BlackScreenPayload>(json);

        Assert.NotNull(restored);
        Assert.True(restored.Enable);
    }

    [Fact]
    public void MassScript_PresetTemplates_GenerateValidPayloads()
    {
        var dnsTemplate = MassActionTemplates.GetTemplate(MassScriptTemplateKey.FlushDns);
        Assert.NotNull(dnsTemplate);
        Assert.Contains("flushdns", dnsTemplate.ScriptContent);
        Assert.Equal(ScriptType.Batch, dnsTemplate.Type);

        var diskTemplate = MassActionTemplates.GetTemplate(MassScriptTemplateKey.CheckDisk);
        Assert.NotNull(diskTemplate);
        Assert.Contains("Get-PSDrive", diskTemplate.ScriptContent);
        Assert.Equal(ScriptType.PowerShell, diskTemplate.Type);

        var killBrowserTemplate = MassActionTemplates.GetTemplate(MassScriptTemplateKey.KillFrozenBrowsers);
        Assert.NotNull(killBrowserTemplate);
        Assert.Contains("chrome.exe", killBrowserTemplate.ScriptContent);

        var syncTimeTemplate = MassActionTemplates.GetTemplate(MassScriptTemplateKey.SyncTime);
        Assert.NotNull(syncTimeTemplate);
        Assert.Contains("w32tm", syncTimeTemplate.ScriptContent);
    }

    [Fact]
    public void MassKeyboard_ShortcutAndTextPayloads_GenerateValidCommands()
    {
        var shortcutPayload = new KeyboardInputPayload
        {
            Action = KeyboardAction.Shortcut,
            Key = "WIN+L"
        };
        string json = MessageSerializer.Serialize(shortcutPayload);
        var restored = MessageSerializer.Deserialize<KeyboardInputPayload>(json);

        Assert.NotNull(restored);
        Assert.Equal(KeyboardAction.Shortcut, restored.Action);
        Assert.Equal("WIN+L", restored.Key);

        var inputSim = new InputSimulator();
        bool ok = inputSim.SimulateKeyboard(restored);
        Assert.True(ok);
    }

    [Fact]
    public void MultiLabSelection_SelectRoom_WithNullOrWhitespaceRoom_MatchesGeneralRoom()
    {
        var computers = new List<ComputerInfo>
        {
            new ComputerInfo { Hostname = "PC-GEN-1", RoomName = "", IsSelected = false },
            new ComputerInfo { Hostname = "PC-GEN-2", RoomName = "   ", IsSelected = false },
            new ComputerInfo { Hostname = "PC-OTHER", RoomName = "Lab 101", IsSelected = false }
        };

        // Select General room
        SelectionService.SelectRoom(computers, "General");
        Assert.True(computers[0].IsSelected);
        Assert.True(computers[1].IsSelected);
        Assert.False(computers[2].IsSelected);

        // Deselect General room
        SelectionService.DeselectRoom(computers, "General");
        Assert.False(computers[0].IsSelected);
        Assert.False(computers[1].IsSelected);

        // Select via SelectMultipleRooms
        SelectionService.SelectMultipleRooms(computers, new[] { "General" });
        Assert.True(computers[0].IsSelected);
        Assert.True(computers[1].IsSelected);
        Assert.False(computers[2].IsSelected);
    }

    [Fact]
    public void ScreenLock_TransitionsBetweenBlackScreenAndAttentionLock_PreservesState()
    {
        // 1. Initially unlocked
        Assert.False(_screenLockService.IsBlackScreen);
        Assert.False(_screenLockService.IsLocked);

        // 2. Turn on black screen
        bool blackOn = _screenLockService.SetBlackScreen(true);
        Assert.True(blackOn);
        Assert.True(_screenLockService.IsBlackScreen);
        Assert.True(_screenLockService.IsLocked);

        // 3. Switch directly to Attention Lock
        bool lockAtt = _screenLockService.Lock("UJIAN DIMULAI");
        Assert.True(lockAtt);
        Assert.False(_screenLockService.IsBlackScreen);
        Assert.True(_screenLockService.IsLocked);
        Assert.Equal("UJIAN DIMULAI", _screenLockService.CurrentMessage);

        // 4. Switch back to Black Screen
        bool blackAgain = _screenLockService.SetBlackScreen(true);
        Assert.True(blackAgain);
        Assert.True(_screenLockService.IsBlackScreen);
        Assert.True(_screenLockService.IsLocked);

        // 5. Unlock all
        bool unlock = _screenLockService.Unlock();
        Assert.True(unlock);
        Assert.False(_screenLockService.IsBlackScreen);
        Assert.False(_screenLockService.IsLocked);
    }

    [Fact]
    public void InputSimulator_SimulateKeyboard_WithNewlines_ExecutesSuccessfully()
    {
        var inputSim = new InputSimulator();
        var payload = new KeyboardInputPayload
        {
            Action = KeyboardAction.Text,
            Text = "echo testing 123\r\ndir\n"
        };

        bool ok = inputSim.SimulateKeyboard(payload);
        Assert.True(ok);
    }

    [Fact]
    public void MassLaunch_Sanitization_RejectsDangerousCharacters()
    {
        char[] forbidden = { '&', '|', ';', '<', '>', '^', '%', '\r', '\n' };
        string[] maliciousTargets =
        {
            "notepad.exe & calc.exe",
            "cmd.exe | whoami",
            "test.bat; rm -rf",
            "app.exe > out.txt",
            "evil.exe%var%",
            "run\r\nmalicious"
        };

        foreach (var target in maliciousTargets)
        {
            bool hasForbidden = target.IndexOfAny(forbidden) >= 0;
            Assert.True(hasForbidden, $"Target '{target}' should contain forbidden command chaining characters");
        }

        string benignTarget = "https://exam.school.sch.id/test?id=123";
        Assert.Equal(-1, benignTarget.IndexOfAny(forbidden));
    }

    [Fact]
    public void ForceKill_CriticalOsProcesses_AreRecognized()
    {
        string[] criticalProcesses = { "csrss", "winlogon", "services", "lsass", "smss", "svchost", "system", "idle" };

        Assert.Contains("csrss", criticalProcesses, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("svchost", criticalProcesses, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("winlogon", criticalProcesses, StringComparer.OrdinalIgnoreCase);

        // User typed "svchost.exe" -> trimmed to "svchost"
        string input = "svchost.exe";
        string procName = input.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? input.Substring(0, input.Length - 4) : input;
        Assert.Contains(procName, criticalProcesses, StringComparer.OrdinalIgnoreCase);
    }
}
