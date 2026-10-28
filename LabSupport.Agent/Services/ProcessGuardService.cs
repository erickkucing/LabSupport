using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LabSupport.Agent.Services;

public class ProcessGuardService : IDisposable
{
    private readonly object _lock = new();
    private readonly HashSet<string> _blacklistedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private bool _isEnabled;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private readonly int _scanIntervalMs;

    public bool IsEnabled
    {
        get
        {
            lock (_lock) return _isEnabled;
        }
    }

    public IReadOnlyCollection<string> BlacklistedProcesses
    {
        get
        {
            lock (_lock) return _blacklistedProcesses.ToList().AsReadOnly();
        }
    }

    public ProcessGuardService(int scanIntervalMs = 2000)
    {
        _scanIntervalMs = scanIntervalMs;
    }

    public void Configure(bool isEnabled, IEnumerable<string>? blacklistedProcesses)
    {
        lock (_lock)
        {
            _isEnabled = isEnabled;
            _blacklistedProcesses.Clear();
            if (blacklistedProcesses != null)
            {
                foreach (var p in blacklistedProcesses)
                {
                    string cleaned = CleanProcessName(p);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        _blacklistedProcesses.Add(cleaned);
                    }
                }
            }

            if (_isEnabled && (_cts == null || _cts.IsCancellationRequested))
            {
                StartMonitoring();
            }
            else if (!_isEnabled && _cts != null && !_cts.IsCancellationRequested)
            {
                StopMonitoring();
            }
        }
    }

    public void StartMonitoring()
    {
        lock (_lock)
        {
            if (_cts != null && !_cts.IsCancellationRequested) return;

            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        }
    }

    public void StopMonitoring()
    {
        lock (_lock)
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }
            _cts = null;
            _monitorTask = null;
        }
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (IsEnabled)
                {
                    ScanAndKill();
                }
                await Task.Delay(_scanIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Silent fail-safe
            }
        }
    }

    public int ScanAndKill()
    {
        lock (_lock)
        {
            if (!_isEnabled || _blacklistedProcesses.Count == 0) return 0;
        }

        int killedCount = 0;
        try
        {
            Process[] processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    string name = proc.ProcessName;
                    if (IsBlacklisted(name))
                    {
                        proc.Kill(true);
                        killedCount++;
                    }
                }
                catch
                {
                    // Silent: ignore access denied, process already exited, or system processes
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Silent
        }

        return killedCount;
    }

    public bool IsBlacklisted(string processName)
    {
        string cleaned = CleanProcessName(processName);
        if (string.IsNullOrWhiteSpace(cleaned)) return false;

        lock (_lock)
        {
            return _blacklistedProcesses.Contains(cleaned);
        }
    }

    public static string CleanProcessName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string name = raw.Trim();
        int lastSlash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (lastSlash >= 0 && lastSlash < name.Length - 1)
        {
            name = name[(lastSlash + 1)..];
        }
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }
        return name.Trim().ToLowerInvariant();
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
