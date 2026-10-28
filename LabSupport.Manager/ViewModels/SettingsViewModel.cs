using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LabSupport.Shared.Services;
using LabSupport.Shared.Models;

namespace LabSupport.Manager.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly AiScriptGeneratorService _aiGeneratorService;

    private string _selectedLanguage = "id-ID";
    private int _httpPort = 8080;
    private int _agentPort = 5902;
    private int _heartbeatInterval = 5;
    private string _databasePath = "labsupport.db";
    private string _statusMessage = string.Empty;
    private bool _isDailyShutdownEnabled;
    private string _dailyShutdownTime = "17:00";
    private bool _isAppGuardEnabled;
    private string _appGuardBlacklist = "steam,roblox,discord,cheatengine,epicgames,valorant,bittorrent,spotify,telegram";

    private string _aiRouterBaseUrl = "https://api.9router.com/v1";
    private string _aiRouterApiKey = string.Empty;
    private string _aiRouterModel = "openai/gpt-4o-mini";
    private string _aiConnectionStatus = string.Empty;
    private bool _isTestingAiConnection;

    public ObservableCollection<ActivityLog> ActivityLogs { get; } = new();

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
            {
                ApplyLanguage(value);
            }
        }
    }

    public int HttpPort
    {
        get => _httpPort;
        set => SetProperty(ref _httpPort, value);
    }

    public int AgentPort
    {
        get => _agentPort;
        set => SetProperty(ref _agentPort, value);
    }

    public int HeartbeatInterval
    {
        get => _heartbeatInterval;
        set => SetProperty(ref _heartbeatInterval, value);
    }

    public string DatabasePath
    {
        get => _databasePath;
        set => SetProperty(ref _databasePath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsDailyShutdownEnabled
    {
        get => _isDailyShutdownEnabled;
        set => SetProperty(ref _isDailyShutdownEnabled, value);
    }

    public string DailyShutdownTime
    {
        get => _dailyShutdownTime;
        set => SetProperty(ref _dailyShutdownTime, value);
    }

    public bool IsAppGuardEnabled
    {
        get => _isAppGuardEnabled;
        set => SetProperty(ref _isAppGuardEnabled, value);
    }

    public string AppGuardBlacklist
    {
        get => _appGuardBlacklist;
        set => SetProperty(ref _appGuardBlacklist, value);
    }

    public string AiRouterBaseUrl
    {
        get => _aiRouterBaseUrl;
        set => SetProperty(ref _aiRouterBaseUrl, value);
    }

    public string AiRouterApiKey
    {
        get => _aiRouterApiKey;
        set => SetProperty(ref _aiRouterApiKey, value);
    }

    public string AiRouterModel
    {
        get => _aiRouterModel;
        set => SetProperty(ref _aiRouterModel, value);
    }

    public string AiConnectionStatus
    {
        get => _aiConnectionStatus;
        set => SetProperty(ref _aiConnectionStatus, value);
    }

    public bool IsTestingAiConnection
    {
        get => _isTestingAiConnection;
        set => SetProperty(ref _isTestingAiConnection, value);
    }

    public ICommand SaveSettingsCommand { get; }
    public ICommand RefreshLogsCommand { get; }
    public ICommand ExportLogsCommand { get; }
    public ICommand ExportHardwareInventoryCommand { get; }
    public ICommand TestAiConnectionCommand { get; }
    public Func<string?>? SaveFileDialogHandler { get; set; }

    public SettingsViewModel(DatabaseService databaseService, AiScriptGeneratorService? aiGeneratorService = null)
    {
        _databaseService = databaseService;
        _aiGeneratorService = aiGeneratorService ?? new AiScriptGeneratorService();

        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        RefreshLogsCommand = new AsyncRelayCommand(RefreshLogsAsync);
        ExportLogsCommand = new AsyncRelayCommand(ExportLogsAsync);
        ExportHardwareInventoryCommand = new AsyncRelayCommand(ExportHardwareInventoryAsync);
        TestAiConnectionCommand = new AsyncRelayCommand(TestAiConnectionAsync, () => !IsTestingAiConnection);

        _ = LoadSettingsAsync();
        _ = RefreshLogsAsync();
    }

    public async Task TestAiConnectionAsync()
    {
        if (IsTestingAiConnection) return;
        try
        {
            IsTestingAiConnection = true;
            AiConnectionStatus = "Menguji koneksi ke 9router AI...";
            var (success, message) = await _aiGeneratorService.TestConnectionAsync(AiRouterBaseUrl, AiRouterApiKey, AiRouterModel);
            AiConnectionStatus = success ? $"✅ {message}" : $"❌ {message}";
        }
        catch (Exception ex)
        {
            AiConnectionStatus = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsTestingAiConnection = false;
        }
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            SelectedLanguage = await _databaseService.GetSettingAsync("Language", "id-ID");
            string hp = await _databaseService.GetSettingAsync("HttpPort", "8080");
            if (int.TryParse(hp, out int port)) HttpPort = port;

            string ap = await _databaseService.GetSettingAsync("AgentPort", "5902");
            if (int.TryParse(ap, out int aport)) AgentPort = aport;

            string hi = await _databaseService.GetSettingAsync("HeartbeatInterval", "5");
            if (int.TryParse(hi, out int hint)) HeartbeatInterval = hint;

            var (schedEnabled, schedTime) = await _databaseService.GetSchedulerSettingsAsync();
            IsDailyShutdownEnabled = schedEnabled;
            DailyShutdownTime = schedTime.ToString(@"hh\:mm");

            var (guardEnabled, guardBlacklist) = await _databaseService.GetAppGuardSettingsAsync();
            IsAppGuardEnabled = guardEnabled;
            if (!string.IsNullOrWhiteSpace(guardBlacklist))
            {
                AppGuardBlacklist = guardBlacklist;
            }

            var (aiUrl, aiKey, aiModel) = await _databaseService.GetAiRouterSettingsAsync();
            if (!string.IsNullOrWhiteSpace(aiUrl)) AiRouterBaseUrl = aiUrl;
            if (!string.IsNullOrWhiteSpace(aiKey)) AiRouterApiKey = aiKey;
            if (!string.IsNullOrWhiteSpace(aiModel)) AiRouterModel = aiModel;
        }
        catch { }
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            await _databaseService.SetSettingAsync("Language", SelectedLanguage);
            await _databaseService.SetSettingAsync("HttpPort", HttpPort.ToString());
            await _databaseService.SetSettingAsync("AgentPort", AgentPort.ToString());
            await _databaseService.SetSettingAsync("HeartbeatInterval", HeartbeatInterval.ToString());

            if (TimeSpan.TryParse(DailyShutdownTime, out TimeSpan time))
            {
                await _databaseService.SaveSchedulerSettingsAsync(IsDailyShutdownEnabled, time);
            }
            else
            {
                await _databaseService.SaveSchedulerSettingsAsync(IsDailyShutdownEnabled, new TimeSpan(17, 0, 0));
            }

            await _databaseService.SaveAppGuardSettingsAsync(IsAppGuardEnabled, AppGuardBlacklist);

            await _databaseService.SaveAiRouterSettingsAsync(AiRouterBaseUrl, AiRouterApiKey, AiRouterModel);

            StatusMessage = "Pengaturan berhasil disimpan.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal menyimpan: {ex.Message}";
        }
    }

    public async Task RefreshLogsAsync()
    {
        try
        {
            var logs = await _databaseService.GetActivityLogsAsync(100);
            ActivityLogs.Clear();
            foreach (var l in logs)
            {
                ActivityLogs.Add(l);
            }
        }
        catch { }
    }

    public async Task ExportLogsAsync()
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
                    Title = "Ekspor Log Aktivitas (.csv)",
                    FileName = $"log_aktivitas_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                    Filter = "CSV File (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = ".csv"
                };
                if (sfd.ShowDialog() == true) targetPath = sfd.FileName;
            }

            if (string.IsNullOrWhiteSpace(targetPath)) return;

            await _databaseService.ExportActivityLogsToCsvFileAsync(targetPath);
            StatusMessage = $"Log berhasil diekspor ke: {targetPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal mengekspor log: {ex.Message}";
        }
    }

    public async Task ExportHardwareInventoryAsync()
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
                    Title = "Ekspor Inventaris Hardware Lab (.csv)",
                    FileName = $"inventaris_hardware_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                    Filter = "CSV File (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = ".csv"
                };
                if (sfd.ShowDialog() == true) targetPath = sfd.FileName;
            }

            if (string.IsNullOrWhiteSpace(targetPath)) return;

            await _databaseService.ExportHardwareInventoryCsvFileAsync(targetPath);
            StatusMessage = $"Inventaris hardware berhasil diekspor ke: {targetPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Gagal mengekspor inventaris hardware: {ex.Message}";
        }
    }

    private void ApplyLanguage(string lang)
    {
        try
        {
            var dictUri = new Uri($"Resources/Localization/Strings.{lang}.xaml", UriKind.Relative);
            var newDict = new ResourceDictionary { Source = dictUri };

            var merged = Application.Current?.Resources?.MergedDictionaries;
            if (merged != null)
            {
                // Remove existing strings dictionary if any
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    if (merged[i].Source?.OriginalString.Contains("Strings.") == true)
                    {
                        merged.RemoveAt(i);
                    }
                }
                merged.Add(newDict);
            }
        }
        catch { }
    }
}
