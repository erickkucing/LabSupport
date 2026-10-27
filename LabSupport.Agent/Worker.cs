using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LabSupport.Agent.Services;

namespace LabSupport.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private CommandListener? _commandListener;
    private MjpegStreamer? _mjpegStreamer;
    private HeartbeatService? _heartbeatService;

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
        int commandPort = int.TryParse(_configuration["Agent:CommandPort"], out int cp) ? cp : 5902;
        int mjpegPort = int.TryParse(_configuration["Agent:MjpegPort"], out int sjp) ? sjp : 5901;
        int intervalSec = int.TryParse(_configuration["Agent:HeartbeatIntervalSeconds"], out int hbs) ? hbs : 5;

        var screenCapture = new ScreenCaptureService();
        var powerManager = new PowerManager();
        var scriptExecutor = new ScriptExecutor();

        _commandListener = new CommandListener(commandPort, powerManager, scriptExecutor);
        _mjpegStreamer = new MjpegStreamer(screenCapture, mjpegPort);
        _heartbeatService = new HeartbeatService(managerIp, managerPort, intervalSec, screenCapture);

        _commandListener.Start();
        _logger.LogInformation("CommandListener started on port {Port}", commandPort);

        try
        {
            _mjpegStreamer.Start();
            _logger.LogInformation("MjpegStreamer started on port {Port}", mjpegPort);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MjpegStreamer could not start: {Message}", ex.Message);
        }

        _heartbeatService.Start();
        _logger.LogInformation("HeartbeatService started targeting Manager {Ip}:{Port}", managerIp, managerPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogInformation("LabSupport Agent stopping...");
        _heartbeatService.Stop();
        _mjpegStreamer.Stop();
        _commandListener.Stop();
    }
}
