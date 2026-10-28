using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using LabSupport.Manager.Models;
using LabSupport.Shared.Models;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Manager.ViewModels;

public class DashboardViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly AgentCommandService _commandService;
    private readonly WolService _wolService;
    private readonly HttpFileServer? _httpFileServer;
    private readonly DispatcherTimer _offlineCheckTimer;
    private FileTransferSession? _activeDistributionSession;
    private Action<string, int>? _distributionChunkHandler;

    private string _searchText = string.Empty;
    private string _selectedFilter = "All"; // All, Online, Offline
    private string _selectedGroupFilter = "All Groups";
    private LabComputer? _selectedComputer;
    private string _statusMessage = "Siap.";
    private bool _isBroadcasting;
    private string _currentManagerId = string.Empty;
    private bool _isFloorPlanViewActive;
    private bool _isAppGuardActive;
    private string _appGuardBlacklist = "steam,roblox,discord,cheatengine,epicgames,valorant,bittorrent,spotify,telegram";
    private bool _isDailyShutdownEnabled;
    private TimeSpan _dailyShutdownTime = new TimeSpan(17, 0, 0);
    private DateTime _lastDailyShutdownDate = DateTime.MinValue;

    public ObservableCollection<LabComputer> Computers { get; } = new();
    public ObservableCollection<string> AvailableGroups { get; } = new() { "All Groups" };
    public ICollectionView FilteredComputers { get; }

    public event Action<LabComputer>? OnOpenRemoteRequested;
    public event Action<LabComputer>? OnOpenScriptRequested;

    public string CurrentManagerId
    {
        get => _currentManagerId;
        set => SetProperty(ref _currentManagerId, value);
    }

    public bool IsBroadcasting
    {
        get => _isBroadcasting;
        set => SetProperty(ref _isBroadcasting, value);
    }

    public bool IsFloorPlanViewActive
    {
        get => _isFloorPlanViewActive;
        set => SetProperty(ref _isFloorPlanViewActive, value);
    }

    public bool IsAppGuardActive
    {
        get => _isAppGuardActive;
        set => SetProperty(ref _isAppGuardActive, value);
    }

    public string AppGuardBlacklist
    {
        get => _appGuardBlacklist;
        set => SetProperty(ref _appGuardBlacklist, value);
    }

    public bool IsDailyShutdownEnabled
    {
        get => _isDailyShutdownEnabled;
        set => SetProperty(ref _isDailyShutdownEnabled, value);
    }

    public TimeSpan DailyShutdownTime
    {
        get => _dailyShutdownTime;
        set => SetProperty(ref _dailyShutdownTime, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                FilteredComputers.Refresh();
            }
        }
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                FilteredComputers.Refresh();
            }
        }
    }

    public string SelectedGroupFilter
    {
        get => _selectedGroupFilter;
        set
        {
            if (SetProperty(ref _selectedGroupFilter, value))
            {
                FilteredComputers.Refresh();
            }
        }
    }

    private string _selectedRoomFilter = "Semua Ruang";
    public ObservableCollection<LabRoom> AvailableRooms { get; } = new();

    public string SelectedRoomFilter
    {
        get => _selectedRoomFilter;
        set
        {
            if (SetProperty(ref _selectedRoomFilter, value))
            {
                foreach (var r in AvailableRooms)
                {
                    r.IsSelected = string.Equals(r.Name, value, StringComparison.OrdinalIgnoreCase);
                }
                FilteredComputers.Refresh();
                OnPropertyChanged(nameof(IsAllRoomsSelected));
            }
        }
    }

    public bool IsAllRoomsSelected => string.Equals(SelectedRoomFilter, "Semua Ruang", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(SelectedRoomFilter, "All Rooms", StringComparison.OrdinalIgnoreCase);

    public LabComputer? SelectedComputer
    {
        get => _selectedComputer;
        set => SetProperty(ref _selectedComputer, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public int TotalCount => Computers.Count;
    public int OnlineCount => Computers.Count(c => c.IsOnline);
    public int OfflineCount => Computers.Count(c => !c.IsOnline);
    public int SelectedCount => Computers.Count(c => c.IsSelected);
    public string MassActionButtonText => SelectedCount > 0 ? $"🚀 Aktivitas Masal ({SelectedCount})" : "🚀 Aktivitas Masal";

    private string _targetSummary = "0 PC Terpilih";
    public string TargetSummary
    {
        get => _targetSummary;
        set => SetProperty(ref _targetSummary, value);
    }

    private bool _isMultiLabDialogVisible;
    public bool IsMultiLabDialogVisible
    {
        get => _isMultiLabDialogVisible;
        set => SetProperty(ref _isMultiLabDialogVisible, value);
    }

    public ObservableCollection<RoomSelectionItemViewModel> MultiLabSelectionList { get; } = new();

    public Func<MassActionViewModel, bool?>? MassActionDialogHandler { get; set; }

    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand InvertSelectionCommand { get; }
    public ICommand SelectRoomCommand { get; }
    public ICommand DeselectRoomCommand { get; }
    public ICommand OpenMultiLabSelectionCommand { get; }
    public ICommand ApplyMultiLabSelectionCommand { get; }
    public ICommand CloseMultiLabSelectionCommand { get; }
    public ICommand OpenMassActionCommand { get; }
    public ICommand ShutdownSelectedCommand { get; }
    public ICommand RestartSelectedCommand { get; }
    public ICommand LockSelectedCommand { get; }
    public ICommand WakeSelectedCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenRemoteCommand { get; }
    public ICommand OpenScriptCommand { get; }
    public ICommand StartBroadcastCommand { get; }
    public ICommand StopBroadcastCommand { get; }
    public ICommand StartStudentBroadcastCommand { get; }
    public ICommand ExportInstallerCommand { get; }
    public ICommand AssignRoomCommand { get; }
    public ICommand FilterByRoomCommand { get; }

    // Feature 1: Screen & Input Lock ("Perhatikan Guru")
    public ICommand LockAttentionSelectedCommand { get; }
    public ICommand UnlockAttentionSelectedCommand { get; }

    // Feature 2: Audio Control & Mute All
    public ICommand MuteAllCommand { get; }
    public ICommand UnmuteAllCommand { get; }

    // Feature 3: Exam Mode / Lockdown
    public ICommand EnableExamModeCommand { get; }
    public ICommand DisableExamModeCommand { get; }

    // Feature 4: USB Storage Blocker
    public ICommand BlockUsbSelectedCommand { get; }
    public ICommand UnblockUsbSelectedCommand { get; }

    // Feature 5: Selective Internet Blocker
    public ICommand BlockInternetSelectedCommand { get; }
    public ICommand UnblockInternetSelectedCommand { get; }

    // Feature 6: Launch Application & Website Broadcast
    public ICommand LaunchAppOrUrlCommand { get; }

    // Feature 7: File Distribution & Collection
    public ICommand DistributeFileCommand { get; }
    public ICommand CollectFilesCommand { get; }

    // Feature 9: Group Management
    public ICommand AssignGroupCommand { get; }

    // Feature 10: Activity Log & Telemetry Export
    public ICommand ExportTelemetryCommand { get; }

    // Feature 11: Floor Plan & Seating View
    public ICommand ToggleViewModeCommand { get; }
    public ICommand AutoArrangeFloorPlanCommand { get; }

    // Feature 12: Real-time Application Blacklist & Auto-Kill Guard
    public ICommand ToggleAppGuardCommand { get; }

    // Feature 13: Clipboard & Text Broadcast
    public ICommand BroadcastClipboardCommand { get; }

    // Feature 14: Single-PC Wake-on-LAN
    public ICommand WakeSingleComputerCommand { get; }

    // Feature 15: 1-Click Workspace & Storage Cleaner
    public ICommand CleanWorkspaceCommand { get; }

    // Feature 16: Adaptive Streaming & Quality Profiles
    private string _selectedStreamQuality = "Balanced (900p 15fps)";
    public string SelectedStreamQuality
    {
        get => _selectedStreamQuality;
        set => SetProperty(ref _selectedStreamQuality, value);
    }
    public string[] AvailableStreamQualities { get; } = new[]
    {
        "Eco (720p 8fps)",
        "Balanced (900p 15fps)",
        "High (1080p 25fps)"
    };

    // Feature 17: Selective Web Filtering
    public ICommand SetWebFilterCommand { get; }
    public ICommand ClearWebFilterCommand { get; }

    // Feature 19: Push Notification & Connection Health Check
    public ICommand SendNotificationCommand { get; }
    public ICommand CheckAgentConnectionCommand { get; }

    // Feature 18: Hardware Inventory & Diagnostics
    private LabComputer? _selectedComputerHardware;
    public LabComputer? SelectedComputerHardware
    {
        get => _selectedComputerHardware;
        set => SetProperty(ref _selectedComputerHardware, value);
    }

    private bool _isHardwareDialogVisible;
    public bool IsHardwareDialogVisible
    {
        get => _isHardwareDialogVisible;
        set => SetProperty(ref _isHardwareDialogVisible, value);
    }

    public ICommand ShowHardwareDetailsCommand { get; }
    public ICommand CloseHardwareDialogCommand { get; }
    public ICommand ExportHardwareInventoryCommand { get; }
    public ICommand ThrottleBandwidthHogCommand { get; }

    private bool _isFileTransferModalVisible;
    public bool IsFileTransferModalVisible
    {
        get => _isFileTransferModalVisible;
        set => SetProperty(ref _isFileTransferModalVisible, value);
    }

    private FileTransferProgressViewModel? _transferProgressVM;
    public FileTransferProgressViewModel? TransferProgressVM
    {
        get => _transferProgressVM;
        set => SetProperty(ref _transferProgressVM, value);
    }

    public Func<FileDistributionOptions?>? FileDistributionDialogHandler { get; set; }
    public ICommand CloseFileTransferModalCommand { get; }

    // Feature 19: Agent Lifecycle & Two-Way Uninstallation
    public ICommand DeleteComputerCommand { get; }
    public ICommand BatchDeleteComputersCommand { get; }
    public ICommand ExportUninstallerCommand { get; }
    public Func<string, string, bool>? ConfirmationDialogHandler { get; set; }

    // Dialog handlers for MVVM decoupling and testing
    public Func<string?>? SaveFileDialogHandler { get; set; }
    public Func<string?>? OpenFileDialogHandler { get; set; }
    public Func<string?>? FolderBrowserDialogHandler { get; set; }
    public Func<string, string, string?>? PromptDialogHandler { get; set; }

    public DashboardViewModel(
        DatabaseService databaseService,
        AgentCommandService commandService,
        WolService wolService,
        HttpFileServer? httpFileServer = null)
    {
        _databaseService = databaseService;
        _commandService = commandService;
        _wolService = wolService;
        _httpFileServer = httpFileServer;

        FilteredComputers = CollectionViewSource.GetDefaultView(Computers);
        FilteredComputers.Filter = FilterComputerItem;
        FilteredComputers.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LabComputer.RoomName)));

        AssignRoomCommand = new AsyncRelayCommand(p => AssignRoomSelectedAsync(p as string));
        FilterByRoomCommand = new RelayCommand(p =>
        {
            if (p is string roomName) SelectedRoomFilter = roomName;
            else if (p is LabRoom room) SelectedRoomFilter = room.Name;
        });

        SelectAllCommand = new RelayCommand(() => SetSelectionAll(true));
        DeselectAllCommand = new RelayCommand(() => SetSelectionAll(false));
        InvertSelectionCommand = new RelayCommand(InvertSelection);
        SelectRoomCommand = new RelayCommand(p => SelectRoom(p as string));
        DeselectRoomCommand = new RelayCommand(p => DeselectRoom(p as string));
        OpenMultiLabSelectionCommand = new RelayCommand(OpenMultiLabSelection);
        ApplyMultiLabSelectionCommand = new RelayCommand(ApplyMultiLabSelection);
        CloseMultiLabSelectionCommand = new RelayCommand(CloseMultiLabSelection);
        OpenMassActionCommand = new RelayCommand(OpenMassAction);

        Computers.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (LabComputer c in e.NewItems)
                {
                    c.PropertyChanged += (ps, pe) =>
                    {
                        if (pe.PropertyName == nameof(LabComputer.IsSelected))
                        {
                            UpdateSelectionSummary();
                        }
                    };
                }
            }
            UpdateSelectionSummary();
        };
        ShutdownSelectedCommand = new AsyncRelayCommand(ShutdownSelectedAsync);
        RestartSelectedCommand = new AsyncRelayCommand(RestartSelectedAsync);
        LockSelectedCommand = new AsyncRelayCommand(LockSelectedAsync);
        WakeSelectedCommand = new AsyncRelayCommand(WakeSelectedAsync);
        RefreshCommand = new AsyncRelayCommand(LoadComputersFromDbAsync);
        StartBroadcastCommand = new AsyncRelayCommand(StartBroadcastAsync);
        StopBroadcastCommand = new AsyncRelayCommand(StopBroadcastAsync);
        StartStudentBroadcastCommand = new AsyncRelayCommand(StartStudentBroadcastAsync);
        ExportInstallerCommand = new AsyncRelayCommand(ExportInstallerAsync);

        LockAttentionSelectedCommand = new AsyncRelayCommand(LockAttentionSelectedAsync);
        UnlockAttentionSelectedCommand = new AsyncRelayCommand(UnlockAttentionSelectedAsync);

        MuteAllCommand = new AsyncRelayCommand(MuteAllAsync);
        UnmuteAllCommand = new AsyncRelayCommand(UnmuteAllAsync);

        EnableExamModeCommand = new AsyncRelayCommand(EnableExamModeSelectedAsync);
        DisableExamModeCommand = new AsyncRelayCommand(DisableExamModeSelectedAsync);

        BlockUsbSelectedCommand = new AsyncRelayCommand(() => BlockUsbSelectedAsync(true));
        UnblockUsbSelectedCommand = new AsyncRelayCommand(() => BlockUsbSelectedAsync(false));

        BlockInternetSelectedCommand = new AsyncRelayCommand(() => BlockInternetSelectedAsync(true));
        UnblockInternetSelectedCommand = new AsyncRelayCommand(() => BlockInternetSelectedAsync(false));

        LaunchAppOrUrlCommand = new AsyncRelayCommand(p => LaunchAppOrUrlSelectedAsync(p as string));
        DistributeFileCommand = new AsyncRelayCommand(DistributeFileAsync);
        CollectFilesCommand = new AsyncRelayCommand(CollectFilesFromStudentsAsync);
        AssignGroupCommand = new AsyncRelayCommand(p => AssignGroupSelectedAsync(p as string));
        ExportTelemetryCommand = new AsyncRelayCommand(ExportTelemetryAsync);

        ToggleViewModeCommand = new RelayCommand(() =>
        {
            IsFloorPlanViewActive = !IsFloorPlanViewActive;
        });

        AutoArrangeFloorPlanCommand = new RelayCommand(() =>
        {
            AutoArrangeComputers();
        });

        ToggleAppGuardCommand = new AsyncRelayCommand(ToggleAppGuardAsync);
        BroadcastClipboardCommand = new AsyncRelayCommand(BroadcastClipboardAsync);
        WakeSingleComputerCommand = new AsyncRelayCommand(p => WakeSingleComputerAsync(p as LabComputer));
        CleanWorkspaceCommand = new AsyncRelayCommand(CleanWorkspaceSelectedAsync);

        SetWebFilterCommand = new AsyncRelayCommand(p =>
        {
            if (p is WebFilterMode mode) return SetWebFilterSelectedAsync(mode);
            if (p is string s && Enum.TryParse<WebFilterMode>(s, true, out var parsedMode))
                return SetWebFilterSelectedAsync(parsedMode);
            return SetWebFilterSelectedAsync(WebFilterMode.Blacklist);
        });
        ClearWebFilterCommand = new AsyncRelayCommand(ClearWebFilterSelectedAsync);

        SendNotificationCommand = new AsyncRelayCommand(SendNotificationAsync);
        CheckAgentConnectionCommand = new AsyncRelayCommand(CheckAgentConnectionAsync);

        ShowHardwareDetailsCommand = new RelayCommand(p =>
        {
            var target = (p as LabComputer) ?? SelectedComputer;
            if (target != null)
            {
                SelectedComputerHardware = target;
                IsHardwareDialogVisible = true;
            }
        });
        CloseHardwareDialogCommand = new RelayCommand(() =>
        {
            IsHardwareDialogVisible = false;
        });

        CloseFileTransferModalCommand = new RelayCommand(() =>
        {
            IsFileTransferModalVisible = false;
            TransferProgressVM?.CancelTransfer();
            CleanupActiveDistributionSession();
        });

        ExportHardwareInventoryCommand = new AsyncRelayCommand(ExportHardwareInventoryAsync);
        ThrottleBandwidthHogCommand = new AsyncRelayCommand(p => ThrottleBandwidthHogAsync(p as LabComputer));

        if (_httpFileServer != null)
        {
            _httpFileServer.OnAgentUnregisterReceived += HandleAgentUnregistered;
            _httpFileServer.HeartbeatDirectiveEvaluator = HandleHeartbeatDirective;
        }

        DeleteComputerCommand = new AsyncRelayCommand(p => DeleteComputerAsync((p as LabComputer) ?? SelectedComputer));
        BatchDeleteComputersCommand = new AsyncRelayCommand(BatchDeleteComputersAsync);
        ExportUninstallerCommand = new AsyncRelayCommand(ExportUninstallerAsync);

        OpenRemoteCommand = new RelayCommand(p =>
        {
            if (p is LabComputer comp) OnOpenRemoteRequested?.Invoke(comp);
            else if (SelectedComputer != null) OnOpenRemoteRequested?.Invoke(SelectedComputer);
        });

        OpenScriptCommand = new RelayCommand(p =>
        {
            if (p is LabComputer comp) OnOpenScriptRequested?.Invoke(comp);
            else if (SelectedComputer != null) OnOpenScriptRequested?.Invoke(SelectedComputer);
        });

        _offlineCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _offlineCheckTimer.Tick += (_, _) =>
        {
            CheckOfflineComputers();
            _ = CheckPowerSchedulerAsync();
        };
        _offlineCheckTimer.Start();

        _ = LoadSettingsAsync();
    }

    private bool FilterComputerItem(object obj)
    {
        if (obj is not LabComputer pc) return false;

        if (SelectedFilter == "Online" && !pc.IsOnline) return false;
        if (SelectedFilter == "Offline" && pc.IsOnline) return false;

        if (!string.IsNullOrWhiteSpace(SelectedGroupFilter) &&
            !SelectedGroupFilter.Equals("All Groups", StringComparison.OrdinalIgnoreCase) &&
            !SelectedGroupFilter.Equals("Semua Grup", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(pc.GroupName, SelectedGroupFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedRoomFilter) &&
            !SelectedRoomFilter.Equals("Semua Ruang", StringComparison.OrdinalIgnoreCase) &&
            !SelectedRoomFilter.Equals("All Rooms", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(pc.RoomName, SelectedRoomFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        string query = SearchText.Trim().ToLowerInvariant();
        return pc.Hostname.ToLowerInvariant().Contains(query) ||
               pc.IpAddress.ToLowerInvariant().Contains(query) ||
               pc.MacAddress.ToLowerInvariant().Contains(query) ||
               pc.GroupName.ToLowerInvariant().Contains(query) ||
               pc.RoomName.ToLowerInvariant().Contains(query) ||
               (pc.DisplayName != null && pc.DisplayName.ToLowerInvariant().Contains(query));
    }

    public async Task StartBroadcastAsync()
    {
        var targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer online untuk broadcast.";
            return;
        }

        string localIp = NetworkHelper.GetPrimaryLanIPv4Address();
        int port = _httpFileServer?.Port ?? 8080;
        string streamUrl = $"http://{localIp}:{port}/presentation/view";

        if (_httpFileServer != null)
        {
            var screenCapture = new ScreenCaptureService();
            ulong lastFrameHash = 0;
            int width = 1600;
            int height = 900;
            int quality = 70;
            int interval = 66;

            if (SelectedStreamQuality.StartsWith("Eco", StringComparison.OrdinalIgnoreCase))
            {
                width = 1280;
                height = 720;
                quality = 55;
                interval = 125;
            }
            else if (SelectedStreamQuality.StartsWith("High", StringComparison.OrdinalIgnoreCase))
            {
                width = 1920;
                height = 1080;
                quality = 85;
                interval = 40;
            }

            _httpFileServer.StreamIntervalMs = interval;
            _httpFileServer.PresentationFrameProvider = () =>
            {
                return screenCapture.CaptureScreenAdaptiveJpeg(ref lastFrameHash, quality, width, height);
            };
        }

        StatusMessage = $"Memulai broadcast layar guru ke {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.StartBroadcast,
            Payload = streamUrl
        };

        await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        IsBroadcasting = true;
        StatusMessage = $"Broadcast layar guru aktif ({targets.Count} PC terhubung).";
    }

    public async Task StartStudentBroadcastAsync(object? parameter = null)
    {
        var presenter = (parameter as LabComputer) ?? SelectedComputer;
        if (presenter == null || !presenter.IsOnline)
        {
            StatusMessage = "Pilih salah satu komputer siswa yang online untuk disiarkan.";
            return;
        }

        SelectedComputer = presenter;

        var otherClients = Computers.Where(c => c.IsOnline && !c.Hostname.Equals(presenter.Hostname, StringComparison.OrdinalIgnoreCase)).ToList();
        if (otherClients.Count == 0)
        {
            StatusMessage = "Tidak ada komputer siswa lain yang online untuk menerima siaran.";
            return;
        }

        string broadcastUrl;
        if (_httpFileServer != null)
        {
            var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            _httpFileServer.PresentationFrameProvider = () =>
            {
                try
                {
                    return http.GetByteArrayAsync($"http://{presenter.IpAddress}:5901/screenshot").GetAwaiter().GetResult();
                }
                catch
                {
                    return null;
                }
            };
            string localIp = NetworkHelper.GetPrimaryLanIPv4Address();
            broadcastUrl = $"http://{localIp}:{_httpFileServer.Port}/presentation/view";
        }
        else
        {
            broadcastUrl = $"http://{presenter.IpAddress}:5901/view";
        }

        StatusMessage = $"Menyiarkan layar {presenter.Hostname} ke {otherClients.Count} PC siswa...";

        var cmd = new CommandMessage
        {
            Type = CommandType.StartBroadcast,
            Payload = broadcastUrl
        };

        await _commandService.SendBroadcastCommandAsync(otherClients.Select(c => c.IpAddress), cmd);
        IsBroadcasting = true;
        StatusMessage = $"Siaran layar siswa {presenter.Hostname} aktif ({otherClients.Count} PC menerima).";
    }

    public async Task StopBroadcastAsync()
    {
        var targets = Computers.Where(c => c.IsOnline).ToList();
        if (_httpFileServer != null)
        {
            _httpFileServer.PresentationFrameProvider = null;
        }

        var cmd = new CommandMessage { Type = CommandType.StopBroadcast };
        await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        IsBroadcasting = false;
        StatusMessage = "Broadcast layar dihentikan.";
    }

    public async Task LockAttentionSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer online yang dipilih.";
            return;
        }

        string? custom = Prompt("Perhatikan Guru", "Masukkan pesan untuk ditampilkan di layar siswa:", "PERHATIKAN GURU");
        if (custom == null) return;
        string msg = string.IsNullOrWhiteSpace(custom) ? "PERHATIKAN GURU" : custom;

        StatusMessage = $"Mengunci layar & input ({msg}) pada {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.LockAttention,
            Payload = MessageSerializer.Serialize(new AttentionPayload { Lock = true, Message = msg })
        };

        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int ok = results.Count(r => r.Value.Success);
        StatusMessage = $"Perhatikan Guru aktif pada {ok}/{targets.Count} PC.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "LockAttention", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task UnlockAttentionSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer online yang dipilih.";
            return;
        }

        StatusMessage = $"Membuka kunci layar & input ke {targets.Count} PC...";
        var cmd = new CommandMessage { Type = CommandType.UnlockAttention };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int ok = results.Count(r => r.Value.Success);
        StatusMessage = $"Kunci layar dibuka pada {ok}/{targets.Count} PC.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "UnlockAttention", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task MuteAllAsync()
    {
        var targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer online untuk di-mute.";
            return;
        }

        StatusMessage = $"Membisukan (Mute) audio pada {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.SetAudioMute,
            Payload = MessageSerializer.Serialize(new AudioPayload { Mute = true })
        };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int ok = results.Count(r => r.Value.Success);
        StatusMessage = $"Audio Mute: {ok}/{targets.Count} PC berhasil dibisukan.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "MuteAudio", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task UnmuteAllAsync()
    {
        var targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer online untuk di-unmute.";
            return;
        }

        StatusMessage = $"Mengaktifkan suara (Unmute) pada {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.SetAudioMute,
            Payload = MessageSerializer.Serialize(new AudioPayload { Mute = false })
        };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int ok = results.Count(r => r.Value.Success);
        StatusMessage = $"Audio Unmute: {ok}/{targets.Count} PC aktif bersuara.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "UnmuteAudio", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task EnableExamModeSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer online untuk Mode Ujian.";
            return;
        }

        StatusMessage = $"Mengaktifkan Mode Ujian (Exam Lockdown) pada {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.SetExamMode,
            Payload = MessageSerializer.Serialize(new ExamModePayload { Enabled = true })
        };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int ok = results.Count(r => r.Value.Success);
        StatusMessage = $"Mode Ujian aktif pada {ok}/{targets.Count} PC (Win keys & TaskMgr dinonaktifkan).";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "EnableExamMode", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task DisableExamModeSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer online.";
            return;
        }

        StatusMessage = $"Menonaktifkan Mode Ujian pada {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.SetExamMode,
            Payload = MessageSerializer.Serialize(new ExamModePayload { Enabled = false })
        };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int ok = results.Count(r => r.Value.Success);
        StatusMessage = $"Mode Ujian dinonaktifkan pada {ok}/{targets.Count} PC.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "DisableExamMode", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task BlockUsbSelectedAsync(bool block)
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer online.";
            return;
        }

        string actionName = block ? "Blokir USB" : "Buka Blokir USB";
        StatusMessage = $"Mengirim perintah {actionName} ke {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.SetUsbBlock,
            Payload = MessageSerializer.Serialize(new ToggleBlockPayload { Blocked = block })
        };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int ok = results.Count(r => r.Value.Success);
        StatusMessage = $"{actionName}: {ok}/{targets.Count} berhasil.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, block ? "BlockUSB" : "UnblockUSB", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task BlockInternetSelectedAsync(bool block)
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer online.";
            return;
        }

        string actionName = block ? "Blokir Internet Selektif" : "Buka Akses Internet";
        StatusMessage = $"Mengirim perintah {actionName} ke {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.SetInternetBlock,
            Payload = MessageSerializer.Serialize(new ToggleBlockPayload { Blocked = block })
        };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int ok = results.Count(r => r.Value.Success);
        StatusMessage = $"{actionName}: {ok}/{targets.Count} berhasil (LAN & DNS dipertahankan).";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, block ? "BlockInternet" : "UnblockInternet", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task LaunchAppOrUrlSelectedAsync(string? target = null)
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer online.";
            return;
        }

        string toLaunch = target ?? string.Empty;
        if (string.IsNullOrWhiteSpace(toLaunch))
        {
            string? promptResult = Prompt("Jalankan Aplikasi / Buka URL", "Masukkan URL situs (cth: https://google.com) atau nama/path aplikasi:", "https://");
            if (string.IsNullOrWhiteSpace(promptResult)) return;
            toLaunch = promptResult;
        }

        if (string.IsNullOrWhiteSpace(toLaunch)) return;

        StatusMessage = $"Menjalankan '{toLaunch}' di {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.LaunchProcess,
            Payload = MessageSerializer.Serialize(new LaunchPayload { Target = toLaunch })
        };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int ok = results.Count(r => r.Value.Success);
        StatusMessage = $"Peluncuran aplikasi: {ok}/{targets.Count} berhasil.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "LaunchApp", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task DistributeFileAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer online untuk distribusi file.";
            return;
        }

        FileDistributionOptions? options = null;
        if (FileDistributionDialogHandler != null)
        {
            options = FileDistributionDialogHandler();
        }
        else
        {
            options = FileDistributionDialogHelper.ShowDialog(OpenFileDialogHandler, FolderBrowserDialogHandler);
        }

        if (options == null || string.IsNullOrWhiteSpace(options.SourcePath)) return;
        if (!File.Exists(options.SourcePath) && !Directory.Exists(options.SourcePath))
        {
            StatusMessage = "Berkas atau folder sumber tidak ditemukan.";
            return;
        }

        // Fallback for legacy Base64 TCP if HttpFileServer is unavailable
        if (_httpFileServer == null)
        {
            if (options.IsDirectory)
            {
                StatusMessage = "HttpFileServer tidak aktif, pengiriman satu folder memerlukan server streaming.";
                return;
            }

            byte[] fileBytes = await File.ReadAllBytesAsync(options.SourcePath);
            string base64 = Convert.ToBase64String(fileBytes);
            var uploadPayload = new FileTransferPayload
            {
                FileName = options.ItemName,
                TargetDirectory = options.TargetDirectory,
                ContentBase64 = base64
            };
            var legacyCmd = new CommandMessage
            {
                Type = CommandType.UploadFile,
                Payload = MessageSerializer.Serialize(uploadPayload)
            };
            var legacyResults = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), legacyCmd);
            int legacyOk = legacyResults.Count(r => r.Value.Success);
            StatusMessage = $"Distribusi file (fallback): {legacyOk}/{targets.Count} tersimpan ke siswa.";
            return;
        }

        CleanupActiveDistributionSession();

        // 1. Prepare streaming session (Disk-to-Disk, SHA256 integrity, transparent folder packaging)
        FileTransferSession session;
        try
        {
            session = FileDistributionService.CreateSession(options.SourcePath, options.IsDirectory);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal memproses berkas distribusi: {ex.Message}";
            return;
        }

        _activeDistributionSession = session;
        _httpFileServer.RegisterFileSession(session);

        string localIp = NetworkHelper.GetPrimaryLanIPv4Address();
        string downloadUrl = $"http://{localIp}:{_httpFileServer.Port}/files/download/{session.Token}";

        var payload = new FileDistributionPayload
        {
            TransferToken = session.Token,
            DownloadUrl = downloadUrl,
            ItemName = session.FileName,
            IsDirectory = session.IsDirectory,
            TargetDirectory = options.TargetDirectory,
            CustomSubfolder = options.CustomSubfolder,
            ChecksumSha256 = session.Sha256Hash,
            FileSizeBytes = session.FileSize,
            AutoOpen = options.AutoOpen,
            ShowNotification = options.ShowNotification,
            Overwrite = options.Overwrite
        };

        // 2. Setup Real-time Progress ViewModel & Modal
        var progressVM = new FileTransferProgressViewModel
        {
            ItemSummary = $"Mengirim '{(session.FileName)}' ({session.FileSize / (1024.0 * 1024.0):F2} MB) ke {targets.Count} PC",
            IsInProgress = true,
            CanCancel = true
        };

        foreach (var t in targets)
        {
            progressVM.Targets.Add(new FileTransferTargetItem
            {
                Hostname = t.EffectiveName,
                IpAddress = t.IpAddress,
                State = TargetTransferState.Waiting,
                StatusMessage = "Menunggu antrean..."
            });
        }

        progressVM.OnCloseRequested += () =>
        {
            IsFileTransferModalVisible = false;
            CleanupActiveDistributionSession();
        };

        TransferProgressVM = progressVM;
        IsFileTransferModalVisible = true;

        // 3. Setup aggregate progress & network speed tracking
        long totalExpectedBytes = session.FileSize * targets.Count;
        DateTime lastSpeedCalc = DateTime.UtcNow;
        long bytesSinceLastCalc = 0;

        void OnChunk(string token, int bytes)
        {
            if (_activeDistributionSession == null || token != _activeDistributionSession.Token) return;
            Interlocked.Add(ref bytesSinceLastCalc, bytes);

            var now = DateTime.UtcNow;
            var elapsed = (now - lastSpeedCalc).TotalSeconds;
            if (elapsed >= 0.5)
            {
                long chunkSum = Interlocked.Exchange(ref bytesSinceLastCalc, 0);
                double speedMBs = (chunkSum / (1024.0 * 1024.0)) / Math.Max(elapsed, 0.001);
                lastSpeedCalc = now;
                progressVM.UpdateAggregateProgress(_activeDistributionSession.BytesTransferred, totalExpectedBytes, speedMBs);
            }
        }

        _distributionChunkHandler = OnChunk;
        _httpFileServer.OnFileChunkTransferred += _distributionChunkHandler;

        // 4. Per-target worker
        async Task ExecuteTransferAsync(System.Collections.Generic.List<FileTransferTargetItem> itemsToTransfer)
        {
            var tasks = itemsToTransfer.Select(async item =>
            {
                if (progressVM.CancellationToken.IsCancellationRequested)
                {
                    item.State = TargetTransferState.Cancelled;
                    item.StatusMessage = "Dibatalkan oleh guru.";
                    return;
                }

                item.State = TargetTransferState.Transferring;
                item.StatusMessage = "Mengunduh via stream HTTP...";

                var cmd = new CommandMessage
                {
                    Type = CommandType.DownloadAndDeployFile,
                    Payload = MessageSerializer.Serialize(payload)
                };

                int timeoutMs = Math.Max(60000, (int)(session.FileSize / (1024 * 1024) * 2000) + 60000);

                try
                {
                    var resp = await _commandService.SendCommandAsync(item.IpAddress, cmd, timeoutMs: timeoutMs, cancellationToken: progressVM.CancellationToken);
                    if (resp.Success)
                    {
                        progressVM.MarkTargetCompleted(item.IpAddress, resp.Output ?? "Selesai (100%)");
                        await _databaseService.LogActivityAsync(item.Hostname, "DistributeFile", resp.Output ?? "Success", true);
                    }
                    else
                    {
                        progressVM.MarkTargetFailed(item.IpAddress, resp.Error ?? "Gagal.");
                        await _databaseService.LogActivityAsync(item.Hostname, "DistributeFile", resp.Error ?? "Failed", false);
                    }
                }
                catch (OperationCanceledException)
                {
                    item.State = TargetTransferState.Cancelled;
                    item.StatusMessage = "Dibatalkan oleh guru.";
                }
                catch (Exception ex)
                {
                    progressVM.MarkTargetFailed(item.IpAddress, ex.Message);
                    await _databaseService.LogActivityAsync(item.Hostname, "DistributeFile", ex.Message, false);
                }
            });

            await Task.WhenAll(tasks);
        }

        progressVM.RetryFailedHandler = async failedItems =>
        {
            await ExecuteTransferAsync(failedItems);
            int completed = progressVM.Targets.Count(t => t.State == TargetTransferState.Completed);
            StatusMessage = $"Distribusi file: {completed}/{targets.Count} PC berhasil menerima berkas.";
        };

        try
        {
            await ExecuteTransferAsync(progressVM.Targets.ToList());
        }
        finally
        {
            progressVM.UpdateCompletionState();
            int completedCount = progressVM.Targets.Count(t => t.State == TargetTransferState.Completed);
            StatusMessage = $"Distribusi file: {completedCount}/{targets.Count} PC berhasil menerima berkas.";
        }
    }

    private void CleanupActiveDistributionSession()
    {
        if (_distributionChunkHandler != null && _httpFileServer != null)
        {
            _httpFileServer.OnFileChunkTransferred -= _distributionChunkHandler;
            _distributionChunkHandler = null;
        }

        if (_activeDistributionSession != null)
        {
            if (_httpFileServer != null)
            {
                _httpFileServer.UnregisterFileSession(_activeDistributionSession.Token);
            }
            else
            {
                _activeDistributionSession.Dispose();
            }
            _activeDistributionSession = null;
        }
    }

    public async Task CollectFilesFromStudentsAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih komputer siswa untuk pengumpulan file.";
            return;
        }

        string baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LabSupport_Collected_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        if (FolderBrowserDialogHandler != null)
        {
            string? customDir = FolderBrowserDialogHandler();
            if (!string.IsNullOrWhiteSpace(customDir)) baseFolder = customDir;
        }

        if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);

        string pattern = "*.*";
        string? p = Prompt("Kumpulkan File Siswa", "Filter ekstensi file (cth: *.docx, *.py, *.*):", "*.*");
        if (p == null) return;
        if (!string.IsNullOrWhiteSpace(p)) pattern = p;

        StatusMessage = $"Mengumpulkan tugas siswa dari {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.CollectFiles,
            Payload = MessageSerializer.Serialize(new CollectFilesPayload { SourcePath = "Desktop", SearchPattern = pattern })
        };

        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int totalCollected = 0;
        int successPCs = 0;

        foreach (var t in targets)
        {
            if (results.TryGetValue(t.IpAddress, out var r) && r.Success && !string.IsNullOrEmpty(r.Output))
            {
                try
                {
                    var response = MessageSerializer.Deserialize<CollectFilesResponse>(r.Output);
                    if (response != null && response.Files.Count > 0)
                    {
                        string safeHost = string.Join("_", (t.Hostname ?? "PC").Split(Path.GetInvalidFileNameChars())).Replace("..", "_").Trim();
                        if (string.IsNullOrWhiteSpace(safeHost)) safeHost = "PC_" + t.IpAddress.Replace(".", "_");

                        // Sanitize Windows DOS reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
                        string upperHost = safeHost.ToUpperInvariant();
                        if (upperHost == "CON" || upperHost == "PRN" || upperHost == "AUX" || upperHost == "NUL" ||
                            (upperHost.Length == 4 && (upperHost.StartsWith("COM") || upperHost.StartsWith("LPT")) && char.IsDigit(upperHost[3])))
                        {
                            safeHost = "_" + safeHost;
                        }

                        string fullBaseFolder = Path.GetFullPath(baseFolder);
                        string pcSubfolder = Path.GetFullPath(Path.Combine(fullBaseFolder, safeHost));
                        if (!pcSubfolder.StartsWith(fullBaseFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            pcSubfolder = Path.Combine(fullBaseFolder, "PC_" + Guid.NewGuid().ToString("N")[..8]);
                        }
                        if (!Directory.Exists(pcSubfolder)) Directory.CreateDirectory(pcSubfolder);

                        foreach (var f in response.Files)
                        {
                            string safeRelative = Path.GetFileName(f.RelativePath);
                            if (string.IsNullOrWhiteSpace(safeRelative) || safeRelative.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                            string destPath = Path.GetFullPath(Path.Combine(pcSubfolder, safeRelative));
                            if (!destPath.StartsWith(pcSubfolder, StringComparison.OrdinalIgnoreCase)) continue;

                            byte[] bytes = Convert.FromBase64String(f.ContentBase64);
                            await File.WriteAllBytesAsync(destPath, bytes);
                            totalCollected++;
                        }
                        successPCs++;
                    }
                }
                catch { }
            }
        }

        StatusMessage = $"Pengumpulan selesai: {totalCollected} file dikumpulkan dari {successPCs}/{targets.Count} PC ke: {baseFolder}";
    }

    public async Task AssignGroupSelectedAsync(string? newGroup = null)
    {
        var targets = Computers.Where(c => c.IsSelected).ToList();
        if (targets.Count == 0 && SelectedComputer != null) targets.Add(SelectedComputer);
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih komputer untuk diberi nama grup.";
            return;
        }

        string groupName = newGroup ?? string.Empty;
        if (string.IsNullOrWhiteSpace(groupName))
        {
            string? promptResult = Prompt("Atur Grup Komputer", "Masukkan nama grup baru (cth: Baris 1, Kelompok A):", "Baris 1");
            if (string.IsNullOrWhiteSpace(promptResult)) return;
            groupName = promptResult;
        }

        if (string.IsNullOrWhiteSpace(groupName)) return;

        foreach (var pc in targets)
        {
            pc.GroupName = groupName;
            await _databaseService.UpdateComputerGroupAsync(pc.Hostname, groupName);
        }

        await RefreshGroupsListAsync();
        FilteredComputers.Refresh();
        StatusMessage = $"{targets.Count} PC dimasukkan ke grup '{groupName}'.";
    }

    public async Task RefreshGroupsListAsync()
    {
        var dbGroups = await _databaseService.GetGroupsAsync();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All Groups" };
        foreach (var g in dbGroups) if (!string.IsNullOrWhiteSpace(g)) set.Add(g);
        foreach (var c in Computers) if (!string.IsNullOrWhiteSpace(c.GroupName)) set.Add(c.GroupName);

        var currentList = AvailableGroups.ToList();
        var newList = set.OrderBy(x => x == "All Groups" ? "" : x).ToList();

        if (!currentList.SequenceEqual(newList))
        {
            AvailableGroups.Clear();
            foreach (var g in newList) AvailableGroups.Add(g);
        }
    }

    public async Task AssignRoomSelectedAsync(string? newRoom = null)
    {
        var targets = Computers.Where(c => c.IsSelected).ToList();
        if (targets.Count == 0 && SelectedComputer != null) targets.Add(SelectedComputer);
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih komputer untuk dipindahkan ke kategori ruang.";
            return;
        }

        string roomName = newRoom ?? string.Empty;
        if (string.IsNullOrWhiteSpace(roomName))
        {
            string defaultRoom = AvailableRooms.FirstOrDefault(r => r.Name != "Semua Ruang" && r.Name != "All Rooms")?.Name ?? "Lab 209A";
            string? promptResult = Prompt("Atur Ruang Lab", "Masukkan nama kategori ruang (cth: Lab 209A, Lab 209B):", defaultRoom);
            if (string.IsNullOrWhiteSpace(promptResult)) return;
            roomName = promptResult.Trim();
        }

        if (string.IsNullOrWhiteSpace(roomName)) return;

        foreach (var pc in targets)
        {
            pc.RoomName = roomName;
            await _databaseService.UpdateComputerRoomAsync(pc.Hostname, roomName);
        }

        // Ensure room is in database
        var existingRoom = (await _databaseService.GetAllRoomsAsync()).FirstOrDefault(r => r.Name.Equals(roomName, StringComparison.OrdinalIgnoreCase));
        if (existingRoom == null)
        {
            await _databaseService.UpsertRoomAsync(new LabRoom { Name = roomName, Capacity = 30 });
        }

        await RefreshRoomsListAsync();
        FilteredComputers.Refresh();
        UpdateStats();
        StatusMessage = $"{targets.Count} PC berhasil dipindahkan ke ruang '{roomName}'.";
    }

    public async Task RefreshRoomsListAsync()
    {
        var dbRooms = await _databaseService.GetAllRoomsAsync();
        var roomDict = dbRooms.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        // Check if any PC has a room not yet in lab_rooms
        foreach (var c in Computers)
        {
            if (!string.IsNullOrWhiteSpace(c.RoomName) && !roomDict.ContainsKey(c.RoomName))
            {
                var newRoom = new LabRoom { Name = c.RoomName, Capacity = 30 };
                roomDict[c.RoomName] = newRoom;
                _ = _databaseService.UpsertRoomAsync(newRoom);
            }
        }

        // Calculate stats per room
        int totalOnline = 0;
        int totalCapacity = 0;
        foreach (var r in roomDict.Values)
        {
            r.OnlineCount = Computers.Count(c => c.IsOnline && string.Equals(c.RoomName, r.Name, StringComparison.OrdinalIgnoreCase));
            r.TotalRegistered = Computers.Count(c => string.Equals(c.RoomName, r.Name, StringComparison.OrdinalIgnoreCase));
            totalOnline += r.OnlineCount;
            totalCapacity += r.Capacity;
        }

        var allRoomsItem = new LabRoom
        {
            Name = "Semua Ruang",
            Capacity = totalCapacity > 0 ? totalCapacity : Math.Max(Computers.Count, 30),
            OnlineCount = totalOnline,
            TotalRegistered = Computers.Count,
            Description = "Seluruh Komputer Lab"
        };

        var list = new List<LabRoom> { allRoomsItem };
        list.AddRange(roomDict.Values.OrderBy(r => r.Name));

        foreach (var r in list)
        {
            r.IsSelected = string.Equals(r.Name, SelectedRoomFilter, StringComparison.OrdinalIgnoreCase);
        }

        var dispatcher = Application.Current?.Dispatcher;
        Action update = () =>
        {
            AvailableRooms.Clear();
            foreach (var r in list) AvailableRooms.Add(r);
        };

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(update);
        }
        else
        {
            update();
        }
    }

    public async Task ExportTelemetryAsync()
    {
        try
        {
            string? targetPath = null;
            if (SaveFileDialogHandler != null)
            {
                targetPath = SaveFileDialogHandler();
            }
            else
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Ekspor Telemetri Komputer (.csv)",
                    FileName = $"telemetri_lab_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                    Filter = "CSV File (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = ".csv"
                };
                if (sfd.ShowDialog() == true) targetPath = sfd.FileName;
            }

            if (string.IsNullOrWhiteSpace(targetPath)) return;

            string csv = DatabaseService.ExportTelemetryToCsvString(Computers);
            await File.WriteAllTextAsync(targetPath, csv, Encoding.UTF8);

            StatusMessage = $"Telemetri berhasil diekspor ke: {targetPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal mengekspor telemetri: {ex.Message}";
        }
    }

    public async Task ExportInstallerAsync()
    {
        try
        {
            string? targetPath = null;
            if (SaveFileDialogHandler != null)
            {
                targetPath = SaveFileDialogHandler();
            }
            else
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Simpan File Installer Agent Lab (.bat)",
                    FileName = "pasang_agent_lab.bat",
                    Filter = "Batch Script (*.bat)|*.bat|All Files (*.*)|*.*",
                    DefaultExt = ".bat"
                };

                if (sfd.ShowDialog() == true)
                {
                    targetPath = sfd.FileName;
                }
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentManagerId) && _databaseService != null)
            {
                CurrentManagerId = await _databaseService.GetOrCreateManagerIdAsync();
            }

            string ip = NetworkHelper.GetPrimaryLanIPv4Address();
            int port = _httpFileServer?.Port ?? 8080;
            string labCode = string.IsNullOrWhiteSpace(CurrentManagerId) ? "LAB-DEFAULT" : CurrentManagerId;

            string batContent = ScriptGenerator.GenerateSingleBatchInstaller(ip, port, labCode);
            await File.WriteAllTextAsync(targetPath, batContent, Encoding.Default);

            StatusMessage = $"[SUKSES] Installer mandiri (.bat) berhasil diekspor ke: {targetPath}";

            if (Application.Current != null)
            {
                MessageBox.Show(
                    $"Installer mandiri 'pasang_agent_lab.bat' berhasil diekspor ke:\n{targetPath}\n\nLangkah selanjutnya:\n1. Salin file ini ke Flashdisk (USB).\n2. Jalankan 'Run as Administrator' di PC Lab target.",
                    "LabSupport — Export Installer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal mengekspor installer: {ex.Message}";
        }
    }

    public async Task LoadComputersFromDbAsync()
    {
        try
        {
            var dbComputers = await _databaseService.GetAllComputersAsync();
            var now = DateTime.UtcNow;

            var dispatcher = Application.Current?.Dispatcher;
            Action updateAction = () =>
            {
                var existingMap = Computers.ToDictionary(c => c.Hostname, StringComparer.OrdinalIgnoreCase);

                foreach (var c in dbComputers)
                {
                    if (existingMap.TryGetValue(c.Hostname, out var existing))
                    {
                        existing.IpAddress = c.IpAddress;
                        existing.MacAddress = c.MacAddress;
                        existing.OsVersion = c.OsVersion;
                        existing.Notes = c.Notes;
                        if (!string.IsNullOrWhiteSpace(c.GroupName)) existing.GroupName = c.GroupName;
                        if (!string.IsNullOrWhiteSpace(c.RoomName)) existing.RoomName = c.RoomName;
                        if (!string.IsNullOrWhiteSpace(c.DisplayName)) existing.DisplayName = c.DisplayName;
                        if (!string.IsNullOrWhiteSpace(c.CpuModel)) existing.CpuModel = c.CpuModel;
                        if (!string.IsNullOrWhiteSpace(c.GpuModel)) existing.GpuModel = c.GpuModel;
                        if (c.DiskTotalGb > 0) existing.DiskTotalGb = c.DiskTotalGb;
                        if (!string.IsNullOrWhiteSpace(c.MotherboardSerial)) existing.MotherboardSerial = c.MotherboardSerial;
                        if (c.PositionX > 0 || c.PositionY > 0)
                        {
                            existing.PositionX = c.PositionX;
                            existing.PositionY = c.PositionY;
                        }
                    }
                    else
                    {
                        bool isRecent = (now - c.LastSeen).TotalSeconds < 15;
                        Computers.Add(new LabComputer
                        {
                            Hostname = c.Hostname,
                            IpAddress = c.IpAddress,
                            MacAddress = c.MacAddress,
                            OsVersion = c.OsVersion,
                            LastSeen = c.LastSeen,
                            Notes = c.Notes,
                            GroupName = string.IsNullOrWhiteSpace(c.GroupName) ? "Default" : c.GroupName,
                            RoomName = string.IsNullOrWhiteSpace(c.RoomName) ? "General" : c.RoomName,
                            DisplayName = c.DisplayName,
                            CpuModel = c.CpuModel,
                            GpuModel = c.GpuModel,
                            DiskTotalGb = c.DiskTotalGb,
                            MotherboardSerial = c.MotherboardSerial,
                            PositionX = c.PositionX,
                            PositionY = c.PositionY,
                            IsOnline = isRecent
                        });
                    }
                }
                UpdateStats();
            };

            if (dispatcher != null)
            {
                dispatcher.Invoke(updateAction);
            }
            else
            {
                updateAction();
            }

            await RefreshRoomsListAsync();
            await RefreshGroupsListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal memuat daftar PC: {ex.Message}";
        }
    }

    public void HandleHeartbeat(HeartbeatMessage hb, System.Windows.Media.Imaging.BitmapSource? predecodedThumbnail = null)
    {
        // Manager Pairing Code check: Ignore heartbeats from other labs/managers
        if (!string.IsNullOrWhiteSpace(CurrentManagerId) &&
            !string.IsNullOrWhiteSpace(hb.ManagerId) &&
            !string.Equals(hb.ManagerId, CurrentManagerId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var existing = (!string.IsNullOrWhiteSpace(hb.MacAddress)
            ? Computers.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.MacAddress) && c.MacAddress.Equals(hb.MacAddress, StringComparison.OrdinalIgnoreCase))
            : null)
            ?? Computers.FirstOrDefault(c => c.Hostname.Equals(hb.Hostname, StringComparison.OrdinalIgnoreCase));

        bool hasDuplicateHostname = Computers.Any(c => c != existing && c.Hostname.Equals(hb.Hostname, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            var newPc = new LabComputer
            {
                Hostname = hb.Hostname,
                DisplayName = hb.DisplayName,
                RoomName = !string.IsNullOrWhiteSpace(hb.RoomName) ? hb.RoomName : "General",
                IpAddress = hb.IpAddress,
                MacAddress = hb.MacAddress,
                OsVersion = hb.OsVersion,
                GroupName = "Default",
                CpuUsage = hb.CpuUsage,
                RamUsageMb = hb.RamUsageMb,
                RamTotalMb = hb.RamTotalMb,
                DiskFreeGb = hb.DiskFreeGb,
                DiskTotalGb = hb.DiskTotalGb,
                CpuModel = hb.CpuModel ?? string.Empty,
                GpuModel = hb.GpuModel ?? string.Empty,
                MotherboardSerial = hb.MotherboardSerial ?? string.Empty,
                NetworkDownloadKbps = hb.NetworkDownloadKbps,
                NetworkUploadKbps = hb.NetworkUploadKbps,
                IdleSeconds = hb.IdleSeconds,
                LastSeen = DateTime.UtcNow,
                IsOnline = true,
                IsDuplicateHostname = hasDuplicateHostname,
                LastDbSaved = DateTime.UtcNow
            };

            int idx = Computers.Count;
            newPc.PositionX = 40 + ((idx % 5) * 260);
            newPc.PositionY = 40 + ((idx / 5) * 300);

            if (predecodedThumbnail != null)
            {
                newPc.SetThumbnailSource(predecodedThumbnail);
            }
            else if (!string.IsNullOrEmpty(hb.ScreenshotBase64))
            {
                try { newPc.SetThumbnailBytes(Convert.FromBase64String(hb.ScreenshotBase64)); } catch { }
            }

            Computers.Add(newPc);
            _ = _databaseService.UpsertComputerAsync(newPc);
        }
        else
        {
            bool ipChanged = !string.Equals(existing.IpAddress, hb.IpAddress, StringComparison.OrdinalIgnoreCase);
            bool macChanged = !string.IsNullOrEmpty(hb.MacAddress) && !string.Equals(existing.MacAddress, hb.MacAddress, StringComparison.OrdinalIgnoreCase);

            existing.Hostname = hb.Hostname;
            if (!string.IsNullOrWhiteSpace(hb.RoomName) && !string.Equals(hb.RoomName, "General", StringComparison.OrdinalIgnoreCase))
            {
                existing.RoomName = hb.RoomName;
            }
            if (!string.IsNullOrWhiteSpace(hb.DisplayName))
            {
                existing.DisplayName = hb.DisplayName;
            }
            existing.IpAddress = hb.IpAddress;
            if (!string.IsNullOrEmpty(hb.MacAddress)) existing.MacAddress = hb.MacAddress;
            if (!string.IsNullOrEmpty(hb.OsVersion)) existing.OsVersion = hb.OsVersion;
            existing.IsDuplicateHostname = hasDuplicateHostname;
            existing.CpuUsage = hb.CpuUsage;
            existing.RamUsageMb = hb.RamUsageMb;
            existing.RamTotalMb = hb.RamTotalMb;
            existing.DiskFreeGb = hb.DiskFreeGb;
            if (hb.DiskTotalGb > 0) existing.DiskTotalGb = hb.DiskTotalGb;
            if (!string.IsNullOrEmpty(hb.CpuModel)) existing.CpuModel = hb.CpuModel;
            if (!string.IsNullOrEmpty(hb.GpuModel)) existing.GpuModel = hb.GpuModel;
            if (!string.IsNullOrEmpty(hb.MotherboardSerial)) existing.MotherboardSerial = hb.MotherboardSerial;
            existing.NetworkDownloadKbps = hb.NetworkDownloadKbps;
            existing.NetworkUploadKbps = hb.NetworkUploadKbps;
            existing.IdleSeconds = hb.IdleSeconds;
            existing.LastSeen = DateTime.UtcNow;
            existing.IsOnline = true;

            if (predecodedThumbnail != null)
            {
                existing.SetThumbnailSource(predecodedThumbnail);
            }
            else if (!string.IsNullOrEmpty(hb.ScreenshotBase64))
            {
                try { existing.SetThumbnailBytes(Convert.FromBase64String(hb.ScreenshotBase64)); } catch { }
            }

            existing.NotifyMetricsChanged();

            var now = DateTime.UtcNow;
            if (ipChanged || macChanged || (now - existing.LastDbSaved).TotalSeconds >= 60)
            {
                existing.LastDbSaved = now;
                _ = _databaseService.UpsertComputerAsync(existing);
            }
        }

        UpdateStats();
    }

    private void CheckOfflineComputers()
    {
        var now = DateTime.UtcNow;
        bool changed = false;

        foreach (var c in Computers)
        {
            if (c.IsOnline && (now - c.LastSeen).TotalSeconds > 15)
            {
                c.IsOnline = false;
                c.NotifyMetricsChanged();
                changed = true;
            }
        }

        if (changed)
        {
            UpdateStats();
        }
    }

    public void UpdateStats()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(OnlineCount));
        OnPropertyChanged(nameof(OfflineCount));

        int allOnline = 0;
        foreach (var r in AvailableRooms)
        {
            if (r.Name.Equals("Semua Ruang", StringComparison.OrdinalIgnoreCase) ||
                r.Name.Equals("All Rooms", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            r.OnlineCount = Computers.Count(c => c.IsOnline && string.Equals(c.RoomName, r.Name, StringComparison.OrdinalIgnoreCase));
            r.TotalRegistered = Computers.Count(c => string.Equals(c.RoomName, r.Name, StringComparison.OrdinalIgnoreCase));
            allOnline += r.OnlineCount;
        }

        var all = AvailableRooms.FirstOrDefault(r =>
            r.Name.Equals("Semua Ruang", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Equals("All Rooms", StringComparison.OrdinalIgnoreCase));
        if (all != null)
        {
            all.OnlineCount = allOnline;
            all.TotalRegistered = Computers.Count;
        }
    }

    public void UpdateSelectionSummary()
    {
        TargetSummary = SelectionService.FormatSelectionSummary(Computers);
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(MassActionButtonText));
        OnPropertyChanged(nameof(TargetSummary));
    }

    private void SetSelectionAll(bool select)
    {
        SelectionService.SelectAll(Computers, select);
        UpdateSelectionSummary();
    }

    public void InvertSelection()
    {
        SelectionService.InvertSelection(Computers);
        UpdateSelectionSummary();
    }

    public void SelectRoom(string? roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName)) return;
        SelectionService.SelectRoom(Computers, roomName);
        UpdateSelectionSummary();
    }

    public void DeselectRoom(string? roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName)) return;
        SelectionService.DeselectRoom(Computers, roomName);
        UpdateSelectionSummary();
    }

    public void OpenMultiLabSelection()
    {
        MultiLabSelectionList.Clear();
        var summaries = SelectionService.GetRoomSummaries(Computers);
        foreach (var s in summaries)
        {
            MultiLabSelectionList.Add(new RoomSelectionItemViewModel
            {
                RoomName = s.RoomName,
                TotalCount = s.TotalCount,
                OnlineCount = s.OnlineCount,
                IsChecked = s.SelectedCount > 0
            });
        }
        IsMultiLabDialogVisible = true;
    }

    public void ApplyMultiLabSelection()
    {
        var selectedRooms = MultiLabSelectionList.Where(r => r.IsChecked).Select(r => r.RoomName).ToList();
        var unselectedRooms = MultiLabSelectionList.Where(r => !r.IsChecked).Select(r => r.RoomName).ToList();

        SelectionService.SelectMultipleRooms(Computers, selectedRooms);
        foreach (var r in unselectedRooms)
        {
            SelectionService.DeselectRoom(Computers, r);
        }

        UpdateSelectionSummary();
        IsMultiLabDialogVisible = false;
    }

    public void CloseMultiLabSelection()
    {
        IsMultiLabDialogVisible = false;
    }

    public void OpenMassAction()
    {
        var targets = Computers.Where(c => c.IsSelected).ToList();
        if (targets.Count == 0)
        {
            targets = Computers.Where(c => c.IsOnline).ToList();
        }
        if (targets.Count == 0)
        {
            targets = Computers.ToList();
        }

        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer di database untuk aktivitas masal.";
            return;
        }

        var massActionVm = new MassActionViewModel(targets, _commandService, _wolService, _databaseService);
        if (ConfirmationDialogHandler != null)
        {
            massActionVm.ConfirmationHandler = ConfirmationDialogHandler;
        }

        if (MassActionDialogHandler != null)
        {
            MassActionDialogHandler(massActionVm);
            return;
        }

        if (Application.Current != null)
        {
            var dlg = new Views.MassActionDialog
            {
                DataContext = massActionVm,
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
        }
    }

    private async Task ShutdownSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer yang online.";
            return;
        }

        StatusMessage = $"Mengirim perintah Shutdown ke {targets.Count} PC...";
        var cmd = new CommandMessage { Type = CommandType.Shutdown };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);

        int success = results.Count(r => r.Value.Success);
        StatusMessage = $"Shutdown: {success}/{targets.Count} berhasil dikirim.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "Shutdown", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    private async Task RestartSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer yang online.";
            return;
        }

        StatusMessage = $"Mengirim perintah Restart ke {targets.Count} PC...";
        var cmd = new CommandMessage { Type = CommandType.Restart };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);

        int success = results.Count(r => r.Value.Success);
        StatusMessage = $"Restart: {success}/{targets.Count} berhasil dikirim.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "Restart", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    private async Task LockSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer yang online.";
            return;
        }

        StatusMessage = $"Mengirim Lock Screen ke {targets.Count} PC...";
        var cmd = new CommandMessage { Type = CommandType.LockScreen };
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);

        int success = results.Count(r => r.Value.Success);
        StatusMessage = $"Lock Screen: {success}/{targets.Count} berhasil.";

        foreach (var t in targets)
        {
            await _databaseService.LogActivityAsync(t.Hostname, "LockScreen", results.TryGetValue(t.IpAddress, out var r) ? (r.Output ?? r.Error ?? "") : "Timeout", results.TryGetValue(t.IpAddress, out var res) && res.Success);
        }
    }

    public async Task WakeSingleComputerAsync(LabComputer? comp)
    {
        if (comp == null) return;

        if (string.IsNullOrWhiteSpace(comp.MacAddress))
        {
            StatusMessage = $"{comp.Hostname} belum memiliki MAC Address. PC harus pernah online minimal 1x agar Agent merekam MAC fisik.";
            return;
        }

        StatusMessage = $"Mengirim Wake-on-LAN Magic Packet burst ke {comp.Hostname} ({comp.MacAddress})...";
        bool ok = await _wolService.WakeComputerAsync(comp.Hostname, comp.MacAddress, comp.IpAddress);
        StatusMessage = ok
            ? $"Magic Packet (WoL) terkirim ke {comp.Hostname}. Menunggu PC menyala..."
            : $"Gagal mengirim WoL ke {comp.Hostname}.";
    }

    public async Task CleanWorkspaceSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih setidaknya satu komputer yang online untuk dibersihkan.";
            return;
        }

        string? confirm = Prompt(
            "Konfirmasi Bersihkan Workspace",
            $"Yakin ingin membersihkan file unduhan, cache temp, Recycle Bin, dan dokumen desktop pada {targets.Count} PC terpilih?\n\nShortcut aplikasi (.lnk) tetap aman terlindungi.\nKetik 'YA' untuk melanjutkan:",
            "YA");

        if (confirm == null || !confirm.Trim().Equals("YA", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Pembersihan workspace dibatalkan.";
            return;
        }

        StatusMessage = $"Membersihkan workspace pada {targets.Count} PC siswa...";
        var payload = new CleanWorkspacePayload
        {
            CleanDownloads = true,
            CleanTemp = true,
            CleanRecycleBin = true,
            CleanDesktopDocs = true,
            CleanSsh = true,
            CleanVirtualBoxVms = true
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.CleanWorkspace,
            Payload = MessageSerializer.Serialize(payload)
        };

        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);

        int successCount = 0;
        int totalFiles = 0;
        long totalBytes = 0;

        foreach (var r in results.Values)
        {
            if (r.Success)
            {
                successCount++;
                if (!string.IsNullOrWhiteSpace(r.Output))
                {
                    try
                    {
                        var cleanRes = MessageSerializer.Deserialize<CleanWorkspaceResult>(r.Output);
                        if (cleanRes != null)
                        {
                            totalFiles += cleanRes.FilesDeletedCount;
                            totalBytes += cleanRes.BytesFreed;
                        }
                    }
                    catch { }
                }
            }
        }

        double mbFreed = Math.Round((double)totalBytes / (1024 * 1024), 1);
        string freedStr = mbFreed >= 1024 ? $"{mbFreed / 1024:F2} GB" : $"{mbFreed:F1} MB";

        string summary = $"Pembersihan selesai: {successCount}/{targets.Count} PC sukses. {totalFiles} file dihapus ({freedStr} ruang dipulihkan).";
        StatusMessage = summary;

        foreach (var t in targets)
        {
            bool ok = results.TryGetValue(t.IpAddress, out var r) && r.Success;
            string outMsg = results.TryGetValue(t.IpAddress, out var r2) ? (r2.Output ?? r2.Error ?? "") : "Timeout";
            await _databaseService.LogActivityAsync(t.Hostname, "CleanWorkspace", outMsg, ok);
        }
    }

    private async Task WakeSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && !string.IsNullOrEmpty(c.MacAddress)).ToList();
        if (targets.Count == 0)
        {
            var offlineWithMac = Computers.Where(c => !c.IsOnline && !string.IsNullOrEmpty(c.MacAddress)).ToList();
            var offlineMissingMac = Computers.Where(c => !c.IsOnline && string.IsNullOrEmpty(c.MacAddress)).ToList();

            if (offlineWithMac.Count > 0)
            {
                string? confirm = Prompt(
                    "Nyalakan Seluruh PC Lab (WoL)",
                    $"Tidak ada PC yang dicentang. Apakah Anda ingin menyalakan seluruh {offlineWithMac.Count} PC offline sekarang? (Ketik 'Y' atau tekan OK)",
                    "Y");

                if (confirm == null) return;
                targets = offlineWithMac;
            }
            else if (offlineMissingMac.Count > 0)
            {
                StatusMessage = $"{offlineMissingMac.Count} PC offline belum memiliki MAC Address. PC harus pernah online minimal 1x agar Agent mencatat MAC.";
                return;
            }
            else
            {
                StatusMessage = "Tidak ada komputer offline yang memerlukan Wake-on-LAN.";
                return;
            }
        }

        StatusMessage = $"Mengirim Wake-on-LAN Magic Packet burst ke {targets.Count} PC...";
        int sent = 0;
        foreach (var t in targets)
        {
            bool ok = await _wolService.WakeComputerAsync(t.Hostname, t.MacAddress, t.IpAddress);
            if (ok) sent++;
        }

        StatusMessage = $"Wake-on-LAN: {sent}/{targets.Count} paket burst terkirim ke jaringan.";
    }

    public string? Prompt(string title, string message, string defaultValue = "")
    {
        if (PromptDialogHandler != null)
        {
            return PromptDialogHandler(title, message);
        }

        try
        {
            return ShowWpfInputDialog(title, message, defaultValue);
        }
        catch
        {
            return null;
        }
    }

    private static string? ShowWpfInputDialog(string title, string message, string defaultValue = "")
    {
        if (Application.Current == null) return null;

        string? result = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new Window
            {
                Title = title,
                Width = 420,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x17, 0x2A)),
                Foreground = System.Windows.Media.Brushes.White
            };

            var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            var textPrompt = new System.Windows.Controls.TextBlock
            {
                Text = message,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            var textBox = new System.Windows.Controls.TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 14),
                Padding = new Thickness(8, 6, 8, 6),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x41, 0x55))
            };

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var btnOk = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 75,
                Height = 30,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB)),
                Foreground = System.Windows.Media.Brushes.White
            };
            btnOk.Click += (_, _) =>
            {
                result = textBox.Text;
                window.DialogResult = true;
                window.Close();
            };

            var btnCancel = new System.Windows.Controls.Button
            {
                Content = "Batal",
                Width = 75,
                Height = 30,
                IsCancel = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x41, 0x55)),
                Foreground = System.Windows.Media.Brushes.White
            };
            btnCancel.Click += (_, _) =>
            {
                window.DialogResult = false;
                window.Close();
            };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);

            sp.Children.Add(textPrompt);
            sp.Children.Add(textBox);
            sp.Children.Add(btnPanel);
            window.Content = sp;

            textBox.Focus();
            textBox.SelectAll();

            if (window.ShowDialog() != true)
            {
                result = null;
            }
        });

        return result;
    }

    public void AutoArrangeComputers(int columns = 5, double startX = 40, double startY = 40, double gapX = 260, double gapY = 300)
    {
        var list = Computers.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            int col = i % columns;
            int row = i / columns;
            double x = startX + (col * gapX);
            double y = startY + (row * gapY);
            list[i].PositionX = x;
            list[i].PositionY = y;
            _ = _databaseService.UpdateComputerPositionAsync(list[i].Hostname, x, y);
        }
        StatusMessage = $"Denah meja berhasil disusun otomatis ({list.Count} PC).";
    }

    public async Task SaveComputerPositionAsync(string hostname, double x, double y)
    {
        var comp = Computers.FirstOrDefault(c => c.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
        if (comp != null)
        {
            comp.PositionX = x;
            comp.PositionY = y;
        }
        await _databaseService.UpdateComputerPositionAsync(hostname, x, y);
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            var (schedEnabled, schedTime) = await _databaseService.GetSchedulerSettingsAsync();
            IsDailyShutdownEnabled = schedEnabled;
            DailyShutdownTime = schedTime;

            var (guardEnabled, guardBlacklist) = await _databaseService.GetAppGuardSettingsAsync();
            IsAppGuardActive = guardEnabled;
            if (!string.IsNullOrWhiteSpace(guardBlacklist))
            {
                AppGuardBlacklist = guardBlacklist;
            }
        }
        catch { }
    }

    public async Task CheckPowerSchedulerAsync()
    {
        if (!_isDailyShutdownEnabled) return;

        var now = DateTime.Now;
        if (_lastDailyShutdownDate.Date == now.Date) return;

        if (now.Hour == _dailyShutdownTime.Hours && now.Minute == _dailyShutdownTime.Minutes)
        {
            _lastDailyShutdownDate = now;
            var targets = Computers.Where(c => c.IsOnline).ToList();
            if (targets.Count > 0)
            {
                var cmd = new CommandMessage { Type = CommandType.Shutdown };
                await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
                await _databaseService.LogActivityAsync("ALL_ONLINE", "Daily Auto-Shutdown", $"Triggered at {now:HH:mm} for {targets.Count} PCs", true);
                StatusMessage = $"[JADWAL OTOMATIS] Mematikan {targets.Count} PC lab sesuai jadwal harian ({_dailyShutdownTime:hh\\:mm}).";
            }
        }
    }

    public async Task ToggleAppGuardAsync()
    {
        IsAppGuardActive = !IsAppGuardActive;
        await _databaseService.SaveAppGuardSettingsAsync(IsAppGuardActive, AppGuardBlacklist);

        var list = AppGuardBlacklist
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        var payload = new ProcessGuardPayload
        {
            Enabled = IsAppGuardActive,
            BlacklistedProcesses = list
        };

        var onlineTargets = Computers.Where(c => c.IsOnline).ToList();
        if (onlineTargets.Count > 0)
        {
            var cmd = new CommandMessage
            {
                Type = CommandType.SetProcessGuard,
                Payload = MessageSerializer.Serialize(payload)
            };
            await _commandService.SendBroadcastCommandAsync(onlineTargets.Select(c => c.IpAddress), cmd);
        }

        await _databaseService.LogActivityAsync("ALL_ONLINE", "Toggle App Guard", $"Enabled: {IsAppGuardActive}, Targets: {onlineTargets.Count}", true);
        StatusMessage = IsAppGuardActive
            ? $"🛡️ App Guard diaktifkan pada {onlineTargets.Count} PC (Silent Kill aktif)."
            : "App Guard dinonaktifkan.";
    }

    public async Task BroadcastClipboardAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer online untuk broadcast clipboard.";
            return;
        }

        string? text = Prompt("Kirim Teks ke Clipboard Siswa", "Masukkan teks/URL/kode untuk disalin ke clipboard PC siswa (Ctrl+V):", "");
        if (text == null) return;

        var payload = new ClipboardPayload { Text = text };
        var cmd = new CommandMessage
        {
            Type = CommandType.SetClipboardText,
            Payload = MessageSerializer.Serialize(payload)
        };

        StatusMessage = $"Menyebarkan teks ke clipboard {targets.Count} PC siswa...";
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(c => c.IpAddress), cmd);
        int successCount = results.Values.Count(r => r.Success);

        await _databaseService.LogActivityAsync("CLIPBOARD_BROADCAST", "Set Clipboard Text", $"Pushed {text.Length} chars to {successCount}/{targets.Count} PCs", successCount > 0);
        StatusMessage = $"📋 Teks disalin ke clipboard {successCount}/{targets.Count} PC siswa (Siswa tinggal tekan Ctrl+V).";
    }

    public async Task SetWebFilterSelectedAsync(WebFilterMode mode, IEnumerable<string>? customDomains = null)
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0) targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih minimal satu komputer online untuk menerapkan filter web.";
            return;
        }

        var domains = customDomains?.ToList() ?? new List<string>();
        if (domains.Count == 0 && mode != WebFilterMode.Disabled)
        {
            domains.AddRange(WebFilterService.DefaultSocialDomains);
            domains.AddRange(WebFilterService.DefaultGamingDomains);
        }

        var payload = new WebFilterPayload
        {
            Mode = mode,
            Domains = domains
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.SetWebFilter,
            Payload = MessageSerializer.Serialize(payload)
        };

        StatusMessage = $"Menerapkan web filter ({mode}) ke {targets.Count} PC...";
        var results = await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        int successCount = results.Values.Count(r => r.Success);

        await _databaseService.LogActivityAsync("WEB_FILTER", $"SetWebFilter ({mode})", $"{successCount}/{targets.Count} PCs applied", successCount > 0);
        StatusMessage = mode == WebFilterMode.Disabled
            ? $"Filter web dinonaktifkan pada {successCount}/{targets.Count} PC."
            : $"Filter web ({mode} - {domains.Count} domain) diterapkan pada {successCount}/{targets.Count} PC.";
    }

    public async Task ClearWebFilterSelectedAsync()
    {
        await SetWebFilterSelectedAsync(WebFilterMode.Disabled, Array.Empty<string>());
    }

    public async Task ExportHardwareInventoryAsync()
    {
        string defaultFileName = $"LabSupport_Hardware_Inventory_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string? targetPath = null;

        if (SaveFileDialogHandler != null)
        {
            targetPath = SaveFileDialogHandler();
        }
        else
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Ekspor Laporan Inventaris Hardware Lab (.csv)",
                FileName = defaultFileName,
                Filter = "CSV File (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".csv"
            };
            if (sfd.ShowDialog() == true)
            {
                targetPath = sfd.FileName;
            }
        }

        if (string.IsNullOrWhiteSpace(targetPath)) return;

        try
        {
            await _databaseService.ExportHardwareInventoryCsvFileAsync(targetPath, Computers);
            await _databaseService.LogActivityAsync("ALL_PCS", "Export Hardware Inventory", $"Exported {Computers.Count} PCs to {Path.GetFileName(targetPath)}", true);
            StatusMessage = $"[SUKSES] Laporan inventaris hardware tersimpan di: {Path.GetFileName(targetPath)}";

            if (Application.Current != null)
            {
                MessageBox.Show(
                    $"Laporan inventaris hardware ({Computers.Count} unit PC) berhasil diekspor ke:\n{targetPath}",
                    "LabSupport — Inventaris Hardware",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal mengekspor inventaris hardware: {ex.Message}";
        }
    }

    public async Task ThrottleBandwidthHogAsync(LabComputer? computer)
    {
        if (computer == null || !computer.IsOnline) return;

        var cmd = new CommandMessage
        {
            Type = CommandType.ShowMessage,
            Payload = "Peringatan Guru: Komputer ini terdeteksi menggunakan bandwidth jaringan sangat tinggi (> 3 MB/s). Mohon hentikan video streaming atau download yang tidak berkaitan dengan pelajaran."
        };

        await _commandService.SendCommandAsync(computer.IpAddress, cmd);
        await _databaseService.LogActivityAsync(computer.Hostname, "Bandwidth Alert Sent", $"{computer.NetworkFormatted}", true);
        StatusMessage = $"⚠️ Peringatan pemakaian bandwidth terkirim ke {computer.EffectiveName}.";
    }

    private string? HandleHeartbeatDirective(HeartbeatMessage hb)
    {
        if (hb == null) return null;
        try
        {
            bool isPending = _databaseService.IsPendingDeletionAsync(hb.Hostname, hb.MacAddress).GetAwaiter().GetResult();
            if (isPending)
            {
                _ = _databaseService.RemovePendingDeletionAsync(hb.Hostname, hb.MacAddress);
                return "uninstall";
            }
        }
        catch { }
        return null;
    }

    public void HandleAgentUnregistered(UnregisterAgentMessage msg)
    {
        if (msg == null) return;

        var target = (!string.IsNullOrWhiteSpace(msg.MacAddress)
            ? Computers.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.MacAddress) && c.MacAddress.Equals(msg.MacAddress, StringComparison.OrdinalIgnoreCase))
            : null)
            ?? Computers.FirstOrDefault(c => c.Hostname.Equals(msg.Hostname, StringComparison.OrdinalIgnoreCase));

        var dispatcher = Application.Current?.Dispatcher;
        Action removeAction = () =>
        {
            if (target != null)
            {
                Computers.Remove(target);
                if (SelectedComputer == target) SelectedComputer = null;
                UpdateStats();
            }
        };

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(removeAction);
        }
        else
        {
            removeAction();
        }

        _ = _databaseService.DeleteComputerAsync(msg.Hostname, msg.MacAddress);
        _ = _databaseService.LogActivityAsync(msg.Hostname, "Uninstall Agent", $"Agent uninstalled directly from client PC ({msg.Reason}) [Uninstalled]", true);
        StatusMessage = $"PC '{msg.Hostname}' telah di-uninstall dari client dan dihapus dari manajemen.";
    }

    public async Task DeleteComputerAsync(LabComputer? computer)
    {
        if (computer == null) return;

        bool confirmed = false;
        if (ConfirmationDialogHandler != null)
        {
            confirmed = ConfirmationDialogHandler(
                "Konfirmasi Hapus PC",
                $"Apakah Anda yakin ingin menghapus PC '{computer.EffectiveName}'?\n\nService agent pada PC target akan di-uninstall.");
        }
        else if (Application.Current != null)
        {
            var res = MessageBox.Show(
                $"Apakah Anda yakin ingin menghapus PC '{computer.EffectiveName}'?\n\nService agent pada PC target akan otomatis di-uninstall.",
                "LabSupport — Hapus PC & Uninstall Agent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            confirmed = (res == MessageBoxResult.Yes);
        }
        else
        {
            confirmed = true;
        }

        if (!confirmed) return;

        StatusMessage = $"Menghapus PC '{computer.EffectiveName}'...";

        if (computer.IsOnline)
        {
            var cmd = new CommandMessage { Type = CommandType.UninstallAgent };
            try
            {
                await _commandService.SendCommandAsync(computer.IpAddress, cmd);
            }
            catch { }
        }
        else
        {
            await _databaseService.AddPendingDeletionAsync(computer.Hostname, computer.MacAddress, CurrentManagerId);
        }

        var dispatcher = Application.Current?.Dispatcher;
        Action removeAction = () =>
        {
            Computers.Remove(computer);
            if (SelectedComputer == computer) SelectedComputer = null;
            UpdateStats();
        };

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(removeAction);
        }
        else
        {
            removeAction();
        }

        await _databaseService.DeleteComputerAsync(computer.Hostname, computer.MacAddress);
        await _databaseService.LogActivityAsync(computer.Hostname, "Delete Computer", $"PC dihapus dari manajemen (Online: {computer.IsOnline}) [Uninstalled]", true);
        StatusMessage = $"PC '{computer.EffectiveName}' berhasil dihapus dari manajemen.";
    }

    public async Task BatchDeleteComputersAsync()
    {
        var targets = Computers.Where(c => c.IsSelected).ToList();
        if (targets.Count == 0 && SelectedComputer != null)
        {
            targets.Add(SelectedComputer);
        }

        if (targets.Count == 0)
        {
            StatusMessage = "Pilih minimal satu komputer untuk dihapus.";
            return;
        }

        bool confirmed = false;
        if (ConfirmationDialogHandler != null)
        {
            confirmed = ConfirmationDialogHandler(
                "Konfirmasi Hapus Terpilih",
                $"Apakah Anda yakin ingin menghapus {targets.Count} PC terpilih?\n\nService agent pada PC target akan di-uninstall.");
        }
        else if (Application.Current != null)
        {
            var res = MessageBox.Show(
                $"Apakah Anda yakin ingin menghapus {targets.Count} PC terpilih?\n\nService agent pada PC target akan otomatis di-uninstall.",
                "LabSupport — Hapus Terpilih",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            confirmed = (res == MessageBoxResult.Yes);
        }
        else
        {
            confirmed = true;
        }

        if (!confirmed) return;

        foreach (var pc in targets)
        {
            if (pc.IsOnline)
            {
                var cmd = new CommandMessage { Type = CommandType.UninstallAgent };
                try { await _commandService.SendCommandAsync(pc.IpAddress, cmd); } catch { }
            }
            else
            {
                await _databaseService.AddPendingDeletionAsync(pc.Hostname, pc.MacAddress, CurrentManagerId);
            }

            Computers.Remove(pc);
            await _databaseService.DeleteComputerAsync(pc.Hostname, pc.MacAddress);
            await _databaseService.LogActivityAsync(pc.Hostname, "Delete Computer", $"PC dihapus dalam batch (Online: {pc.IsOnline}) [Uninstalled]", true);
        }

        UpdateStats();
        StatusMessage = $"{targets.Count} PC berhasil dihapus dari manajemen.";
    }

    public async Task ExportUninstallerAsync()
    {
        try
        {
            string? targetPath = null;
            if (SaveFileDialogHandler != null)
            {
                targetPath = SaveFileDialogHandler();
            }
            else
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Simpan File Uninstaller Agent Lab (.bat)",
                    FileName = "uninstall_agent_lab.bat",
                    Filter = "Batch Script (*.bat)|*.bat|All Files (*.*)|*.*",
                    DefaultExt = ".bat"
                };

                if (sfd.ShowDialog() == true)
                {
                    targetPath = sfd.FileName;
                }
            }

            if (string.IsNullOrWhiteSpace(targetPath)) return;

            string ip = NetworkHelper.GetPrimaryLanIPv4Address();
            int port = _httpFileServer?.Port ?? 8080;
            string labCode = string.IsNullOrWhiteSpace(CurrentManagerId) ? "LAB-DEFAULT" : CurrentManagerId;

            string batContent = ScriptGenerator.GenerateUninstallBatchScript(ip, port, labCode);
            await File.WriteAllTextAsync(targetPath, batContent, Encoding.Default);

            StatusMessage = $"[SUKSES] Uninstaller (.bat) berhasil diekspor ke: {Path.GetFileName(targetPath)}";

            if (Application.Current != null)
            {
                MessageBox.Show(
                    $"File 'uninstall_agent_lab.bat' berhasil diekspor ke:\n{targetPath}\n\nJalankan file ini sebagai Administrator di PC lab mana pun untuk meng-uninstall agent secara bersih.",
                    "LabSupport — Export Uninstaller",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal mengekspor uninstaller: {ex.Message}";
        }
    }

    public async Task SendNotificationAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            targets = Computers.Where(c => c.IsOnline).ToList();
        }

        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer online untuk menerima notifikasi.";
            return;
        }

        var payload = NotificationDialogHelper.ShowDialog(targets.Count);
        if (payload == null)
        {
            return;
        }

        StatusMessage = $"Mengirim notifikasi ({payload.Type}) ke {targets.Count} PC siswa...";
        var cmd = new CommandMessage
        {
            Type = CommandType.ShowNotification,
            Payload = MessageSerializer.Serialize(payload)
        };

        int successCount = 0;
        var tasks = targets.Select(async comp =>
        {
            var resp = await _commandService.SendCommandAsync(comp.IpAddress, cmd);
            if (resp.Success)
            {
                Interlocked.Increment(ref successCount);
                await _databaseService.LogActivityAsync(comp.Hostname, "Notification Sent", $"{payload.Type} - {payload.Title}: {payload.Message}", true);
            }
            else
            {
                await _databaseService.LogActivityAsync(comp.Hostname, "Notification Failed", resp.Error ?? "Network Error", false);
            }
        });

        await Task.WhenAll(tasks);
        StatusMessage = $"📢 Notifikasi berhasil ditampilkan pada {successCount}/{targets.Count} PC siswa.";
    }

    public async Task CheckAgentConnectionAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            targets = Computers.Where(c => c.IsOnline).ToList();
        }

        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer online untuk diperiksa status koneksinya.";
            return;
        }

        StatusMessage = $"🔍 Memeriksa koneksi & program pada {targets.Count} PC lab...";

        int verifiedCount = 0;
        long totalElapsedMs = 0;
        var failedHosts = new System.Collections.Concurrent.ConcurrentBag<string>();
        string localIp = NetworkHelper.GetPrimaryLanIPv4Address();

        var tasks = targets.Select(async comp =>
        {
            var payload = new NotificationPayload
            {
                Title = "Status Program & Koneksi",
                Message = "PC terverifikasi terpasang LabSupport Agent dan terhubung aktif dengan PC Manager.",
                Type = NotificationType.ConnectionCheck,
                DurationSeconds = 4,
                ShowHostInfo = true,
                Hostname = comp.Hostname,
                IpAddress = comp.IpAddress,
                RoomName = comp.RoomName,
                ManagerHost = localIp
            };

            var cmd = new CommandMessage
            {
                Type = CommandType.ShowNotification,
                Payload = MessageSerializer.Serialize(payload)
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = await _commandService.SendCommandAsync(comp.IpAddress, cmd, timeoutMs: 3500);
            sw.Stop();

            if (resp.Success)
            {
                Interlocked.Increment(ref verifiedCount);
                Interlocked.Add(ref totalElapsedMs, sw.ElapsedMilliseconds);
                await _databaseService.LogActivityAsync(comp.Hostname, "Connection Check", $"Agent verified connected ({sw.ElapsedMilliseconds}ms)", true);
            }
            else
            {
                failedHosts.Add(comp.EffectiveName);
                await _databaseService.LogActivityAsync(comp.Hostname, "Connection Check Failed", resp.Error ?? "Timeout", false);
            }
        });

        await Task.WhenAll(tasks);

        long avgMs = verifiedCount > 0 ? (totalElapsedMs / verifiedCount) : 0;
        if (failedHosts.IsEmpty)
        {
            StatusMessage = $"✅ Pengecekan selesai: {verifiedCount}/{targets.Count} PC terverifikasi aktif & terhubung (rata-rata {avgMs}ms).";
        }
        else
        {
            StatusMessage = $"⚠️ Cek selesai: {verifiedCount}/{targets.Count} PC terhubung ({avgMs}ms). Gagal ({failedHosts.Count} PC): {string.Join(", ", failedHosts)}";
        }
    }
}

public class RoomSelectionItemViewModel : ViewModelBase
{
    private bool _isChecked;
    public string RoomName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int OnlineCount { get; set; }
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
    public string DisplayText => $"{RoomName} ({TotalCount} PC, {OnlineCount} Online)";
}

