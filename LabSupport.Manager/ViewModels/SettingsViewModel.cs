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

    private string _selectedLanguage = "id-ID";
    private int _httpPort = 8080;
    private int _agentPort = 5902;
    private int _heartbeatInterval = 5;
    private string _databasePath = "labsupport.db";
    private string _statusMessage = string.Empty;

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

    public ICommand SaveSettingsCommand { get; }
    public ICommand RefreshLogsCommand { get; }

    public SettingsViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;

        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        RefreshLogsCommand = new AsyncRelayCommand(RefreshLogsAsync);

        _ = LoadSettingsAsync();
        _ = RefreshLogsAsync();
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
