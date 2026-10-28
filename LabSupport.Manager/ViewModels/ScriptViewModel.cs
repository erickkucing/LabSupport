using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using LabSupport.Manager.Models;
using LabSupport.Shared.Models;
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
    private readonly AiScriptGeneratorService _aiGeneratorService;

    private bool _isVisualMode = true;
    private ScriptType _selectedScriptType = ScriptType.PowerShell;
    private string _scriptContent = string.Empty;
    private string _selectedTargetMode = "AllOnline"; // "AllOnline" or "Selected"
    private bool _isExecuting;
    private string _statusText = "Siap mengeksekusi script.";

    // AI Assistant fields
    private bool _isAiPromptOpen;
    private string _aiPromptText = string.Empty;
    private bool _isAiGenerating;
    private string _aiStatusMessage = string.Empty;

    // Visual Builder Recipe
    private ScriptRecipe _currentRecipe = new() { Name = "Resep Script Lab" };

    public ObservableCollection<ScriptActionStep> ActionSteps { get; } = new();
    public ObservableCollection<ScriptExecutionResultItem> Results { get; } = new();
    public IReadOnlyList<ScriptRecipe> AvailablePresets { get; } = ScriptPresetService.GetDefaultPresets();

    public bool IsVisualMode
    {
        get => _isVisualMode;
        set
        {
            if (SetProperty(ref _isVisualMode, value))
            {
                OnPropertyChanged(nameof(IsCodeMode));
                if (value)
                {
                    SelectedScriptType = ScriptType.PowerShell;
                }
                SyncScriptFromRecipe();
            }
        }
    }

    public bool IsCodeMode
    {
        get => !_isVisualMode;
        set => IsVisualMode = !value;
    }

    public ScriptRecipe CurrentRecipe
    {
        get => _currentRecipe;
        set
        {
            if (SetProperty(ref _currentRecipe, value))
            {
                OnPropertyChanged(nameof(RecipeName));
                OnPropertyChanged(nameof(RecipeDescription));
            }
        }
    }

    public string RecipeName
    {
        get => CurrentRecipe.Name;
        set
        {
            if (CurrentRecipe.Name != value)
            {
                CurrentRecipe.Name = value ?? string.Empty;
                OnPropertyChanged();
                SyncScriptFromRecipe();
            }
        }
    }

    public string RecipeDescription
    {
        get => CurrentRecipe.Description;
        set
        {
            if (CurrentRecipe.Description != value)
            {
                CurrentRecipe.Description = value ?? string.Empty;
                OnPropertyChanged();
                SyncScriptFromRecipe();
            }
        }
    }

    public bool IsAiPromptOpen
    {
        get => _isAiPromptOpen;
        set => SetProperty(ref _isAiPromptOpen, value);
    }

    public string AiPromptText
    {
        get => _aiPromptText;
        set => SetProperty(ref _aiPromptText, value);
    }

    public bool IsAiGenerating
    {
        get => _isAiGenerating;
        set => SetProperty(ref _isAiGenerating, value);
    }

    public string AiStatusMessage
    {
        get => _aiStatusMessage;
        set => SetProperty(ref _aiStatusMessage, value);
    }

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

    public ICommand ToggleAiPromptCommand { get; }
    public ICommand GenerateWithAiCommand { get; }
    public ICommand AddActionCommand { get; }
    public ICommand RemoveStepCommand { get; }
    public ICommand MoveUpStepCommand { get; }
    public ICommand MoveDownStepCommand { get; }
    public ICommand ClearRecipeCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand SaveRecipeCommand { get; }
    public ICommand LoadRecipeCommand { get; }
    public ICommand ExportBatchScriptCommand { get; }
    public ICommand RefreshPreviewCommand { get; }

    public Func<string?>? SaveRecipeFileDialogHandler { get; set; }
    public Func<string?>? OpenRecipeFileDialogHandler { get; set; }
    public Func<string?>? ExportBatchFileDialogHandler { get; set; }

    public ScriptViewModel(
        AgentCommandService commandService,
        DatabaseService databaseService,
        ObservableCollection<LabComputer> allComputers,
        AiScriptGeneratorService? aiGeneratorService = null)
    {
        _commandService = commandService;
        _databaseService = databaseService;
        _allComputers = allComputers;
        _aiGeneratorService = aiGeneratorService ?? new AiScriptGeneratorService();

        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => !IsExecuting && !string.IsNullOrWhiteSpace(ScriptContent));
        ClearResultsCommand = new RelayCommand(() => Results.Clear());
        SelectTemplateCommand = new RelayCommand(p => ApplyTemplate(p?.ToString() ?? ""));

        ToggleAiPromptCommand = new RelayCommand(() => IsAiPromptOpen = !IsAiPromptOpen);
        GenerateWithAiCommand = new AsyncRelayCommand(GenerateWithAiAsync, () => !IsAiGenerating);
        AddActionCommand = new RelayCommand(AddAction);
        RemoveStepCommand = new RelayCommand(p => RemoveStep(p as ScriptActionStep));
        MoveUpStepCommand = new RelayCommand(p => MoveUpStep(p as ScriptActionStep));
        MoveDownStepCommand = new RelayCommand(p => MoveDownStep(p as ScriptActionStep));
        ClearRecipeCommand = new RelayCommand(ClearRecipe);
        ApplyPresetCommand = new RelayCommand(ApplyPreset);
        SaveRecipeCommand = new AsyncRelayCommand(SaveRecipeAsync);
        LoadRecipeCommand = new AsyncRelayCommand(LoadRecipeAsync);
        ExportBatchScriptCommand = new AsyncRelayCommand(ExportBatchScriptAsync);
        RefreshPreviewCommand = new RelayCommand(SyncScriptFromRecipe);

        // Initialize with default preset (Deep Clean)
        ApplyPreset(ScriptPresetService.GetDeepCleanPreset());
    }

    public void AddAction(object? param)
    {
        ScriptActionType actionType = ScriptActionType.ClearTempCache;
        if (param is ScriptActionType sat)
        {
            actionType = sat;
        }
        else if (param is string str && Enum.TryParse<ScriptActionType>(str, true, out var parsed))
        {
            actionType = parsed;
        }

        string defaultTitle = actionType switch
        {
            ScriptActionType.ClearTempCache => "Bersihkan Cache & Temp",
            ScriptActionType.FlushDnsNetwork => "Flush DNS & Jaringan",
            ScriptActionType.LaunchExecutable => "Jalankan Executable (.exe)",
            ScriptActionType.InstallWingetSoftware => "Install Software (Winget)",
            ScriptActionType.KillProcess => "Hentikan Proses",
            ScriptActionType.SetUsbStorageLock => "Kunci/Buka Port USB",
            ScriptActionType.OpenUrlBrowser => "Buka URL di Browser",
            ScriptActionType.SetSystemVolume => "Atur Volume Audio",
            ScriptActionType.SetWallpaper => "Ganti Wallpaper Desktop",
            ScriptActionType.ShutdownRestart => "Matikan / Restart Komputer",
            ScriptActionType.LogoffUser => "Logoff Pengguna",
            ScriptActionType.CustomPowerShellBlock => "Script PowerShell Kustom",
            _ => actionType.ToString()
        };

        var step = new ScriptActionStep(actionType, defaultTitle);
        ActionSteps.Add(step);
        SyncScriptFromRecipe();
    }

    public void RemoveStep(ScriptActionStep? step)
    {
        if (step != null && ActionSteps.Remove(step))
        {
            SyncScriptFromRecipe();
        }
    }

    public void MoveUpStep(ScriptActionStep? step)
    {
        if (step == null) return;
        int index = ActionSteps.IndexOf(step);
        if (index > 0)
        {
            ActionSteps.Move(index, index - 1);
            SyncScriptFromRecipe();
        }
    }

    public void MoveDownStep(ScriptActionStep? step)
    {
        if (step == null) return;
        int index = ActionSteps.IndexOf(step);
        if (index >= 0 && index < ActionSteps.Count - 1)
        {
            ActionSteps.Move(index, index + 1);
            SyncScriptFromRecipe();
        }
    }

    public void ApplyPreset(object? param)
    {
        ScriptRecipe? preset = null;
        if (param is ScriptRecipe sr)
        {
            preset = sr;
        }
        else if (param is string str)
        {
            preset = ScriptPresetService.GetPresetById(str) ??
                     AvailablePresets.FirstOrDefault(p => string.Equals(p.Name, str, StringComparison.OrdinalIgnoreCase) ||
                                                          string.Equals(p.Id, str, StringComparison.OrdinalIgnoreCase));
        }

        if (preset == null) return;

        CurrentRecipe = new ScriptRecipe
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = preset.Name,
            Description = preset.Description,
            Author = preset.Author,
            CreatedAt = DateTime.UtcNow
        };

        ActionSteps.Clear();
        foreach (var step in preset.Steps)
        {
            var newStep = new ScriptActionStep(step.ActionType, step.Title)
            {
                Description = step.Description,
                IsEnabled = step.IsEnabled
            };
            foreach (var kvp in step.Parameters)
            {
                newStep.Parameters[kvp.Key] = kvp.Value;
            }
            ActionSteps.Add(newStep);
        }

        SyncScriptFromRecipe();
        StatusText = $"Preset '{preset.Name}' berhasil diterapkan ({ActionSteps.Count} langkah).";
    }

    public void ClearRecipe()
    {
        CurrentRecipe = new ScriptRecipe { Name = "Resep Script Baru" };
        ActionSteps.Clear();
        SyncScriptFromRecipe();
        StatusText = "Resep script telah dikosongkan.";
    }

    public void SyncScriptFromRecipe()
    {
        CurrentRecipe.Steps = ActionSteps.ToList();
        if (IsVisualMode)
        {
            SelectedScriptType = ScriptType.PowerShell;
            ScriptContent = CurrentRecipe.GeneratePowerShellScript();
        }
    }

    public async Task GenerateWithAiAsync()
    {
        if (IsAiGenerating) return;
        if (string.IsNullOrWhiteSpace(AiPromptText))
        {
            AiStatusMessage = "Ketik instruksi lab terlebih dahulu.";
            return;
        }

        var (baseUrl, apiKey, model) = await _databaseService.GetAiRouterSettingsAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            AiStatusMessage = "⚠️ API Key 9router belum dikonfigurasi. Silakan buka menu Pengaturan untuk memasukkan API Key.";
            return;
        }

        try
        {
            IsAiGenerating = true;
            AiStatusMessage = "⏳ Menghubungi 9router AI dan menyusun resep... Mohon tunggu...";

            var (success, recipe, error) = await _aiGeneratorService.GenerateRecipeAsync(
                AiPromptText, baseUrl, apiKey, model);

            if (success && recipe != null)
            {
                CurrentRecipe = recipe;
                ActionSteps.Clear();
                foreach (var step in recipe.Steps)
                {
                    ActionSteps.Add(step);
                }
                SyncScriptFromRecipe();
                AiStatusMessage = $"✅ Resep '{recipe.Name}' berhasil dibuat oleh AI ({recipe.Steps.Count} langkah)!";
                AiPromptText = string.Empty;
                IsAiPromptOpen = false;
                StatusText = $"Resep AI '{recipe.Name}' berhasil dimuat.";
            }
            else
            {
                AiStatusMessage = $"❌ Gagal membuat resep: {error}";
            }
        }
        catch (Exception ex)
        {
            AiStatusMessage = $"❌ Terjadi kesalahan: {ex.Message}";
        }
        finally
        {
            IsAiGenerating = false;
        }
    }

    public async Task SaveRecipeAsync()
    {
        SyncScriptFromRecipe();
        string? filePath = null;
        if (SaveRecipeFileDialogHandler != null)
        {
            filePath = SaveRecipeFileDialogHandler();
        }
        else
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Simpan Resep Script (.json)",
                Filter = "JSON Recipe (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = $"{SanitizeFileName(CurrentRecipe.Name)}.json"
            };
            if (sfd.ShowDialog() == true) filePath = sfd.FileName;
        }

        if (string.IsNullOrWhiteSpace(filePath)) return;

        try
        {
            string json = CurrentRecipe.ToJson();
            await File.WriteAllTextAsync(filePath, json, System.Text.Encoding.UTF8);
            StatusText = $"Resep berhasil disimpan: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Gagal menyimpan resep: {ex.Message}";
        }
    }

    public async Task LoadRecipeAsync()
    {
        string? filePath = null;
        if (OpenRecipeFileDialogHandler != null)
        {
            filePath = OpenRecipeFileDialogHandler();
        }
        else
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Buka Resep Script (.json)",
                Filter = "JSON Recipe (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json"
            };
            if (ofd.ShowDialog() == true) filePath = ofd.FileName;
        }

        if (string.IsNullOrWhiteSpace(filePath)) return;

        try
        {
            string json = await File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8);
            var loaded = ScriptRecipe.FromJson(json);
            if (loaded != null)
            {
                CurrentRecipe = loaded;
                ActionSteps.Clear();
                foreach (var step in loaded.Steps)
                {
                    ActionSteps.Add(step);
                }
                SyncScriptFromRecipe();
                StatusText = $"Resep '{loaded.Name}' berhasil dimuat ({loaded.Steps.Count} langkah).";
            }
            else
            {
                StatusText = "Gagal memuat resep: format JSON tidak valid.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Gagal membaca file resep: {ex.Message}";
        }
    }

    public async Task ExportBatchScriptAsync()
    {
        SyncScriptFromRecipe();
        string? filePath = null;
        if (ExportBatchFileDialogHandler != null)
        {
            filePath = ExportBatchFileDialogHandler();
        }
        else
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Ekspor Batch Script Mandiri (.bat)",
                Filter = "Batch Script (*.bat)|*.bat|All Files (*.*)|*.*",
                DefaultExt = ".bat",
                FileName = $"{SanitizeFileName(CurrentRecipe.Name)}.bat"
            };
            if (sfd.ShowDialog() == true) filePath = sfd.FileName;
        }

        if (string.IsNullOrWhiteSpace(filePath)) return;

        try
        {
            string batContent = CurrentRecipe.GenerateBatchWrapper();
            await File.WriteAllTextAsync(filePath, batContent, System.Text.Encoding.UTF8);
            StatusText = $"Batch script berhasil diekspor: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Gagal mengekspor batch script: {ex.Message}";
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "recipe";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
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
        if (IsVisualMode)
        {
            SyncScriptFromRecipe();
        }

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
