using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using LabSupport.Manager.Models;
using LabSupport.Shared.Services;
using LabSupport.Shared.Protocol;

namespace LabSupport.Manager.ViewModels;

public class ScriptExecutionResultItem : ViewModelBase
{
    private string _hostname = string.Empty;
    private string _ip = string.Empty;
    private string _status = "Pending";
    private string _output = string.Empty;
    private string _error = string.Empty;
    private long _executionTimeMs;
    private bool _isSuccess;

    public string Hostname
    {
        get => _hostname;
        set => SetProperty(ref _hostname, value);
    }

    public string Ip
    {
        get => _ip;
        set => SetProperty(ref _ip, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Output
    {
        get => _output;
        set => SetProperty(ref _output, value);
    }

    public string Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    public long ExecutionTimeMs
    {
        get => _executionTimeMs;
        set => SetProperty(ref _executionTimeMs, value);
    }

    public bool IsSuccess
    {
        get => _isSuccess;
        set => SetProperty(ref _isSuccess, value);
    }
}

public class ScriptViewModel : ViewModelBase
{
    private readonly AgentCommandService _commandService;
    private readonly DatabaseService _databaseService;
    private readonly ObservableCollection<LabComputer> _allComputers;

    private ScriptType _selectedScriptType = ScriptType.PowerShell;
    private string _scriptContent = "Get-ComputerInfo | Select-Object WindowsProductName, OsVersion, CsName, CsTotalPhysicalMemory";
    private string _selectedTargetMode = "AllOnline"; // "AllOnline" or "Selected"
    private bool _isExecuting;
    private string _statusText = "Siap mengeksekusi script.";

    public ObservableCollection<ScriptExecutionResultItem> Results { get; } = new();

    public ScriptType SelectedScriptType
    {
        get => _selectedScriptType;
        set => SetProperty(ref _selectedScriptType, value);
    }

    public string ScriptContent
    {
        get => _scriptContent;
        set => SetProperty(ref _scriptContent, value);
    }

    public string SelectedTargetMode
    {
        get => _selectedTargetMode;
        set => SetProperty(ref _selectedTargetMode, value);
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        set => SetProperty(ref _isExecuting, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ICommand ExecuteCommand { get; }
    public ICommand ClearResultsCommand { get; }
    public ICommand SelectTemplateCommand { get; }

    public ScriptViewModel(
        AgentCommandService commandService,
        DatabaseService databaseService,
        ObservableCollection<LabComputer> allComputers)
    {
        _commandService = commandService;
        _databaseService = databaseService;
        _allComputers = allComputers;

        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => !IsExecuting && !string.IsNullOrWhiteSpace(ScriptContent));
        ClearResultsCommand = new RelayCommand(() => Results.Clear());
        SelectTemplateCommand = new RelayCommand(p => ApplyTemplate(p?.ToString() ?? ""));
    }

    public void ApplyTemplate(string templateName)
    {
        switch (templateName)
        {
            case "SysInfo":
                SelectedScriptType = ScriptType.PowerShell;
                ScriptContent = "Get-ComputerInfo | Select-Object WindowsProductName, OsVersion, CsName, CsTotalPhysicalMemory";
                break;
            case "FlushDns":
                SelectedScriptType = ScriptType.Batch;
                ScriptContent = "@echo off\nipconfig /flushdns\nipconfig /renew\necho DNS and IP refreshed successfully!";
                break;
            case "ListServices":
                SelectedScriptType = ScriptType.PowerShell;
                ScriptContent = "Get-Service | Where-Object { $_.Status -eq 'Running' } | Select-Object -First 20 Name, DisplayName";
                break;
            case "ClearTemp":
                SelectedScriptType = ScriptType.Batch;
                ScriptContent = "@echo off\ndel /q/f/s %TEMP%\\* 2>nul\necho Temporary files cleared.";
                break;
            case "PingTest":
                SelectedScriptType = ScriptType.PowerShell;
                ScriptContent = "Test-Connection -ComputerName 8.8.8.8 -Count 3 | Select-Object Address, ResponseTime";
                break;
        }
    }

    private async Task ExecuteAsync()
    {
        var targets = SelectedTargetMode == "Selected"
            ? _allComputers.Where(c => c.IsSelected && c.IsOnline).ToList()
            : _allComputers.Where(c => c.IsOnline).ToList();

        if (targets.Count == 0)
        {
            StatusText = "Tidak ada target komputer online yang dipilih.";
            return;
        }

        IsExecuting = true;
        Results.Clear();
        StatusText = $"Mengeksekusi script pada {targets.Count} komputer...";

        var payload = new ScriptPayload
        {
            Type = SelectedScriptType,
            ScriptContent = ScriptContent,
            TimeoutSeconds = 60
        };

        var command = new CommandMessage
        {
            Type = CommandType.ExecuteScript,
            Payload = MessageSerializer.Serialize(payload)
        };

        var resultItems = targets.Select(t => new ScriptExecutionResultItem
        {
            Hostname = t.Hostname,
            Ip = t.IpAddress,
            Status = "Menjalankan..."
        }).ToList();

        foreach (var item in resultItems)
        {
            Results.Add(item);
        }

        var tasks = targets.Zip(resultItems, async (pc, item) =>
        {
            var res = await _commandService.SendCommandAsync(pc.IpAddress, command, timeoutMs: 65000);
            item.IsSuccess = res.Success;
            item.Status = res.Success ? "Selesai" : "Gagal";
            item.Output = res.Output ?? string.Empty;
            item.Error = res.Error ?? string.Empty;
            item.ExecutionTimeMs = res.ExecutionTimeMs;

            await _databaseService.LogActivityAsync(
                pc.Hostname,
                $"Script ({SelectedScriptType})",
                res.Success ? $"Berhasil ({res.ExecutionTimeMs}ms)" : $"Error: {res.Error}",
                res.Success);
        });

        await Task.WhenAll(tasks);

        int succeeded = resultItems.Count(r => r.IsSuccess);
        StatusText = $"Eksekusi selesai. Berhasil: {succeeded}/{targets.Count}.";
        IsExecuting = false;
    }
}
