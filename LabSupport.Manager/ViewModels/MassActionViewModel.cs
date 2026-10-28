using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LabSupport.Manager.Models;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Manager.ViewModels;

public class MassActionResultItem : ViewModelBase
{
    private string _hostname = string.Empty;
    private string _ipAddress = string.Empty;
    private string _roomName = string.Empty;
    private bool _success;
    private string _message = string.Empty;
    private bool _isPending = true;

    public string Hostname { get => _hostname; set => SetProperty(ref _hostname, value); }
    public string IpAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value); }
    public string RoomName { get => _roomName; set => SetProperty(ref _roomName, value); }
    public bool Success
    {
        get => _success;
        set
        {
            if (SetProperty(ref _success, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }
    public string Message { get => _message; set => SetProperty(ref _message, value); }
    public bool IsPending
    {
        get => _isPending;
        set
        {
            if (SetProperty(ref _isPending, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    public string StatusText => IsPending ? "Memproses..." : (Success ? "Sukses" : "Gagal");
    public string StatusBrush => IsPending ? "#94A3B8" : (Success ? "#10B981" : "#EF4444");
}

public class MassActionViewModel : ViewModelBase
{
    private readonly AgentCommandService _commandService;
    private readonly WolService _wolService;
    private readonly DatabaseService _databaseService;
    private readonly IReadOnlyList<LabComputer> _allTargetComputers;

    private int _selectedTabIndex;
    private bool _isExecuting;
    private double _executionProgress;
    private string _executionSummary = string.Empty;
    private bool _hasFailedPcs;
    private Func<IReadOnlyList<LabComputer>, Task>? _lastBatchDispatcher;

    // Black Screen Tab
    private string _customAttentionMessage = "PERHATIKAN GURU";

    // Script Tab
    private MassScriptTemplateItem? _selectedTemplate;
    private ScriptType _scriptType = ScriptType.PowerShell;
    private string _scriptContent = string.Empty;

    // Keyboard Tab
    private string _broadcastText = string.Empty;

    // Launcher Tab
    private string _launchTarget = string.Empty;
    private string _launchArguments = string.Empty;

    // Maintenance Tab
    private string _killProcessName = string.Empty;

    public IReadOnlyList<LabComputer> TargetComputers => _allTargetComputers;
    public string TargetSummary { get; }
    public int TotalTargetCount => _allTargetComputers.Count;
    public int OnlineTargetCount => _allTargetComputers.Count(c => c.IsOnline);

    public ObservableCollection<MassActionResultItem> Results { get; } = new();
    public ObservableCollection<MassScriptTemplateItem> AvailableTemplates { get; } = new();

    public Func<string, string, bool>? ConfirmationHandler { get; set; }
    public Action? RequestClose { get; set; }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        set => SetProperty(ref _isExecuting, value);
    }

    public double ExecutionProgress
    {
        get => _executionProgress;
        set => SetProperty(ref _executionProgress, value);
    }

    public string ExecutionSummary
    {
        get => _executionSummary;
        set => SetProperty(ref _executionSummary, value);
    }

    public bool HasFailedPcs
    {
        get => _hasFailedPcs;
        set => SetProperty(ref _hasFailedPcs, value);
    }

    public string CustomAttentionMessage
    {
        get => _customAttentionMessage;
        set => SetProperty(ref _customAttentionMessage, value);
    }

    public MassScriptTemplateItem? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value) && value != null)
            {
                ScriptType = value.Type;
                ScriptContent = value.ScriptContent;
            }
        }
    }

    public ScriptType ScriptType
    {
        get => _scriptType;
        set
        {
            if (SetProperty(ref _scriptType, value))
            {
                OnPropertyChanged(nameof(IsPowerShell));
                OnPropertyChanged(nameof(IsBatch));
            }
        }
    }

    public bool IsPowerShell
    {
        get => ScriptType == ScriptType.PowerShell;
        set { if (value) ScriptType = ScriptType.PowerShell; }
    }

    public bool IsBatch
    {
        get => ScriptType == ScriptType.Batch;
        set { if (value) ScriptType = ScriptType.Batch; }
    }

    public string ScriptContent
    {
        get => _scriptContent;
        set => SetProperty(ref _scriptContent, value);
    }

    public string BroadcastText
    {
        get => _broadcastText;
        set => SetProperty(ref _broadcastText, value);
    }

    public string LaunchTarget
    {
        get => _launchTarget;
        set => SetProperty(ref _launchTarget, value);
    }

    public string LaunchArguments
    {
        get => _launchArguments;
        set => SetProperty(ref _launchArguments, value);
    }

    public string KillProcessName
    {
        get => _killProcessName;
        set => SetProperty(ref _killProcessName, value);
    }

    // Commands
    public ICommand WakeAllCommand { get; }
    public ICommand SleepAllCommand { get; }
    public ICommand RestartAllCommand { get; }
    public ICommand ShutdownAllCommand { get; }

    public ICommand EnableBlackScreenCommand { get; }
    public ICommand DisableBlackScreenCommand { get; }
    public ICommand LockAttentionCommand { get; }

    public ICommand ExecuteScriptCommand { get; }

    public ICommand SendWinDCommand { get; }
    public ICommand SendAltF4Command { get; }
    public ICommand SendWinLCommand { get; }
    public ICommand SendEnterCommand { get; }
    public ICommand SendEscCommand { get; }
    public ICommand SendBroadcastTextCommand { get; }

    public ICommand ExecuteLaunchCommand { get; }

    public ICommand CleanWorkspaceCommand { get; }
    public ICommand ForceKillProcessCommand { get; }

    public ICommand RetryFailedCommand { get; }
    public ICommand CloseCommand { get; }

    public MassActionViewModel(
        IEnumerable<LabComputer> targetComputers,
        AgentCommandService commandService,
        WolService wolService,
        DatabaseService databaseService)
    {
        _allTargetComputers = targetComputers.ToList();
        _commandService = commandService;
        _wolService = wolService;
        _databaseService = databaseService;

        TargetSummary = SelectionService.FormatSelectionSummary(_allTargetComputers);

        foreach (var tmpl in MassActionTemplates.GetAllTemplates())
        {
            AvailableTemplates.Add(tmpl);
        }
        if (AvailableTemplates.Count > 0)
        {
            SelectedTemplate = AvailableTemplates[0];
        }

        // Power Commands
        WakeAllCommand = new AsyncRelayCommand(ExecuteWakeAllAsync, () => !IsExecuting);
        SleepAllCommand = new AsyncRelayCommand(ExecuteSleepAllAsync, () => !IsExecuting);
        RestartAllCommand = new AsyncRelayCommand(ExecuteRestartAllAsync, () => !IsExecuting);
        ShutdownAllCommand = new AsyncRelayCommand(ExecuteShutdownAllAsync, () => !IsExecuting);

        // Black Screen Commands
        EnableBlackScreenCommand = new AsyncRelayCommand(() => ExecuteBlackScreenAsync(true), () => !IsExecuting);
        DisableBlackScreenCommand = new AsyncRelayCommand(() => ExecuteBlackScreenAsync(false), () => !IsExecuting);
        LockAttentionCommand = new AsyncRelayCommand(ExecuteLockAttentionAsync, () => !IsExecuting);

        // Script Command
        ExecuteScriptCommand = new AsyncRelayCommand(ExecuteMassScriptAsync, () => !IsExecuting);

        // Keyboard Commands
        SendWinDCommand = new AsyncRelayCommand(() => ExecuteShortcutAsync("WIN+D"), () => !IsExecuting);
        SendAltF4Command = new AsyncRelayCommand(() => ExecuteShortcutAsync("ALT+F4"), () => !IsExecuting);
        SendWinLCommand = new AsyncRelayCommand(() => ExecuteShortcutAsync("WIN+L"), () => !IsExecuting);
        SendEnterCommand = new AsyncRelayCommand(() => ExecuteShortcutAsync("ENTER"), () => !IsExecuting);
        SendEscCommand = new AsyncRelayCommand(() => ExecuteShortcutAsync("ESC"), () => !IsExecuting);
        SendBroadcastTextCommand = new AsyncRelayCommand(ExecuteBroadcastTextAsync, () => !IsExecuting);

        // Launch Command
        ExecuteLaunchCommand = new AsyncRelayCommand(ExecuteMassLaunchAsync, () => !IsExecuting);

        // Maintenance Commands
        CleanWorkspaceCommand = new AsyncRelayCommand(ExecuteCleanWorkspaceAsync, () => !IsExecuting);
        ForceKillProcessCommand = new AsyncRelayCommand(ExecuteForceKillProcessAsync, () => !IsExecuting);

        // Retry & Close
        RetryFailedCommand = new AsyncRelayCommand(ExecuteRetryFailedAsync, () => !IsExecuting && HasFailedPcs);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    private bool Confirm(string title, string message)
    {
        if (ConfirmationHandler != null)
        {
            return ConfirmationHandler(title, message);
        }

        if (Application.Current != null)
        {
            var res = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return res == MessageBoxResult.Yes;
        }

        return true;
    }

    private void SetupResultsList(IEnumerable<LabComputer> targets)
    {
        Results.Clear();
        foreach (var pc in targets)
        {
            Results.Add(new MassActionResultItem
            {
                Hostname = pc.Hostname,
                IpAddress = pc.IpAddress,
                RoomName = pc.RoomName,
                IsPending = true
            });
        }
        ExecutionProgress = 0;
        ExecutionSummary = $"Memproses 0/{Results.Count} PC...";
        HasFailedPcs = false;
    }

    private async Task DispatchBatchCommandAsync(
        IReadOnlyList<LabComputer> targets,
        CommandMessage command,
        string activityName,
        bool isRetry = false)
    {
        IsExecuting = true;
        if (!isRetry)
        {
            SetupResultsList(targets);
        }
        else
        {
            var targetHostnames = targets.Select(t => t.Hostname).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Results)
            {
                if (targetHostnames.Contains(r.Hostname))
                {
                    r.IsPending = true;
                    r.Message = "Mengulang pengiriman...";
                }
            }
            ExecutionProgress = 0;
            ExecutionSummary = $"Mengulang pengiriman ke {targets.Count} PC gagal...";
            HasFailedPcs = false;
        }

        _lastBatchDispatcher = (failedTargets) => DispatchBatchCommandAsync(failedTargets, command, activityName, isRetry: true);

        int totalToProcess = targets.Count;
        int completedCount = 0;
        object countLock = new();

        try
        {
            var tasks = targets.Select(async pc =>
            {
                var resItem = Results.FirstOrDefault(r => r.Hostname == pc.Hostname && r.IpAddress == pc.IpAddress);

                CommandResponse resp;
                try
                {
                    resp = await _commandService.SendCommandAsync(pc.IpAddress, command, timeoutMs: 7000);
                }
                catch (Exception ex)
                {
                    resp = new CommandResponse { Success = false, Error = ex.Message };
                }

                lock (countLock)
                {
                    completedCount++;
                    ExecutionProgress = Math.Round((double)completedCount / totalToProcess * 100, 0);
                }

                if (resItem != null)
                {
                    resItem.IsPending = false;
                    resItem.Success = resp.Success;
                    resItem.Message = resp.Success ? (resp.Output ?? "Sukses") : (resp.Error ?? "Gagal");
                }

                await _databaseService.LogActivityAsync(
                    pc.Hostname,
                    activityName,
                    resp.Success ? (resp.Output ?? "Sukses") : (resp.Error ?? "Gagal"),
                    resp.Success);
            }).ToList();

            await Task.WhenAll(tasks);

            int totalSuccess = Results.Count(r => r.Success && !r.IsPending);
            int totalFailed = Results.Count(r => !r.Success && !r.IsPending);
            HasFailedPcs = totalFailed > 0;
            ExecutionSummary = $"Selesai: {totalSuccess}/{Results.Count} PC Berhasil" + (totalFailed > 0 ? $", {totalFailed} PC Gagal." : ".");
            CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            IsExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task ExecuteWakeAllAsync()
    {
        var offlineTargets = _allTargetComputers.Where(c => !string.IsNullOrWhiteSpace(c.MacAddress)).ToList();
        if (offlineTargets.Count == 0)
        {
            offlineTargets = _allTargetComputers.ToList();
        }

        await ExecuteWakeBatchAsync(offlineTargets, isRetry: false);
    }

    private async Task ExecuteWakeBatchAsync(IReadOnlyList<LabComputer> targets, bool isRetry)
    {
        IsExecuting = true;
        if (!isRetry)
        {
            SetupResultsList(targets);
        }
        else
        {
            var targetHostnames = targets.Select(t => t.Hostname).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Results)
            {
                if (targetHostnames.Contains(r.Hostname))
                {
                    r.IsPending = true;
                    r.Message = "Mengulang WoL...";
                }
            }
            ExecutionProgress = 0;
            ExecutionSummary = $"Mengulang WoL ke {targets.Count} PC gagal...";
            HasFailedPcs = false;
        }

        _lastBatchDispatcher = (failedTargets) => ExecuteWakeBatchAsync(failedTargets, isRetry: true);

        int totalToProcess = targets.Count;
        int completedCount = 0;
        object countLock = new();

        try
        {
            var tasks = targets.Select(async pc =>
            {
                var resItem = Results.FirstOrDefault(r => r.Hostname == pc.Hostname);
                bool ok = false;
                string msg = string.Empty;

                if (!string.IsNullOrWhiteSpace(pc.MacAddress))
                {
                    ok = await _wolService.WakeComputerAsync(pc.Hostname, pc.MacAddress, pc.IpAddress);
                    msg = ok ? "Magic Packet WoL terkirim." : "Gagal mengirim WoL packet.";
                }
                else
                {
                    msg = "MAC Address tidak tersedia.";
                }

                lock (countLock)
                {
                    completedCount++;
                    ExecutionProgress = Math.Round((double)completedCount / totalToProcess * 100, 0);
                }

                if (resItem != null)
                {
                    resItem.IsPending = false;
                    resItem.Success = ok;
                    resItem.Message = msg;
                }

                await _databaseService.LogActivityAsync(pc.Hostname, "WakeOnLan", msg, ok);
            }).ToList();

            await Task.WhenAll(tasks);

            int totalSuccess = Results.Count(r => r.Success && !r.IsPending);
            int totalFailed = Results.Count(r => !r.Success && !r.IsPending);
            HasFailedPcs = totalFailed > 0;
            ExecutionSummary = $"WoL Broadcast: {totalSuccess}/{Results.Count} PC terkirim" + (totalFailed > 0 ? $", {totalFailed} PC Gagal." : ".");
            CommandManager.InvalidateRequerySuggested();
        }
        finally
        {
            IsExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task ExecuteSleepAllAsync()
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        if (!Confirm(
            "Konfirmasi Mode Standby / Sleep",
            $"Anda akan menidurkan (Sleep / Standby) {onlineTargets.Count} PC terpilih secara serentak.\nPC dapat dibangunkan kembali dengan Wake-on-LAN.\n\nLanjutkan?"))
        {
            return;
        }

        var cmd = new CommandMessage { Type = CommandType.Sleep };
        await DispatchBatchCommandAsync(onlineTargets, cmd, "MassSleep");
    }

    public async Task ExecuteRestartAllAsync()
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        if (!Confirm(
            "Konfirmasi Restart Masal",
            $"Anda akan me-restart {onlineTargets.Count} PC terpilih.\n\nLanjutkan?"))
        {
            return;
        }

        var cmd = new CommandMessage { Type = CommandType.Restart };
        await DispatchBatchCommandAsync(onlineTargets, cmd, "MassRestart");
    }

    public async Task ExecuteShutdownAllAsync()
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        if (!Confirm(
            "Konfirmasi Shutdown Masal",
            $"⚠️ PERINGATAN: Anda akan mematikan {onlineTargets.Count} PC terpilih sekarang.\n\nLanjutkan?"))
        {
            return;
        }

        var cmd = new CommandMessage { Type = CommandType.Shutdown };
        await DispatchBatchCommandAsync(onlineTargets, cmd, "MassShutdown");
    }

    public async Task ExecuteBlackScreenAsync(bool enable)
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        var cmd = new CommandMessage
        {
            Type = CommandType.SetBlackScreen,
            Payload = MessageSerializer.Serialize(new BlackScreenPayload { Enable = enable })
        };

        await DispatchBatchCommandAsync(onlineTargets, cmd, enable ? "EnableBlackScreen" : "DisableBlackScreen");
    }

    public async Task ExecuteLockAttentionAsync()
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        string msg = string.IsNullOrWhiteSpace(CustomAttentionMessage) ? "PERHATIKAN GURU" : CustomAttentionMessage;
        var cmd = new CommandMessage
        {
            Type = CommandType.LockAttention,
            Payload = MessageSerializer.Serialize(new AttentionPayload { Lock = true, Message = msg })
        };

        await DispatchBatchCommandAsync(onlineTargets, cmd, "LockAttention");
    }

    public async Task ExecuteMassScriptAsync()
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ScriptContent))
        {
            ExecutionSummary = "Isi script tidak boleh kosong.";
            return;
        }

        var payload = new ScriptPayload
        {
            Type = ScriptType,
            ScriptContent = ScriptContent,
            TimeoutSeconds = 60
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.ExecuteScript,
            Payload = MessageSerializer.Serialize(payload)
        };

        await DispatchBatchCommandAsync(onlineTargets, cmd, $"ExecuteScript_{ScriptType}");
    }

    public async Task ExecuteShortcutAsync(string key)
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        var cmd = new CommandMessage
        {
            Type = CommandType.KeyboardEvent,
            Payload = MessageSerializer.Serialize(new KeyboardInputPayload
            {
                Action = KeyboardAction.Shortcut,
                Key = key
            })
        };

        await DispatchBatchCommandAsync(onlineTargets, cmd, $"KeyboardShortcut_{key}");
    }

    public async Task ExecuteBroadcastTextAsync()
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        if (string.IsNullOrEmpty(BroadcastText))
        {
            ExecutionSummary = "Teks siaran tidak boleh kosong.";
            return;
        }

        var cmd = new CommandMessage
        {
            Type = CommandType.KeyboardEvent,
            Payload = MessageSerializer.Serialize(new KeyboardInputPayload
            {
                Action = KeyboardAction.Text,
                Text = BroadcastText
            })
        };

        await DispatchBatchCommandAsync(onlineTargets, cmd, "BroadcastTextTyping");
    }

    public async Task ExecuteMassLaunchAsync()
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        if (string.IsNullOrWhiteSpace(LaunchTarget))
        {
            ExecutionSummary = "Target URL atau nama aplikasi tidak boleh kosong.";
            return;
        }

        // Defensive sanitization: reject command chaining characters
        char[] forbidden = { '&', '|', ';', '<', '>', '^', '%', '\r', '\n' };
        if (LaunchTarget.IndexOfAny(forbidden) >= 0 || LaunchArguments.IndexOfAny(forbidden) >= 0)
        {
            ExecutionSummary = "Karakter tidak diizinkan (&, |, ;, dll) terdeteksi pada target atau argumen.";
            return;
        }

        var cmd = new CommandMessage
        {
            Type = CommandType.LaunchProcess,
            Payload = MessageSerializer.Serialize(new LaunchPayload
            {
                Target = LaunchTarget.Trim(),
                Arguments = string.IsNullOrWhiteSpace(LaunchArguments) ? null : LaunchArguments.Trim()
            })
        };

        await DispatchBatchCommandAsync(onlineTargets, cmd, "MassLaunch");
    }

    public async Task ExecuteCleanWorkspaceAsync()
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        if (!Confirm(
            "Konfirmasi Bersihkan Workspace Masal",
            $"Bersihkan folder Downloads, file Temp, Recycle Bin, dan dokumen desktop siswa pada {onlineTargets.Count} PC terpilih?\n\nShortcut aplikasi (.lnk) tetap terlindungi.\nLanjutkan?"))
        {
            return;
        }

        var cmd = new CommandMessage
        {
            Type = CommandType.CleanWorkspace,
            Payload = MessageSerializer.Serialize(new CleanWorkspacePayload())
        };

        await DispatchBatchCommandAsync(onlineTargets, cmd, "MassCleanWorkspace");
    }

    public async Task ExecuteForceKillProcessAsync()
    {
        var onlineTargets = _allTargetComputers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count == 0)
        {
            ExecutionSummary = "Tidak ada komputer online yang dipilih.";
            return;
        }

        if (string.IsNullOrWhiteSpace(KillProcessName))
        {
            ExecutionSummary = "Nama proses yang ingin dihentikan tidak boleh kosong.";
            return;
        }

        string procName = KillProcessName.Trim();
        if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            procName = procName.Substring(0, procName.Length - 4);
        }

        // Security check: Protect critical Windows OS components from being killed
        string[] criticalProcesses = { "csrss", "winlogon", "services", "lsass", "smss", "svchost", "system", "idle" };
        if (criticalProcesses.Contains(procName, StringComparer.OrdinalIgnoreCase))
        {
            ExecutionSummary = $"⚠️ Proses '{procName}' adalah komponen inti Windows dan dilindungi dari pematian.";
            return;
        }

        if (!Confirm(
            "Konfirmasi Hentikan Proses Masal",
            $"⚠️ Hentikan paksa seluruh proses '{procName}' pada {onlineTargets.Count} PC terpilih?\n\nAplikasi yang sedang berjalan mungkin kehilangan data yang belum tersimpan.\nLanjutkan?"))
        {
            return;
        }

        var cmd = new CommandMessage
        {
            Type = CommandType.KillProcess,
            Payload = procName
        };

        await DispatchBatchCommandAsync(onlineTargets, cmd, $"MassKillProcess_{procName}");
    }

    public async Task ExecuteRetryFailedAsync()
    {
        if (_lastBatchDispatcher == null) return;

        var failedHostnames = Results
            .Where(r => !r.Success && !r.IsPending)
            .Select(r => r.Hostname)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (failedHostnames.Count == 0)
        {
            ExecutionSummary = "Tidak ada PC gagal untuk dikirim ulang.";
            return;
        }

        var retryTargets = _allTargetComputers
            .Where(c => failedHostnames.Contains(c.Hostname))
            .ToList();

        if (retryTargets.Count == 0)
        {
            ExecutionSummary = "Target PC gagal tidak ditemukan.";
            return;
        }

        await _lastBatchDispatcher(retryTargets);
    }
}
