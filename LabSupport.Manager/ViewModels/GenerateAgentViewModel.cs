using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using LabSupport.Shared.Services;

namespace LabSupport.Manager.ViewModels;

public class GenerateAgentViewModel : ViewModelBase
{
    private string _selectedManagerIp = "127.0.0.1";
    private int _managerPort = 8080;
    private string _serviceName = "LabSupportAgent";
    private string _outputDirectory = string.Empty;
    private string _ps1Preview = string.Empty;
    private string _batPreview = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isGenerating;

    public ObservableCollection<string> AvailableIpAddresses { get; } = new();

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
    public ICommand RefreshIpsCommand { get; }

    public GenerateAgentViewModel()
    {
        GenerateCommand = new AsyncRelayCommand(GeneratePackageAsync, () => !IsGenerating);
        RefreshIpsCommand = new RelayCommand(RefreshNetworkIps);

        OutputDirectory = Path.Combine(AppContext.BaseDirectory, "LabAgentPackage");

        RefreshNetworkIps();
        UpdatePreview();
    }

    public void RefreshNetworkIps()
    {
        AvailableIpAddresses.Clear();
        var ips = NetworkHelper.GetLocalIPv4Addresses();
        foreach (var ip in ips)
        {
            AvailableIpAddresses.Add(ip);
        }

        // Prefer non-loopback IP
        var primary = ips.FirstOrDefault(ip => !ip.StartsWith("127."));
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
        Ps1Preview = ScriptGenerator.GeneratePowerShellScript(SelectedManagerIp, ManagerPort, ServiceName);
        BatPreview = ScriptGenerator.GenerateBatchScript();
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
                ServiceName,
                agentSourcePath);

            StatusMessage = $"SUKSES! File berhasil dibuat di:\n{OutputDirectory}\n(Termasuk install_agent.bat dan install_agent.ps1)";
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
