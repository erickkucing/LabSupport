using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LabSupport.Manager.Models;
using LabSupport.Shared.Services;
using LabSupport.Shared.Protocol;
using RemoteMouseAction = LabSupport.Shared.Protocol.MouseAction;

namespace LabSupport.Manager.ViewModels;

public class RemoteViewModel : ViewModelBase, IDisposable
{
    private readonly AgentCommandService _commandService;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _streamCts;
    private Task? _streamLoopTask;

    private LabComputer? _targetComputer;
    private BitmapSource? _remoteScreenSource;
    private bool _isConnected;
    private bool _isControlEnabled = true;
    private string _connectionStatus = "Terputus";
    private int _refreshIntervalMs = 120; // ~8 FPS
    private string _customMessage = string.Empty;
    private string _typingText = string.Empty;

    public LabComputer? TargetComputer
    {
        get => _targetComputer;
        set
        {
            if (SetProperty(ref _targetComputer, value))
            {
                if (IsConnected)
                {
                    _ = ConnectAsync();
                }
            }
        }
    }

    public BitmapSource? RemoteScreenSource
    {
        get => _remoteScreenSource;
        private set => SetProperty(ref _remoteScreenSource, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public bool IsControlEnabled
    {
        get => _isControlEnabled;
        set => SetProperty(ref _isControlEnabled, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public int RefreshIntervalMs
    {
        get => _refreshIntervalMs;
        set => SetProperty(ref _refreshIntervalMs, Math.Clamp(value, 50, 2000));
    }

    public string CustomMessage
    {
        get => _customMessage;
        set => SetProperty(ref _customMessage, value);
    }

    public string TypingText
    {
        get => _typingText;
        set => SetProperty(ref _typingText, value);
    }

    public ICommand ToggleConnectCommand { get; }
    public ICommand SendLockCommand { get; }
    public ICommand SendRestartCommand { get; }
    public ICommand SendShutdownCommand { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand SaveScreenshotCommand { get; }
    public ICommand SendShortcutCommand { get; }
    public ICommand SendTypingTextCommand { get; }

    public RemoteViewModel(AgentCommandService commandService, HttpClient? httpClient = null)
    {
        _commandService = commandService;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        ToggleConnectCommand = new AsyncRelayCommand(ToggleConnectAsync);
        SendLockCommand = new AsyncRelayCommand(SendLockAsync);
        SendRestartCommand = new AsyncRelayCommand(SendRestartAsync);
        SendShutdownCommand = new AsyncRelayCommand(SendShutdownAsync);
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync);
        SaveScreenshotCommand = new RelayCommand(SaveScreenshot);
        SendShortcutCommand = new AsyncRelayCommand(p => SendShortcutAsync(p?.ToString() ?? ""));
        SendTypingTextCommand = new AsyncRelayCommand(SendTypingTextAsync);
    }

    private async Task ToggleConnectAsync()
    {
        if (IsConnected)
        {
            Disconnect();
        }
        else
        {
            await ConnectAsync();
        }
    }

    public async Task ConnectAsync()
    {
        Disconnect();

        if (TargetComputer == null || string.IsNullOrWhiteSpace(TargetComputer.IpAddress))
        {
            ConnectionStatus = "Pilih komputer terlebih dahulu.";
            return;
        }

        ConnectionStatus = $"Menghubungkan ke {TargetComputer.Hostname} ({TargetComputer.IpAddress})...";
        _streamCts = new CancellationTokenSource();
        IsConnected = true;

        _streamLoopTask = Task.Run(() => StreamLoopAsync(_streamCts.Token));
        await Task.CompletedTask;
    }

    private async Task StreamLoopAsync(CancellationToken token)
    {
        string url = $"http://{TargetComputer!.IpAddress}:5901/screenshot";

        while (!token.IsCancellationRequested && IsConnected)
        {
            try
            {
                byte[] jpegBytes = await _httpClient.GetByteArrayAsync(url, token);
                if (jpegBytes.Length > 0)
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher != null)
                    {
                        await dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                var img = new BitmapImage();
                                using (var ms = new MemoryStream(jpegBytes))
                                {
                                    img.BeginInit();
                                    img.CacheOption = BitmapCacheOption.OnLoad;
                                    img.StreamSource = ms;
                                    img.EndInit();
                                }
                                img.Freeze();
                                RemoteScreenSource = img;
                                ConnectionStatus = $"Terhubung ({TargetComputer.Hostname})";
                            }
                            catch { }
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        ConnectionStatus = $"Error stream: {ex.Message}";
                    });
                }
            }

            try
            {
                await Task.Delay(_refreshIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Disconnect()
    {
        _streamCts?.Cancel();
        IsConnected = false;
        ConnectionStatus = "Terputus";
    }

    public async Task SendMouseInputAsync(double normX, double normY, RemoteMouseAction action, int wheelDelta = 0)
    {
        if (!IsConnected || !IsControlEnabled || TargetComputer == null) return;

        var payload = new MouseInputPayload
        {
            Action = action,
            NormalizedX = normX,
            NormalizedY = normY,
            WheelDelta = wheelDelta
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.MouseEvent,
            Payload = MessageSerializer.Serialize(payload)
        };

        await _commandService.SendCommandAsync(TargetComputer.IpAddress, cmd, timeoutMs: 2000);
    }

    public async Task SendShortcutAsync(string shortcut)
    {
        if (TargetComputer == null || string.IsNullOrWhiteSpace(shortcut)) return;

        var payload = new KeyboardInputPayload
        {
            Action = KeyboardAction.Shortcut,
            Key = shortcut
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.KeyboardEvent,
            Payload = MessageSerializer.Serialize(payload)
        };

        await _commandService.SendCommandAsync(TargetComputer.IpAddress, cmd, timeoutMs: 3000);
    }

    public async Task SendTypingTextAsync()
    {
        if (TargetComputer == null || string.IsNullOrWhiteSpace(TypingText)) return;

        var payload = new KeyboardInputPayload
        {
            Action = KeyboardAction.Text,
            Text = TypingText
        };

        var cmd = new CommandMessage
        {
            Type = CommandType.KeyboardEvent,
            Payload = MessageSerializer.Serialize(payload)
        };

        await _commandService.SendCommandAsync(TargetComputer.IpAddress, cmd, timeoutMs: 5000);
        TypingText = string.Empty;
    }

    public async Task<bool> SendFileAsync(string localFilePath, string? targetDirectory = null)
    {
        if (TargetComputer == null || !File.Exists(localFilePath)) return false;

        try
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(localFilePath);
            var payload = new FileTransferPayload
            {
                FileName = Path.GetFileName(localFilePath),
                TargetDirectory = targetDirectory,
                ContentBase64 = Convert.ToBase64String(fileBytes)
            };

            var cmd = new CommandMessage
            {
                Type = CommandType.UploadFile,
                Payload = MessageSerializer.Serialize(payload)
            };

            var resp = await _commandService.SendCommandAsync(TargetComputer.IpAddress, cmd, timeoutMs: 30000);
            return resp.Success;
        }
        catch
        {
            return false;
        }
    }

    private async Task SendLockAsync()
    {
        if (TargetComputer == null) return;
        var cmd = new CommandMessage { Type = CommandType.LockScreen };
        await _commandService.SendCommandAsync(TargetComputer.IpAddress, cmd);
    }

    private async Task SendRestartAsync()
    {
        if (TargetComputer == null) return;
        var cmd = new CommandMessage { Type = CommandType.Restart };
        await _commandService.SendCommandAsync(TargetComputer.IpAddress, cmd);
    }

    private async Task SendShutdownAsync()
    {
        if (TargetComputer == null) return;
        var cmd = new CommandMessage { Type = CommandType.Shutdown };
        await _commandService.SendCommandAsync(TargetComputer.IpAddress, cmd);
    }

    private async Task SendMessageAsync()
    {
        if (TargetComputer == null || string.IsNullOrWhiteSpace(CustomMessage)) return;
        var cmd = new CommandMessage { Type = CommandType.ShowMessage, Payload = CustomMessage };
        await _commandService.SendCommandAsync(TargetComputer.IpAddress, cmd);
        CustomMessage = string.Empty;
    }

    private void SaveScreenshot()
    {
        if (RemoteScreenSource == null) return;

        try
        {
            string fileName = $"Screen_{TargetComputer?.Hostname ?? "Lab"}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(RemoteScreenSource));

            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
            ConnectionStatus = $"Screenshot tersimpan: {fileName}";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Gagal simpan screenshot: {ex.Message}";
        }
    }

    public void Dispose()
    {
        Disconnect();
        _streamCts?.Dispose();
    }
}
