using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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

    private string _searchText = string.Empty;
    private string _selectedFilter = "All"; // All, Online, Offline
    private LabComputer? _selectedComputer;
    private string _statusMessage = "Siap.";
    private bool _isBroadcasting;

    public ObservableCollection<LabComputer> Computers { get; } = new();
    public ICollectionView FilteredComputers { get; }

    public event Action<LabComputer>? OnOpenRemoteRequested;
    public event Action<LabComputer>? OnOpenScriptRequested;

    public bool IsBroadcasting
    {
        get => _isBroadcasting;
        set => SetProperty(ref _isBroadcasting, value);
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

    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand ShutdownSelectedCommand { get; }
    public ICommand RestartSelectedCommand { get; }
    public ICommand LockSelectedCommand { get; }
    public ICommand WakeSelectedCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenRemoteCommand { get; }
    public ICommand OpenScriptCommand { get; }
    public ICommand StartBroadcastCommand { get; }
    public ICommand StopBroadcastCommand { get; }

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

        SelectAllCommand = new RelayCommand(() => SetSelectionAll(true));
        DeselectAllCommand = new RelayCommand(() => SetSelectionAll(false));
        ShutdownSelectedCommand = new AsyncRelayCommand(ShutdownSelectedAsync);
        RestartSelectedCommand = new AsyncRelayCommand(RestartSelectedAsync);
        LockSelectedCommand = new AsyncRelayCommand(LockSelectedAsync);
        WakeSelectedCommand = new AsyncRelayCommand(WakeSelectedAsync);
        RefreshCommand = new AsyncRelayCommand(LoadComputersFromDbAsync);
        StartBroadcastCommand = new AsyncRelayCommand(StartBroadcastAsync);
        StopBroadcastCommand = new AsyncRelayCommand(StopBroadcastAsync);

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
        _offlineCheckTimer.Tick += (_, _) => CheckOfflineComputers();
        _offlineCheckTimer.Start();
    }

    private bool FilterComputerItem(object obj)
    {
        if (obj is not LabComputer pc) return false;

        if (SelectedFilter == "Online" && !pc.IsOnline) return false;
        if (SelectedFilter == "Offline" && pc.IsOnline) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        string query = SearchText.Trim().ToLowerInvariant();
        return pc.Hostname.ToLowerInvariant().Contains(query) ||
               pc.IpAddress.ToLowerInvariant().Contains(query) ||
               pc.MacAddress.ToLowerInvariant().Contains(query);
    }

    public async Task StartBroadcastAsync()
    {
        var targets = Computers.Where(c => c.IsOnline).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Tidak ada komputer online untuk broadcast.";
            return;
        }

        string localIp = NetworkHelper.GetLocalIPv4Addresses().FirstOrDefault(ip => !ip.StartsWith("127.")) ?? "127.0.0.1";
        int port = _httpFileServer?.Port ?? 8080;
        string streamUrl = $"http://{localIp}:{port}/presentation/view";

        if (_httpFileServer != null)
        {
            var screenCapture = new ScreenCaptureService();
            _httpFileServer.PresentationFrameProvider = () => screenCapture.CaptureScreenJpeg(65);
        }

        StatusMessage = $"Memulai broadcast layar ke {targets.Count} PC...";
        var cmd = new CommandMessage
        {
            Type = CommandType.StartBroadcast,
            Payload = streamUrl
        };

        await _commandService.SendBroadcastCommandAsync(targets.Select(t => t.IpAddress), cmd);
        IsBroadcasting = true;
        StatusMessage = $"Broadcast layar aktif ({targets.Count} PC terhubung).";
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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal memuat daftar PC: {ex.Message}";
        }
    }

    public void HandleHeartbeat(HeartbeatMessage hb)
    {
        var existing = Computers.FirstOrDefault(c => c.Hostname.Equals(hb.Hostname, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            var newPc = new LabComputer
            {
                Hostname = hb.Hostname,
                IpAddress = hb.IpAddress,
                MacAddress = hb.MacAddress,
                OsVersion = hb.OsVersion,
                CpuUsage = hb.CpuUsage,
                RamUsageMb = hb.RamUsageMb,
                RamTotalMb = hb.RamTotalMb,
                DiskFreeGb = hb.DiskFreeGb,
                LastSeen = DateTime.UtcNow,
                IsOnline = true
            };

            if (!string.IsNullOrEmpty(hb.ScreenshotBase64))
            {
                try { newPc.SetThumbnailBytes(Convert.FromBase64String(hb.ScreenshotBase64)); } catch { }
            }

            Computers.Add(newPc);
            _ = _databaseService.UpsertComputerAsync(newPc);
        }
        else
        {
            existing.IpAddress = hb.IpAddress;
            if (!string.IsNullOrEmpty(hb.MacAddress)) existing.MacAddress = hb.MacAddress;
            if (!string.IsNullOrEmpty(hb.OsVersion)) existing.OsVersion = hb.OsVersion;
            existing.CpuUsage = hb.CpuUsage;
            existing.RamUsageMb = hb.RamUsageMb;
            existing.RamTotalMb = hb.RamTotalMb;
            existing.DiskFreeGb = hb.DiskFreeGb;
            existing.LastSeen = DateTime.UtcNow;
            existing.IsOnline = true;

            if (!string.IsNullOrEmpty(hb.ScreenshotBase64))
            {
                try { existing.SetThumbnailBytes(Convert.FromBase64String(hb.ScreenshotBase64)); } catch { }
            }

            existing.NotifyMetricsChanged();
            _ = _databaseService.UpsertComputerAsync(existing);
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
    }

    private void SetSelectionAll(bool select)
    {
        foreach (var c in Computers)
        {
            c.IsSelected = select;
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

    private async Task WakeSelectedAsync()
    {
        var targets = Computers.Where(c => c.IsSelected && !string.IsNullOrEmpty(c.MacAddress)).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Pilih komputer dengan MAC Address yang tersimpan untuk WoL.";
            return;
        }

        StatusMessage = $"Mengirim Wake-on-LAN Magic Packet ke {targets.Count} PC...";
        int sent = 0;
        foreach (var t in targets)
        {
            bool ok = await _wolService.WakeComputerAsync(t.Hostname, t.MacAddress);
            if (ok) sent++;
        }

        StatusMessage = $"Wake-on-LAN: {sent}/{targets.Count} packet terkirim.";
    }
}
