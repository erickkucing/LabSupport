using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using LabSupport.Shared.Models;
using LabSupport.Shared.Services;

namespace LabSupport.Manager.ViewModels;

public class GenerateAgentViewModel : ViewModelBase
{
    private readonly DatabaseService? _databaseService;
    private string _selectedManagerIp = "127.0.0.1";
    private int _managerPort = 8080;
    private string _managerId = string.Empty;
    private string _serviceName = "LabSupportAgent";
    private string _outputDirectory = string.Empty;
    private string _ps1Preview = string.Empty;
    private string _batPreview = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isGenerating;

    private string _newRoomName = string.Empty;
    private int _newRoomCapacity = 30;
    private LabRoom? _selectedLabRoom;

    public Func<string?>? SaveFileDialogHandler { get; set; }
    public Func<string?>? FolderBrowserDialogHandler { get; set; }
    public event Action<string>? OnManagerIdChanged;

    public ObservableCollection<string> AvailableIpAddresses { get; } = new();
    public ObservableCollection<LabRoom> MultiLabRooms { get; } = new();

    public string NewRoomName
    {
        get => _newRoomName;
        set => SetProperty(ref _newRoomName, value);
    }

    public int NewRoomCapacity
    {
        get => _newRoomCapacity;
        set => SetProperty(ref _newRoomCapacity, value);
    }

    public LabRoom? SelectedLabRoom
    {
        get => _selectedLabRoom;
        set
        {
            if (SetProperty(ref _selectedLabRoom, value))
            {
                UpdatePreview();
            }
        }
    }

    public string SelectedManagerIp
    {
        get => _selectedManagerIp;
        set
        {
            if (SetProperty(ref _selectedManagerIp, value))
            {
                UpdatePreview();
            }
        }
    }

    public int ManagerPort
    {
        get => _managerPort;
        set
        {
            if (SetProperty(ref _managerPort, value))
            {
                UpdatePreview();
            }
        }
    }

