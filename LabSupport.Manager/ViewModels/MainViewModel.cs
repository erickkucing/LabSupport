using System;
using System.Windows;
using System.Windows.Input;
using LabSupport.Manager.Models;
using LabSupport.Shared.Services;

namespace LabSupport.Manager.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private ViewModelBase _currentViewModel;
    private string _activeViewName = "Dashboard";
    private string _serverStatus = "Server Berjalan (Port 8080)";

    public DatabaseService DatabaseService { get; }
    public HttpFileServer HttpFileServer { get; }
    public AgentCommandService CommandService { get; }
    public WolService WolService { get; }

    public DashboardViewModel DashboardVM { get; }
    public ScriptViewModel ScriptVM { get; }
    public RemoteViewModel RemoteVM { get; }
    public GenerateAgentViewModel GenerateAgentVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    public string ActiveViewName
    {
        get => _activeViewName;
        set => SetProperty(ref _activeViewName, value);
    }

    public string ServerStatus
    {
        get => _serverStatus;
        set => SetProperty(ref _serverStatus, value);
    }

    public ManagerBeaconService? BeaconService { get; private set; }

    public ICommand NavigateDashboardCommand { get; }
    public ICommand NavigateScriptCommand { get; }
    public ICommand NavigateRemoteCommand { get; }
    public ICommand NavigateGenerateCommand { get; }
    public ICommand NavigateSettingsCommand { get; }

    public MainViewModel()
    {
        DatabaseService = new DatabaseService("labsupport.db");
        CommandService = new AgentCommandService(5902);
        WolService = new WolService(DatabaseService);
        HttpFileServer = new HttpFileServer(8080);

        DashboardVM = new DashboardViewModel(DatabaseService, CommandService, WolService, HttpFileServer);
        ScriptVM = new ScriptViewModel(CommandService, DatabaseService, DashboardVM.Computers);
        RemoteVM = new RemoteViewModel(CommandService, null, HttpFileServer);
        GenerateAgentVM = new GenerateAgentViewModel(DatabaseService);
        SettingsVM = new SettingsViewModel(DatabaseService);

        _currentViewModel = DashboardVM;

        NavigateDashboardCommand = new RelayCommand(() =>
        {
            CurrentViewModel = DashboardVM;
            ActiveViewName = "Dashboard";
        });

        NavigateScriptCommand = new RelayCommand(() =>
        {
            CurrentViewModel = ScriptVM;
            ActiveViewName = "Script";
        });

        NavigateRemoteCommand = new RelayCommand(() =>
        {
            CurrentViewModel = RemoteVM;
            ActiveViewName = "Remote";
        });

        NavigateGenerateCommand = new RelayCommand(() =>
        {
            CurrentViewModel = GenerateAgentVM;
            ActiveViewName = "Generate";
        });

        NavigateSettingsCommand = new RelayCommand(() =>
        {
            CurrentViewModel = SettingsVM;
            ActiveViewName = "Settings";
        });

        // Wire sub-navigation from Dashboard
        DashboardVM.OnOpenRemoteRequested += pc =>
        {
            RemoteVM.TargetComputer = pc;
            CurrentViewModel = RemoteVM;
            ActiveViewName = "Remote";
            _ = RemoteVM.ConnectAsync();
        };

        DashboardVM.OnOpenScriptRequested += pc =>
        {
            pc.IsSelected = true;
            ScriptVM.SelectedTargetMode = "Selected";
            CurrentViewModel = ScriptVM;
            ActiveViewName = "Script";
        };

        // Wire ManagerId sync when generated in GenerateAgentVM
        GenerateAgentVM.OnManagerIdChanged += newId =>
        {
            CommandService.ManagerId = newId;
            DashboardVM.CurrentManagerId = newId;
            string currentLanIp = NetworkHelper.GetPrimaryLanIPv4Address();
            BeaconService?.UpdateEndpoint(currentLanIp, HttpFileServer.Port, newId);
            ServerStatus = $"Server HTTP Aktif (Port {HttpFileServer.Port}) | Lab Code: {newId}";
        };

        // Wire Heartbeat event with off-UI background thumbnail decoding
        HttpFileServer.OnHeartbeatReceived += hb =>
        {
            System.Windows.Media.Imaging.BitmapSource? frozenThumb = null;
            if (!string.IsNullOrEmpty(hb.ScreenshotBase64))
            {
                try
                {
                    byte[] thumbBytes = Convert.FromBase64String(hb.ScreenshotBase64);
                    frozenThumb = Models.LabComputer.CreateFrozenBitmap(thumbBytes);
                }
                catch { }
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    DashboardVM.HandleHeartbeat(hb, frozenThumb);
                });
            }
        };

        // Start services
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await DatabaseService.InitializeAsync();
            string managerId = await DatabaseService.GetOrCreateManagerIdAsync();
            CommandService.ManagerId = managerId;
            DashboardVM.CurrentManagerId = managerId;
            GenerateAgentVM.ManagerId = managerId;

            await DashboardVM.LoadComputersFromDbAsync();

            HttpFileServer.Start();
            string currentLanIp = NetworkHelper.GetPrimaryLanIPv4Address();
            BeaconService = new ManagerBeaconService(managerId, currentLanIp, HttpFileServer.Port);
            BeaconService.Start();

            string bindStatus = HttpFileServer.IsBoundToWildcard ? "Wildcard (Semua IP)" : HttpFileServer.BoundPrefixes;
            ServerStatus = $"Server Aktif: {bindStatus} | Lab Code: {managerId} | UDP Beacon: Aktif";
        }
        catch (Exception ex)
        {
            ServerStatus = $"Gagal memulai server: {ex.Message}";
        }
    }

    public void Dispose()
    {
        BeaconService?.Dispose();
        HttpFileServer.Dispose();
        RemoteVM.Dispose();
        DatabaseService.Dispose();
    }
}
