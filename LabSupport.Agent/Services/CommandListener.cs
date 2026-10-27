using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;

namespace LabSupport.Agent.Services;

public class CommandListener : IDisposable
{
    private readonly int _port;
    private readonly PowerManager _powerManager;
    private readonly ScriptExecutor _scriptExecutor;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public bool IsRunning => _listener != null;

    public CommandListener(
        int port = 5902,
        PowerManager? powerManager = null,
        ScriptExecutor? scriptExecutor = null)
    {
        _port = port;
        _powerManager = powerManager ?? new PowerManager();
        _scriptExecutor = scriptExecutor ?? new ScriptExecutor();
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

            case CommandType.ShowMessage:
                string msg = command.Payload ?? string.Empty;
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Process.Start(new ProcessStartInfo("msg.exe", $"* \"{msg.Replace("\"", "\\\"")}\"")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                }
                catch { }

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

                if (int.TryParse(target, out int pid))
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
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
                    var procs = Process.GetProcessesByName(target);
                    int count = 0;
                    foreach (var p in procs)
                    {
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

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch { }
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