    public string ManagerId
    {
        get => _managerId;
        set
        {
            if (SetProperty(ref _managerId, value))
            {
                UpdatePreview();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (_databaseService != null)
                    {
                        _ = _databaseService.SetSettingAsync("ManagerId", value);
                    }
                    OnManagerIdChanged?.Invoke(value);
                }
            }
        }
    }

    public string ServiceName
    {
        get => _serviceName;
        set
        {
            if (SetProperty(ref _serviceName, value))
            {
                UpdatePreview();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value);
    }

    public string Ps1Preview
    {
        get => _ps1Preview;
        private set => SetProperty(ref _ps1Preview, value);
    }

    public string BatPreview
    {
        get => _batPreview;
        private set => SetProperty(ref _batPreview, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsGenerating
    {
        get => _isGenerating;
        set => SetProperty(ref _isGenerating, value);
    }

    public ICommand GenerateCommand { get; }
    public ICommand SaveSingleBatCommand { get; }
    public ICommand RegenerateLabCodeCommand { get; }
    public ICommand RefreshIpsCommand { get; }
    public ICommand AddRoomCommand { get; }
    public ICommand RemoveRoomCommand { get; }
    public ICommand ExportAllLabBatsCommand { get; }
    public ICommand ExportSelectedLabBatCommand { get; }

    public GenerateAgentViewModel(DatabaseService? databaseService = null)
    {
        _databaseService = databaseService;
        _managerId = GenerateRandomLabCode();

        GenerateCommand = new AsyncRelayCommand(GeneratePackageAsync, () => !IsGenerating);
        SaveSingleBatCommand = new AsyncRelayCommand(SaveSingleBatAsync, () => !IsGenerating);
        RegenerateLabCodeCommand = new RelayCommand(RegenerateLabCode);
        RefreshIpsCommand = new RelayCommand(RefreshNetworkIps);

        AddRoomCommand = new AsyncRelayCommand(AddRoomAsync);
        RemoveRoomCommand = new AsyncRelayCommand(p => RemoveRoomAsync(p as LabRoom));
        ExportAllLabBatsCommand = new AsyncRelayCommand(ExportAllLabBatsAsync, () => !IsGenerating);
        ExportSelectedLabBatCommand = new AsyncRelayCommand(ExportSelectedLabBatAsync, () => !IsGenerating);

        OutputDirectory = Path.Combine(AppContext.BaseDirectory, "LabAgentPackage");

        if (_databaseService != null)
        {
            _ = LoadStoredManagerIdAsync();
            _ = LoadStoredRoomsAsync();
        }
        else
        {
            SeedDefaultRooms();
        }

        RefreshNetworkIps();
        UpdatePreview();
    }

    private void SeedDefaultRooms()
    {
        MultiLabRooms.Add(new LabRoom { Name = "Lab 209A", Capacity = 30, Description = "Laboratorium 209A" });
        MultiLabRooms.Add(new LabRoom { Name = "Lab 209B", Capacity = 20, Description = "Laboratorium 209B" });
        SelectedLabRoom = MultiLabRooms[0];
    }

    private async Task LoadStoredRoomsAsync()
    {
        try
        {
            if (_databaseService != null)
            {
                var rooms = await _databaseService.GetAllRoomsAsync();
                if (rooms.Count > 0)
                {
                    MultiLabRooms.Clear();
                    foreach (var r in rooms) MultiLabRooms.Add(r);
                }
                else
                {
                    var r1 = new LabRoom { Name = "Lab 209A", Capacity = 30, Description = "Laboratorium 209A" };
                    var r2 = new LabRoom { Name = "Lab 209B", Capacity = 20, Description = "Laboratorium 209B" };
                    MultiLabRooms.Add(r1);
                    MultiLabRooms.Add(r2);
                    await _databaseService.UpsertRoomAsync(r1);
                    await _databaseService.UpsertRoomAsync(r2);
                }
                if (MultiLabRooms.Count > 0) SelectedLabRoom = MultiLabRooms[0];
            }
        }
        catch { }
    }

    private async Task LoadStoredManagerIdAsync()
    {
        try
        {
            if (_databaseService != null)
            {
                string stored = await _databaseService.GetSettingAsync("ManagerId");
                if (!string.IsNullOrWhiteSpace(stored))
                {
                    ManagerId = stored;
                }
            }
        }
        catch { }
    }

    public static string GenerateRandomLabCode()
    {
        return $"LAB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    }

    public void RegenerateLabCode()
    {
        string newCode = GenerateRandomLabCode();
        ManagerId = newCode;
    }

    public void RefreshNetworkIps()
    {
        AvailableIpAddresses.Clear();
        var ips = NetworkHelper.GetLocalIPv4Addresses();
        foreach (var ip in ips)
        {
            AvailableIpAddresses.Add(ip);
        }

        string primary = NetworkHelper.GetPrimaryLanIPv4Address();
        if (!string.IsNullOrEmpty(primary))
        {
            SelectedManagerIp = primary;
        }
        else if (AvailableIpAddresses.Count > 0)
        {
            SelectedManagerIp = AvailableIpAddresses[0];
        }
    }

    private void UpdatePreview()
    {
        string currentRoom = SelectedLabRoom?.Name ?? "General";
        int currentCap = SelectedLabRoom?.Capacity ?? 30;
        Ps1Preview = ScriptGenerator.GeneratePowerShellScript(SelectedManagerIp, ManagerPort, ManagerId, ServiceName, @"C:\ProgramData\LabSupport", currentRoom, currentCap);
        BatPreview = ScriptGenerator.GenerateSingleBatchInstaller(SelectedManagerIp, ManagerPort, ManagerId, ServiceName, @"C:\ProgramData\LabSupport", currentRoom, currentCap);
    }

    public async Task AddRoomAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRoomName))
        {
            StatusMessage = "Nama ruang tidak boleh kosong.";
            return;
        }

        string trimmedName = NewRoomName.Trim();
        if (MultiLabRooms.Any(r => r.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"Ruang '{trimmedName}' sudah ada di daftar.";
            return;
        }

        int cap = NewRoomCapacity > 0 ? NewRoomCapacity : 30;
        var newRoom = new LabRoom
        {
            Name = trimmedName,
            Capacity = cap,
            Description = $"Laboratorium {trimmedName}"
        };

        MultiLabRooms.Add(newRoom);
        SelectedLabRoom = newRoom;
        NewRoomName = string.Empty;
        NewRoomCapacity = 30;

        if (_databaseService != null)
        {
            await _databaseService.UpsertRoomAsync(newRoom);
        }

        StatusMessage = $"Ruang '{newRoom.Name}' (Kapasitas: {newRoom.Capacity} PC) berhasil ditambahkan.";
    }

    public async Task RemoveRoomAsync(LabRoom? room)
    {
        if (room == null) return;
        MultiLabRooms.Remove(room);
        if (_databaseService != null)
        {
            await _databaseService.DeleteRoomAsync(room.Name);
        }
        if (SelectedLabRoom == room)
        {
            SelectedLabRoom = MultiLabRooms.FirstOrDefault();
        }
        StatusMessage = $"Ruang '{room.Name}' dihapus dari daftar.";
    }

    public async Task ExportAllLabBatsAsync()
    {
        if (MultiLabRooms.Count == 0)
        {
            StatusMessage = "Tambahkan minimal satu ruang lab untuk diekspor.";
            return;
        }

        string? targetDir = null;
        if (FolderBrowserDialogHandler != null)
        {
            targetDir = FolderBrowserDialogHandler();
        }
        else
        {
            targetDir = Path.Combine(AppContext.BaseDirectory, "ExportedLabBats");
        }

        if (string.IsNullOrWhiteSpace(targetDir)) return;

        IsGenerating = true;
        StatusMessage = "Mengekspor file .BAT untuk semua ruang lab...";

        try
        {
            string agentSourcePath = Path.Combine(AppContext.BaseDirectory, "LabSupport.Agent.exe");
            await ScriptGenerator.GenerateMultiLabPackageAsync(
                targetDir,
                SelectedManagerIp,
                ManagerPort,
                ManagerId,
                MultiLabRooms,
                ServiceName,
                agentSourcePath);

            StatusMessage = $"SUKSES! Berhasil mengekspor {MultiLabRooms.Count} file .BAT ke folder:\n{targetDir}\n\nFile yang dihasilkan:\n" +
                            string.Join("\n", MultiLabRooms.Select(r => $"• pasang_agent_{string.Join("_", r.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_")}.bat ({r.Capacity} PC)"));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal mengekspor file .BAT multi-lab: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    public async Task ExportSelectedLabBatAsync()
    {
        var targetRoom = SelectedLabRoom ?? MultiLabRooms.FirstOrDefault();
        if (targetRoom == null)
        {
            StatusMessage = "Pilih ruang lab yang ingin diekspor.";
            return;
        }

        string safeName = string.Join("_", targetRoom.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        string defaultFileName = $"pasang_agent_{safeName}.bat";

        string? targetPath = null;
        if (SaveFileDialogHandler != null)
        {
            targetPath = SaveFileDialogHandler();
        }
        else
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Simpan File Installer .BAT untuk {targetRoom.Name}",
                FileName = defaultFileName,
                Filter = "Batch Script (*.bat)|*.bat|All Files (*.*)|*.*",
                DefaultExt = ".bat"
            };

            if (sfd.ShowDialog() == true)
            {
                targetPath = sfd.FileName;
            }
        }

        if (string.IsNullOrWhiteSpace(targetPath)) return;

        IsGenerating = true;
        StatusMessage = $"Menyimpan installer .BAT untuk {targetRoom.Name}...";

        try
        {
            string batContent = ScriptGenerator.GenerateSingleBatchInstaller(
                SelectedManagerIp,
                ManagerPort,
                ManagerId,
                ServiceName,
                @"C:\ProgramData\LabSupport",
                targetRoom.Name,
                targetRoom.Capacity);

            await File.WriteAllTextAsync(targetPath, batContent, Encoding.Default);
            StatusMessage = $"SUKSES! Installer untuk ruang '{targetRoom.Name}' ({targetRoom.Capacity} PC) berhasil disimpan ke:\n{targetPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal menyimpan installer: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    public async Task SaveSingleBatAsync()
    {
        string currentRoom = SelectedLabRoom?.Name ?? "General";
        int currentCap = SelectedLabRoom?.Capacity ?? 30;
        string safeRoomName = string.Join("_", currentRoom.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        string defaultFileName = $"pasang_agent_{safeRoomName}.bat";

        string? targetPath = null;
        if (SaveFileDialogHandler != null)
        {
            targetPath = SaveFileDialogHandler();
        }
        else
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Simpan File Installer Agent Lab (.bat) - {currentRoom}",
                FileName = defaultFileName,
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

        IsGenerating = true;
        StatusMessage = $"Menyimpan file installer standalone (.bat) untuk {currentRoom}...";

        try
        {
            string batContent = ScriptGenerator.GenerateSingleBatchInstaller(
                SelectedManagerIp,
                ManagerPort,
                ManagerId,
                ServiceName,
                @"C:\ProgramData\LabSupport",
                currentRoom,
                currentCap);

            await File.WriteAllTextAsync(targetPath, batContent, Encoding.Default);

            StatusMessage = $"SUKSES! File installer mandiri untuk ruang '{currentRoom}' ({currentCap} PC) berhasil disimpan di:\n{targetPath}\n\nLangkah selanjutnya:\n1. Salin file {defaultFileName} ini ke Flashdisk (USB).\n2. Tancapkan ke PC Lab dan klik kanan -> 'Run as Administrator'.\n3. Agent akan otomatis di-download dan terdaftar di kategori ruang '{currentRoom}'!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal menyimpan file installer: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private async Task GeneratePackageAsync()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusMessage = "Pilih direktori output terlebih dahulu.";
            return;
        }

        IsGenerating = true;
        StatusMessage = "Men-generate paket agent...";

        try
        {
            string agentSourcePath = Path.Combine(AppContext.BaseDirectory, "LabSupport.Agent.exe");
            await ScriptGenerator.GenerateDeploymentPackageAsync(
                OutputDirectory,
                SelectedManagerIp,
                ManagerPort,
                ManagerId,
                ServiceName,
                agentSourcePath);

            StatusMessage = $"SUKSES! File berhasil dibuat di:\n{OutputDirectory}\n(Termasuk install_agent.bat [Single-File Standalone] dan install_agent.ps1)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal men-generate file: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }
}
