using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Agent.Services;

public class CommandListener : IDisposable
{
    private readonly int _port;
    private readonly string _expectedManagerId;
    private readonly PowerManager _powerManager;
    private readonly ScriptExecutor _scriptExecutor;
    private readonly InputSimulator _inputSimulator;
    private readonly ScreenLockService _screenLockService;
    private readonly AudioService _audioService;
    private readonly ExamLockdownService _examLockdownService;
    private readonly UsbBlockerService _usbBlockerService;
    private readonly FirewallBlockerService _firewallBlockerService;
    private readonly ProcessGuardService _processGuardService;
    private readonly ClipboardInjectorService _clipboardInjectorService;
    private readonly WorkspaceCleanerService _workspaceCleanerService;
    private readonly NotificationOverlayService _notificationService;
    private readonly HttpClient _httpClient;
    private Process? _broadcastProcess;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public bool IsRunning => _listener != null;
    public string ExpectedManagerId => _expectedManagerId;
    public ProcessGuardService ProcessGuard => _processGuardService;
    public ClipboardInjectorService ClipboardInjector => _clipboardInjectorService;
    public WorkspaceCleanerService WorkspaceCleaner => _workspaceCleanerService;
    public event Action<string, int>? OnUpdateManagerEndpointRequested;
    public event Action? OnUninstallRequested;

