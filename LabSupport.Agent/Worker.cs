using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LabSupport.Agent.Services;
using LabSupport.Shared.Services;

namespace LabSupport.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private CommandListener? _commandListener;
    private MjpegStreamer? _mjpegStreamer;
    private HeartbeatService? _heartbeatService;
    private AgentDiscoveryClient? _discoveryClient;

    public Worker(ILogger<Worker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LabSupport Agent starting...");

        string managerIp = _configuration["Manager:Ip"] ?? "127.0.0.1";
        int managerPort = int.TryParse(_configuration["Manager:Port"], out int mp) ? mp : 8080;
        string managerId = _configuration["Manager:ManagerId"] ?? "LAB-DEFAULT";
        string roomName = _configuration["Manager:RoomName"] ?? "General";
        string? displayName = _configuration["Agent:DisplayName"];
        int commandPort = int.TryParse(_configuration["Agent:CommandPort"], out int cp) ? cp : 5902;
        int mjpegPort = int.TryParse(_configuration["Agent:MjpegPort"], out int sjp) ? sjp : 5901;
        int intervalSec = int.TryParse(_configuration["Agent:HeartbeatIntervalSeconds"], out int hbs) ? hbs : 5;

        var screenCapture = new ScreenCaptureService();
        var powerManager = new PowerManager();
        var scriptExecutor = new ScriptExecutor();

        _commandListener = new CommandListener(commandPort, managerId, powerManager, scriptExecutor);
        _mjpegStreamer = new MjpegStreamer(screenCapture, mjpegPort);
        _heartbeatService = new HeartbeatService(managerIp, managerPort, intervalSec, managerId, screenCapture, null, roomName, displayName);
        _discoveryClient = new AgentDiscoveryClient(managerId, managerIp, managerPort);

        _discoveryClient.OnManagerEndpointDiscovered += (newIp, newPort) =>
        {
            _logger.LogInformation("Discovered Manager via UDP at {Ip}:{Port}", newIp, newPort);
            _heartbeatService.UpdateManagerEndpoint(newIp, newPort);
        };

        _commandListener.OnUpdateManagerEndpointRequested += (newIp, newPort) =>
        {
            _logger.LogInformation("Manager endpoint updated via command to {Ip}:{Port}", newIp, newPort);
            _discoveryClient.SaveEndpointToAppSettings(newIp, newPort);
            _heartbeatService.UpdateManagerEndpoint(newIp, newPort);
        };

        _heartbeatService.OnConnectionLost += () =>
        {
            _logger.LogWarning("Lost connection to Manager ({Ip}:{Port}). Broadcasting discovery request...", _heartbeatService.ManagerIp, _heartbeatService.ManagerPort);
            _ = _discoveryClient.SendDiscoveryRequestAsync();
        };

        _commandListener.OnUninstallRequested += () =>
        {
            _logger.LogWarning("Uninstall requested via CommandListener. Initiating self-uninstall...");
            AgentUninstallerService.TriggerSelfUninstall(1500, _heartbeatService.ManagerIp, _heartbeatService.ManagerPort, managerId);
        };

        _heartbeatService.OnUninstallDirectiveReceived += () =>
        {
            _logger.LogWarning("Uninstall directive received from Manager heartbeat. Initiating self-uninstall...");
            AgentUninstallerService.TriggerSelfUninstall(1000, _heartbeatService.ManagerIp, _heartbeatService.ManagerPort, managerId);
        };

        _commandListener.Start();
        _logger.LogInformation("CommandListener started on port {Port} (Pairing Code: {ManagerId})", commandPort, managerId);

        try
        {
            _mjpegStreamer.Start();
            _logger.LogInformation("MjpegStreamer started on port {Port}", mjpegPort);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MjpegStreamer could not start: {Message}", ex.Message);
        }

        _discoveryClient.Start();
        _logger.LogInformation("AgentDiscoveryClient started on UDP port 5903 (Pairing Code: {ManagerId})", managerId);

        _heartbeatService.Start();
        _logger.LogInformation("HeartbeatService started targeting Manager {Ip}:{Port}", managerIp, managerPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogInformation("LabSupport Agent stopping...");
        _heartbeatService.Stop();
        _discoveryClient.Stop();
        _discoveryClient.Dispose();
        _mjpegStreamer.Stop();
        _commandListener.Stop();
        _commandListener.Dispose();
    }
}