    public CommandListener(
        int port = 5902,
        string expectedManagerId = "",
        PowerManager? powerManager = null,
        ScriptExecutor? scriptExecutor = null,
        InputSimulator? inputSimulator = null,
        ScreenLockService? screenLockService = null,
        AudioService? audioService = null,
        ExamLockdownService? examLockdownService = null,
        UsbBlockerService? usbBlockerService = null,
        FirewallBlockerService? firewallBlockerService = null,
        ProcessGuardService? processGuardService = null,
        ClipboardInjectorService? clipboardInjectorService = null,
        WorkspaceCleanerService? workspaceCleanerService = null,
        NotificationOverlayService? notificationService = null,
        HttpClient? httpClient = null)
    {
        _port = port;
        _expectedManagerId = expectedManagerId;
        _powerManager = powerManager ?? new PowerManager();
        _scriptExecutor = scriptExecutor ?? new ScriptExecutor();
        _inputSimulator = inputSimulator ?? new InputSimulator();
        _screenLockService = screenLockService ?? new ScreenLockService();
        _audioService = audioService ?? new AudioService();
        _examLockdownService = examLockdownService ?? new ExamLockdownService();
        _usbBlockerService = usbBlockerService ?? new UsbBlockerService();
        _firewallBlockerService = firewallBlockerService ?? new FirewallBlockerService();
        _processGuardService = processGuardService ?? new ProcessGuardService();
        _clipboardInjectorService = clipboardInjectorService ?? new ClipboardInjectorService();
        _workspaceCleanerService = workspaceCleanerService ?? new WorkspaceCleanerService();
        _notificationService = notificationService ?? new NotificationOverlayService();
        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _listenTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
        {
            try
            {
                string? line = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(line)) return;

                var command = MessageSerializer.Deserialize<CommandMessage>(line);
                if (command == null) return;

                var response = await ProcessCommandAsync(command, token);
                string responseJson = MessageSerializer.Serialize(response);
                await writer.WriteLineAsync(responseJson);
            }
            catch (Exception ex)
            {
                try
                {
                    var errResponse = new CommandResponse
                    {
                        Success = false,
                        Error = ex.Message
                    };
                    await writer.WriteLineAsync(MessageSerializer.Serialize(errResponse));
                }
                catch { }
            }
        }
    }

    public async Task<CommandResponse> ProcessCommandAsync(CommandMessage command, CancellationToken token = default)
    {
        var sw = Stopwatch.StartNew();

        if (!string.IsNullOrWhiteSpace(_expectedManagerId))
        {
            if (string.IsNullOrWhiteSpace(command.ManagerId) ||
                !string.Equals(command.ManagerId, _expectedManagerId, StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = false,
                    Error = "Unauthorized: Invalid or missing Manager pairing code.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
            }
        }

        switch (command.Type)
        {
            case CommandType.Ping:
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = "Pong",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.Shutdown:
                bool shutdownSuccess = _powerManager.Shutdown();
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = shutdownSuccess,
                    Output = shutdownSuccess ? "Shutdown initiated." : "Shutdown failed.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.Restart:
                bool restartSuccess = _powerManager.Restart();
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = restartSuccess,
                    Output = restartSuccess ? "Restart initiated." : "Restart failed.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.LockScreen:
                bool lockSuccess = _powerManager.LockScreen();
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = lockSuccess,
                    Output = lockSuccess ? "Screen locked." : "Failed to lock screen.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.Sleep:
                bool sleepSuccess = _powerManager.Sleep();
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = sleepSuccess,
                    Output = sleepSuccess ? "Sleep (standby) initiated." : "Failed to put system to sleep.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.ShowNotification:
                NotificationPayload? notifyPayload = null;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        notifyPayload = MessageSerializer.Deserialize<NotificationPayload>(command.Payload);
                    }
                    catch { }
                }

                notifyPayload ??= new NotificationPayload();
                if (string.IsNullOrWhiteSpace(notifyPayload.Hostname))
                {
                    notifyPayload.Hostname = Environment.MachineName;
                }

                _notificationService.ShowNotification(notifyPayload);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = $"Notification displayed ({notifyPayload.Type}) for {notifyPayload.Hostname}.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.ShowMessage:
                string msg = command.Payload ?? string.Empty;
                var legacyPayload = new NotificationPayload
                {
                    Title = "Pesan Guru",
                    Message = msg,
                    Type = NotificationType.Info,
                    DurationSeconds = 5,
                    ShowHostInfo = false
                };
                _notificationService.ShowNotification(legacyPayload);

                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = $"Message broadcasted: {msg}",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.ExecuteScript:
                if (string.IsNullOrWhiteSpace(command.Payload))
                {
                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = false,
                        Error = "Empty script payload.",
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }

                var scriptPayload = MessageSerializer.Deserialize<ScriptPayload>(command.Payload);
                if (scriptPayload == null)
                {
                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = false,
                        Error = "Failed to deserialize ScriptPayload.",
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }

                var execResult = await _scriptExecutor.ExecuteAsync(scriptPayload, token);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = execResult.Success,
                    Output = execResult.Output,
                    Error = execResult.Error,
                    ExecutionTimeMs = execResult.ExecutionTimeMs
                };

            case CommandType.KillProcess:
                string target = command.Payload ?? string.Empty;
                bool killed = false;
                string killMsg = "";
                int currentPid = Environment.ProcessId;
                string currentProcName = Process.GetCurrentProcess().ProcessName;

                if (int.TryParse(target, out int pid))
                {
                    if (pid == currentPid)
                    {
                        sw.Stop();
                        return new CommandResponse
                        {
                            CommandId = command.Id,
                            Success = false,
                            Error = "Cannot terminate the LabSupport Agent host process.",
                            ExecutionTimeMs = sw.ElapsedMilliseconds
                        };
                    }

                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        if (string.Equals(proc.ProcessName, currentProcName, StringComparison.OrdinalIgnoreCase))
                        {
                            sw.Stop();
                            return new CommandResponse
                            {
                                CommandId = command.Id,
                                Success = false,
                                Error = "Cannot terminate the LabSupport Agent host process.",
                                ExecutionTimeMs = sw.ElapsedMilliseconds
                            };
                        }

                        proc.Kill(true);
                        killed = true;
                        killMsg = $"Process {pid} ({proc.ProcessName}) killed.";
                    }
                    catch (Exception ex)
                    {
                        killMsg = $"Failed to kill PID {pid}: {ex.Message}";
                    }
                }
                else if (!string.IsNullOrWhiteSpace(target))
                {
                    if (string.Equals(target, currentProcName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(target, "LabSupport.Agent", StringComparison.OrdinalIgnoreCase))
                    {
                        sw.Stop();
                        return new CommandResponse
                        {
                            CommandId = command.Id,
                            Success = false,
                            Error = "Cannot terminate the LabSupport Agent host process.",
                            ExecutionTimeMs = sw.ElapsedMilliseconds
                        };
                    }

                    var procs = Process.GetProcessesByName(target);
                    int count = 0;
                    foreach (var p in procs)
                    {
                        if (p.Id == currentPid) continue;
                        try { p.Kill(true); count++; } catch { }
                    }
                    killed = count > 0;
                    killMsg = $"Killed {count} process(es) matching '{target}'.";
                }

                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = killed,
                    Output = killMsg,
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.GetProcessList:
                var items = new List<ProcessInfoItem>();
                try
                {
                    var processes = Process.GetProcesses()
                        .OrderByDescending(p => {
                            try { return p.WorkingSet64; } catch { return 0L; }
                        })
                        .Take(50);

                    foreach (var p in processes)
                    {
                        try
                        {
                            items.Add(new ProcessInfoItem
                            {
                                Pid = p.Id,
                                ProcessName = p.ProcessName,
                                MemoryMb = Math.Round((double)p.WorkingSet64 / (1024 * 1024), 1)
                            });
                        }
                        catch { }
                    }
                }
                catch { }

                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = MessageSerializer.Serialize(items),
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.MouseEvent:
                if (string.IsNullOrWhiteSpace(command.Payload))
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = "Empty mouse payload." };
                }

                var mousePayload = MessageSerializer.Deserialize<MouseInputPayload>(command.Payload);
                bool mouseOk = mousePayload != null && _inputSimulator.SimulateMouse(mousePayload);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = mouseOk,
                    Output = mouseOk ? "Mouse simulated." : "Failed to simulate mouse.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.KeyboardEvent:
                if (string.IsNullOrWhiteSpace(command.Payload))
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = "Empty keyboard payload." };
                }

                var keyPayload = MessageSerializer.Deserialize<KeyboardInputPayload>(command.Payload);
                bool keyOk = keyPayload != null && _inputSimulator.SimulateKeyboard(keyPayload);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = keyOk,
                    Output = keyOk ? "Keyboard simulated." : "Failed to simulate keyboard.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.StartBroadcast:
                string streamUrl = command.Payload ?? string.Empty;
                if (string.IsNullOrWhiteSpace(streamUrl) ||
                    !Uri.TryCreate(streamUrl, UriKind.Absolute, out var parsedUri) ||
                    (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps) ||
                    streamUrl.IndexOfAny(new[] { '"', '\'', '&', '|', ';', '<', '>', '^', '%', '\r', '\n' }) >= 0)
                {
                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = false,
                        Error = "Invalid or insecure broadcast URL.",
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }

                try
                {
                    if (_broadcastProcess != null && !_broadcastProcess.HasExited)
                    {
                        try { _broadcastProcess.Kill(true); } catch { }
                    }

                    if (OperatingSystem.IsWindows())
                    {
                        try
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "msedge.exe",
                                Arguments = $"--kiosk \"{streamUrl}\" --edge-kiosk-type=fullscreen --no-first-run",
                                UseShellExecute = true
                            };
                            _broadcastProcess = Process.Start(psi);
                        }
                        catch
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c start \"\" \"{streamUrl}\"",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            _broadcastProcess = Process.Start(psi);
                        }
                    }
                }
                catch { }

                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = "Broadcast presentation started.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.StopBroadcast:
                try
                {
                    if (_broadcastProcess != null && !_broadcastProcess.HasExited)
                    {
                        _broadcastProcess.Kill(true);
                    }
                    _broadcastProcess = null;
                }
                catch { }

                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = "Broadcast presentation stopped.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.UploadFile:
                if (string.IsNullOrWhiteSpace(command.Payload))
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = "Empty file upload payload." };
                }

                var uploadPayload = MessageSerializer.Deserialize<FileTransferPayload>(command.Payload);
                if (uploadPayload == null || string.IsNullOrWhiteSpace(uploadPayload.FileName))
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = "Invalid upload payload." };
                }

                string rawFileName = uploadPayload.FileName;
                string safeFileName = Path.GetFileName(rawFileName);
                if (string.IsNullOrWhiteSpace(safeFileName) ||
                    rawFileName.Contains("..") ||
                    rawFileName.Contains('/') ||
                    rawFileName.Contains('\\') ||
                    safeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = "Invalid or insecure file name." };
                }

                string targetDir = uploadPayload.TargetDirectory ?? string.Empty;
                if (targetDir.Contains("..") || targetDir.Contains('%'))
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = "Path traversal detected in upload directory." };
                }

                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!string.IsNullOrWhiteSpace(targetDir) && !targetDir.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
                {
                    if (targetDir.Equals("Documents", StringComparison.OrdinalIgnoreCase))
                    {
                        baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    }
                    else if (targetDir.Equals("Downloads", StringComparison.OrdinalIgnoreCase))
                    {
                        baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    }
                    else if (Path.IsPathRooted(targetDir))
                    {
                        string userProf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        string tempDir = Path.GetTempPath();
                        string fullTarget = Path.GetFullPath(targetDir);
                        if (fullTarget.StartsWith(userProf, StringComparison.OrdinalIgnoreCase) ||
                            fullTarget.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) ||
                            fullTarget.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase) ||
                            !OperatingSystem.IsWindows())
                        {
                            baseDir = fullTarget;
                        }
                    }
                    else
                    {
                        baseDir = Path.GetFullPath(Path.Combine(baseDir, targetDir));
                    }
                }

                try
                {
                    string fullTargetDir = Path.GetFullPath(baseDir);
                    if (!Directory.Exists(fullTargetDir)) Directory.CreateDirectory(fullTargetDir);

                    string destPath = Path.GetFullPath(Path.Combine(fullTargetDir, safeFileName));
                    if (!destPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                    {
                        sw.Stop();
                        return new CommandResponse { CommandId = command.Id, Success = false, Error = "Path traversal detected in upload destination." };
                    }

                    byte[] fileBytes = Convert.FromBase64String(uploadPayload.ContentBase64);
                    await File.WriteAllBytesAsync(destPath, fileBytes, token);

                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = true,
                        Output = $"File saved: {destPath} ({fileBytes.Length} bytes)",
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = $"Save failed: {ex.Message}", ExecutionTimeMs = sw.ElapsedMilliseconds };
                }

            case CommandType.DownloadFile:
                string filePath = command.Payload ?? string.Empty;
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = "Empty file path." };
                }

                string fullDownloadPath;
                try
                {
                    fullDownloadPath = Path.GetFullPath(filePath);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = $"Invalid path: {ex.Message}" };
                }

                if (!File.Exists(fullDownloadPath))
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = $"File not found: {filePath}" };
                }

                try
                {
                    byte[] bytes = await File.ReadAllBytesAsync(fullDownloadPath, token);
                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = true,
                        Output = Convert.ToBase64String(bytes),
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = ex.Message };
                }

            case CommandType.GetFileList:
                string browseDir = command.Payload ?? string.Empty;
                if (string.IsNullOrWhiteSpace(browseDir) || !Directory.Exists(browseDir))
                {
                    browseDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }

                var fileItems = new List<FileItemInfo>();
                try
                {
                    var di = new DirectoryInfo(browseDir);
                    foreach (var d in di.GetDirectories().Take(50))
                    {
                        fileItems.Add(new FileItemInfo
                        {
                            Name = d.Name,
                            FullPath = d.FullName,
                            IsDirectory = true,
                            LastModified = d.LastWriteTimeUtc
                        });
                    }

                    foreach (var f in di.GetFiles().Take(100))
                    {
                        fileItems.Add(new FileItemInfo
                        {
                            Name = f.Name,
                            FullPath = f.FullName,
                            IsDirectory = false,
                            SizeBytes = f.Length,
                            LastModified = f.LastWriteTimeUtc
                        });
                    }
                }
                catch { }

                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = MessageSerializer.Serialize(fileItems),
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.LockAttention:
                string attentionMsg = "PERHATIKAN GURU";
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var ap = MessageSerializer.Deserialize<AttentionPayload>(command.Payload);
                        if (ap != null && !string.IsNullOrWhiteSpace(ap.Message)) attentionMsg = ap.Message;
                        else attentionMsg = command.Payload;
                    }
                    catch
                    {
                        attentionMsg = command.Payload;
                    }
                }
                bool lockAttSuccess = _screenLockService.Lock(attentionMsg);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = lockAttSuccess,
                    Output = lockAttSuccess ? $"Screen & input locked: {attentionMsg}" : "Failed to lock screen & input.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.UnlockAttention:
                bool unlockAttSuccess = _screenLockService.Unlock();
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = unlockAttSuccess,
                    Output = unlockAttSuccess ? "Screen & input unlocked." : "Failed to unlock screen & input.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.SetBlackScreen:
                bool enableBlack = true;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var bsp = MessageSerializer.Deserialize<BlackScreenPayload>(command.Payload);
                        if (bsp != null) enableBlack = bsp.Enable;
                        else if (bool.TryParse(command.Payload, out bool b)) enableBlack = b;
                    }
                    catch
                    {
                        if (bool.TryParse(command.Payload, out bool b)) enableBlack = b;
                    }
                }
                bool blackSuccess = _screenLockService.SetBlackScreen(enableBlack);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = blackSuccess,
                    Output = blackSuccess
                        ? (enableBlack ? "Black screen enabled." : "Black screen disabled.")
                        : "Failed to toggle black screen.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.SetAudioMute:
                bool muteVal = true;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var ap = MessageSerializer.Deserialize<AudioPayload>(command.Payload);
                        if (ap != null && ap.Mute.HasValue) muteVal = ap.Mute.Value;
                        else if (bool.TryParse(command.Payload, out bool parsedMute)) muteVal = parsedMute;
                    }
                    catch
                    {
                        if (bool.TryParse(command.Payload, out bool parsedMute)) muteVal = parsedMute;
                    }
                }
                bool muteSuccess = _audioService.SetMute(muteVal);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = muteSuccess,
                    Output = muteSuccess ? $"Audio mute set to {muteVal}." : "Failed to set audio mute.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.SetAudioVolume:
                float volVal = 50.0f;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var ap = MessageSerializer.Deserialize<AudioPayload>(command.Payload);
                        if (ap != null && ap.Volume.HasValue) volVal = ap.Volume.Value;
                        else if (float.TryParse(command.Payload, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedVol) || float.TryParse(command.Payload, out parsedVol)) volVal = parsedVol;
                    }
                    catch
                    {
                        if (float.TryParse(command.Payload, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedVol) || float.TryParse(command.Payload, out parsedVol)) volVal = parsedVol;
                    }
                }
                bool volSuccess = _audioService.SetMasterVolume(volVal);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = volSuccess,
                    Output = volSuccess ? $"Audio volume set to {volVal}%." : "Failed to set audio volume.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.GetAudioState:
                var audioState = _audioService.GetAudioState();
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = MessageSerializer.Serialize(audioState),
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.SetExamMode:
                bool examEnable = true;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var ep = MessageSerializer.Deserialize<ExamModePayload>(command.Payload);
                        if (ep != null) examEnable = ep.Enabled;
                        else if (bool.TryParse(command.Payload, out bool b)) examEnable = b;
                    }
                    catch
                    {
                        if (bool.TryParse(command.Payload, out bool b)) examEnable = b;
                    }
                }
                bool examSuccess = examEnable ? _examLockdownService.EnableExamMode() : _examLockdownService.DisableExamMode();
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = examSuccess,
                    Output = examSuccess ? $"Exam mode {(examEnable ? "enabled" : "disabled")}." : "Failed to toggle exam mode.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.SetUsbBlock:
                bool usbBlock = true;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var tp = MessageSerializer.Deserialize<ToggleBlockPayload>(command.Payload);
                        if (tp != null) usbBlock = tp.Blocked;
                        else if (bool.TryParse(command.Payload, out bool b)) usbBlock = b;
                    }
                    catch
                    {
                        if (bool.TryParse(command.Payload, out bool b)) usbBlock = b;
                    }
                }
                bool usbSuccess = _usbBlockerService.SetUsbStorageBlocked(usbBlock);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = usbSuccess,
                    Output = usbSuccess ? $"USB storage {(usbBlock ? "blocked" : "unblocked")}." : "Failed to toggle USB storage block.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.SetInternetBlock:
                bool netBlock = true;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var tp = MessageSerializer.Deserialize<ToggleBlockPayload>(command.Payload);
                        if (tp != null) netBlock = tp.Blocked;
                        else if (bool.TryParse(command.Payload, out bool b)) netBlock = b;
                    }
                    catch
                    {
                        if (bool.TryParse(command.Payload, out bool b)) netBlock = b;
                    }
                }
                bool netSuccess = _firewallBlockerService.SetInternetBlocked(netBlock);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = netSuccess,
                    Output = netSuccess ? $"Selective internet {(netBlock ? "blocked" : "unblocked")}." : "Failed to toggle internet block.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.LaunchProcess:
                if (string.IsNullOrWhiteSpace(command.Payload))
                {
                    sw.Stop();
                    return new CommandResponse { CommandId = command.Id, Success = false, Error = "Empty launch payload." };
                }

                string targetToRun = command.Payload;
                string? runArgs = null;
                try
                {
                    var lp = MessageSerializer.Deserialize<LaunchPayload>(command.Payload);
                    if (lp != null && !string.IsNullOrWhiteSpace(lp.Target))
                    {
                        targetToRun = lp.Target;
                        runArgs = lp.Arguments;
                    }
                }
                catch { }

                try
                {
                    char[] forbiddenShellChars = { '&', '|', ';', '<', '>', '^', '%', '\r', '\n' };
                    if (targetToRun.IndexOfAny(forbiddenShellChars) >= 0 ||
                        (runArgs != null && runArgs.IndexOfAny(forbiddenShellChars) >= 0))
                    {
                        sw.Stop();
                        return new CommandResponse
                        {
                            CommandId = command.Id,
                            Success = false,
                            Error = "Prohibited characters detected in launch target or arguments.",
                            ExecutionTimeMs = sw.ElapsedMilliseconds
                        };
                    }

                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = targetToRun,
                            Arguments = runArgs ?? string.Empty,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = string.IsNullOrWhiteSpace(runArgs) ? $"/c start \"\" \"{targetToRun}\"" : $"/c start \"\" \"{targetToRun}\" {runArgs}",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            Process.Start(psi);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = true,
                        Output = $"Process '{targetToRun}' started successfully.",
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = false,
                        Error = $"Failed to start process: {ex.Message}",
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }

            case CommandType.CollectFiles:
                string collectPath = "";
                string searchPattern = "*.*";
                int maxFiles = 50;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var collectPayload = MessageSerializer.Deserialize<CollectFilesPayload>(command.Payload);
                        if (collectPayload != null)
                        {
                            collectPath = collectPayload.SourcePath;
                            if (!string.IsNullOrWhiteSpace(collectPayload.SearchPattern))
                            {
                                string sp = collectPayload.SearchPattern.Trim();
                                if (!sp.Contains("..") && !sp.Contains('/') && !sp.Contains('\\'))
                                {
                                    searchPattern = sp;
                                }
                            }
                            if (collectPayload.MaxFiles > 0) maxFiles = Math.Clamp(collectPayload.MaxFiles, 1, 500);
                        }
                    }
                    catch
                    {
                        collectPath = command.Payload;
                    }
                }

                if (string.IsNullOrWhiteSpace(collectPath) || collectPath.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
                {
                    collectPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }
                else
                {
                    try
                    {
                        collectPath = Path.GetFullPath(collectPath);
                    }
                    catch
                    {
                        collectPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    }
                }

                var collectResponse = new CollectFilesResponse
                {
                    Hostname = Environment.MachineName
                };

                if (Directory.Exists(collectPath))
                {
                    var di = new DirectoryInfo(collectPath);
                    var files = di.GetFiles(searchPattern, SearchOption.TopDirectoryOnly).Take(maxFiles);
                    foreach (var fi in files)
                    {
                        try
                        {
                            using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            using var ms = new MemoryStream();
                            await fs.CopyToAsync(ms, token);
                            byte[] bytes = ms.ToArray();
                            collectResponse.Files.Add(new CollectedFileItem
                            {
                                RelativePath = fi.Name,
                                SizeBytes = fi.Length,
                                ContentBase64 = Convert.ToBase64String(bytes)
                            });
                        }
                        catch { }
                    }
                }
                else if (File.Exists(collectPath))
                {
                    try
                    {
                        var fi = new FileInfo(collectPath);
                        using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var ms = new MemoryStream();
                        await fs.CopyToAsync(ms, token);
                        byte[] bytes = ms.ToArray();
                        collectResponse.Files.Add(new CollectedFileItem
                        {
                            RelativePath = fi.Name,
                            SizeBytes = fi.Length,
                            ContentBase64 = Convert.ToBase64String(bytes)
                        });
                    }
                    catch { }
                }
                else
                {
                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = false,
                        Error = $"Path not found: {collectPath}",
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }

                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = MessageSerializer.Serialize(collectResponse),
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.SetClipboardText:
                string textToCopy = string.Empty;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var clipPayload = MessageSerializer.Deserialize<ClipboardPayload>(command.Payload);
                        if (clipPayload != null) textToCopy = clipPayload.Text;
                        else textToCopy = command.Payload;
                    }
                    catch
                    {
                        textToCopy = command.Payload;
                    }
                }

                bool clipSuccess = _clipboardInjectorService.SetClipboardText(textToCopy);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = clipSuccess,
                    Output = clipSuccess ? "Clipboard set successfully." : "Failed to set clipboard.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.SetProcessGuard:
                bool guardEnabled = false;
                List<string> blacklistedProcs = new();

                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var guardPayload = MessageSerializer.Deserialize<ProcessGuardPayload>(command.Payload);
                        if (guardPayload != null)
                        {
                            guardEnabled = guardPayload.Enabled;
                            if (guardPayload.BlacklistedProcesses != null)
                            {
                                blacklistedProcs.AddRange(guardPayload.BlacklistedProcesses);
                            }
                        }
                    }
                    catch { }
                }

                _processGuardService.Configure(guardEnabled, blacklistedProcs);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = $"Process guard {(guardEnabled ? "enabled" : "disabled")} with {blacklistedProcs.Count} rules.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.CleanWorkspace:
                CleanWorkspacePayload? cleanPayload = null;
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        cleanPayload = MessageSerializer.Deserialize<CleanWorkspacePayload>(command.Payload);
                    }
                    catch { }
                }

                var cleanResult = await _workspaceCleanerService.CleanWorkspaceAsync(cleanPayload, token);
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = cleanResult.Success,
                    Output = MessageSerializer.Serialize(cleanResult),
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.UpdateManagerEndpoint:
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var payload = MessageSerializer.Deserialize<UpdateManagerEndpointPayload>(command.Payload);
                        if (payload != null && !string.IsNullOrWhiteSpace(payload.ManagerIp))
                        {
                            OnUpdateManagerEndpointRequested?.Invoke(payload.ManagerIp, payload.ManagerPort);
                            sw.Stop();
                            return new CommandResponse
                            {
                                CommandId = command.Id,
                                Success = true,
                                Output = $"Manager endpoint updated to {payload.ManagerIp}:{payload.ManagerPort}",
                                ExecutionTimeMs = sw.ElapsedMilliseconds
                            };
                        }
                    }
                    catch { }
                }
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = false,
                    Error = "Invalid UpdateManagerEndpoint payload",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.SetWebFilter:
                if (!string.IsNullOrWhiteSpace(command.Payload))
                {
                    try
                    {
                        var filterPayload = MessageSerializer.Deserialize<WebFilterPayload>(command.Payload);
                        if (filterPayload != null)
                        {
                            var webFilterService = new WebFilterService();
                            bool ok = webFilterService.ApplyFilter(filterPayload);
                            sw.Stop();
                            return new CommandResponse
                            {
                                CommandId = command.Id,
                                Success = ok,
                                Output = ok
                                    ? (filterPayload.Mode == WebFilterMode.Disabled ? "Web filter disabled." : $"Web filter applied: {filterPayload.Mode} with {filterPayload.Domains.Count} domains.")
                                    : "Failed to apply web filter (hosts file error).",
                                ExecutionTimeMs = sw.ElapsedMilliseconds
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        return new CommandResponse
                        {
                            CommandId = command.Id,
                            Success = false,
                            Error = $"WebFilter error: {ex.Message}",
                            ExecutionTimeMs = sw.ElapsedMilliseconds
                        };
                    }
                }
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = false,
                    Error = "Invalid SetWebFilter payload",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            case CommandType.DownloadAndDeployFile:
                return await HandleDownloadAndDeployFileAsync(command, sw, token);

            case CommandType.UninstallAgent:
                OnUninstallRequested?.Invoke();
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = true,
                    Output = "Agent uninstall initiated.",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };

            default:
                sw.Stop();
                return new CommandResponse
                {
                    CommandId = command.Id,
                    Success = false,
                    Error = $"Unsupported command type: {command.Type}",
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
        }
    }

    private async Task<CommandResponse> HandleDownloadAndDeployFileAsync(CommandMessage command, Stopwatch sw, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(command.Payload))
        {
            sw.Stop();
            return new CommandResponse { CommandId = command.Id, Success = false, Error = "Empty download payload.", ExecutionTimeMs = sw.ElapsedMilliseconds };
        }

        var payload = MessageSerializer.Deserialize<FileDistributionPayload>(command.Payload);
        if (payload == null || string.IsNullOrWhiteSpace(payload.DownloadUrl) || string.IsNullOrWhiteSpace(payload.ItemName))
        {
            sw.Stop();
            return new CommandResponse { CommandId = command.Id, Success = false, Error = "Invalid FileDistributionPayload.", ExecutionTimeMs = sw.ElapsedMilliseconds };
        }

        // Defensive path sanitization on ItemName
        string rawItemName = payload.ItemName.Trim();
        string safeItemName = Path.GetFileName(rawItemName);
        if (string.IsNullOrWhiteSpace(safeItemName) ||
            rawItemName.Contains("..") ||
            rawItemName.Contains('/') ||
            rawItemName.Contains('\\') ||
            safeItemName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            sw.Stop();
            return new CommandResponse { CommandId = command.Id, Success = false, Error = "Insecure or invalid item name.", ExecutionTimeMs = sw.ElapsedMilliseconds };
        }

        // Resolve base directory
        string targetBaseDir;
        try
        {
            targetBaseDir = FileDistributionService.ResolveDestinationDirectory(payload.TargetDirectory, payload.CustomSubfolder);
            if (!Directory.Exists(targetBaseDir))
            {
                Directory.CreateDirectory(targetBaseDir);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CommandResponse { CommandId = command.Id, Success = false, Error = $"Target directory error: {ex.Message}", ExecutionTimeMs = sw.ElapsedMilliseconds };
        }

        string finalDestinationPath = Path.Combine(targetBaseDir, safeItemName);
        bool itemAlreadyExists = File.Exists(finalDestinationPath) || Directory.Exists(finalDestinationPath);

        // Pre-check collision policy: if overwrite is disabled and item exists, abort immediately to prevent bandwidth waste
        if (!payload.Overwrite && itemAlreadyExists)
        {
            sw.Stop();
            string itemType = payload.IsDirectory ? "directory" : "file";
            return new CommandResponse
            {
                CommandId = command.Id,
                Success = false,
                Error = $"Destination {itemType} already exists and overwrite is disabled: {finalDestinationPath}",
                ExecutionTimeMs = sw.ElapsedMilliseconds
            };
        }

        // Stream download directly from HTTP disk-to-disk
        string tempFilePath = Path.Combine(Path.GetTempPath(), $"ls_dl_{Guid.NewGuid():N}.tmp");
        try
        {
            using (var response = await _httpClient.GetAsync(payload.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                if (!response.IsSuccessStatusCode)
                {
                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = false,
                        Error = $"Download HTTP error: {(int)response.StatusCode} {response.ReasonPhrase}",
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }

                using var httpStream = await response.Content.ReadAsStreamAsync(token);
                using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = await httpStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, token);
                    sha256.AppendData(buffer, 0, read);
                }

                await fileStream.FlushAsync(token);

                // Verify SHA256 integrity
                byte[] computedHashBytes = sha256.GetHashAndReset();
                string computedHash = Convert.ToHexString(computedHashBytes);

                if (!string.IsNullOrWhiteSpace(payload.ChecksumSha256) &&
                    !string.Equals(computedHash, payload.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }
                    sw.Stop();
                    return new CommandResponse
                    {
                        CommandId = command.Id,
                        Success = false,
                        Error = $"SHA256 hash mismatch. Expected: {payload.ChecksumSha256}, Computed: {computedHash}",
                        ExecutionTimeMs = sw.ElapsedMilliseconds
                    };
                }
            }

            // Deploy file or folder
            if (payload.IsDirectory)
            {
                if (File.Exists(finalDestinationPath))
                {
                    try { File.SetAttributes(finalDestinationPath, FileAttributes.Normal); } catch { }
                    File.Delete(finalDestinationPath);
                }
                else if (Directory.Exists(finalDestinationPath) && payload.Overwrite)
                {
                    try { Directory.Delete(finalDestinationPath, recursive: true); } catch { }
                }

                if (!Directory.Exists(finalDestinationPath))
                {
                    Directory.CreateDirectory(finalDestinationPath);
                }

                FileDistributionService.ExtractDirectoryArchive(tempFilePath, finalDestinationPath, overwrite: payload.Overwrite);
                try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }
            }
            else
            {
                if (Directory.Exists(finalDestinationPath))
                {
                    try { Directory.Delete(finalDestinationPath, recursive: true); } catch { }
                }
                else if (File.Exists(finalDestinationPath))
                {
                    try { File.SetAttributes(finalDestinationPath, FileAttributes.Normal); } catch { }
                    File.Delete(finalDestinationPath);
                }

                File.Move(tempFilePath, finalDestinationPath);
            }

            // Post-transfer actions: ShowNotification & AutoOpen
            if (payload.ShowNotification)
            {
                ShowStudentNotification(safeItemName, finalDestinationPath, payload.IsDirectory);
            }

            if (payload.AutoOpen)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = finalDestinationPath,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch { }
            }

            sw.Stop();
            return new CommandResponse
            {
                CommandId = command.Id,
                Success = true,
                Output = $"Item '{safeItemName}' successfully deployed to '{finalDestinationPath}'.",
                ExecutionTimeMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }
            sw.Stop();
            return new CommandResponse
            {
                CommandId = command.Id,
                Success = false,
                Error = $"Download and deploy failed: {ex.Message}",
                ExecutionTimeMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static void ShowStudentNotification(string itemName, string fullPath, bool isDirectory)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                string message = isDirectory
                    ? $"Folder '{itemName}' telah diterima dari Guru."
                    : $"File '{itemName}' telah diterima dari Guru.";

                var psi = new ProcessStartInfo("msg.exe")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("*");
                psi.ArgumentList.Add(message);
                Process.Start(psi);
            }
        }
        catch { }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch { }
        _listener = null;
        try { _screenLockService.Unlock(); } catch { }
        try { _examLockdownService.DisableExamMode(); } catch { }
        try { _processGuardService.StopMonitoring(); } catch { }
        try { _notificationService.Close(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _screenLockService.Dispose();
        _examLockdownService.Dispose();
        _processGuardService.Dispose();
        _notificationService.Dispose();
    }
}
