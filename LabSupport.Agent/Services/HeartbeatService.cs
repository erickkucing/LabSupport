using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Agent.Services;

public class HeartbeatService : IDisposable
{
    private readonly string _managerIp;
    private readonly int _managerPort;
    private readonly int _intervalSeconds;
    private readonly ScreenCaptureService _captureService;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cts;
    private Task? _timerTask;

    public bool IsRunning => _timerTask != null && !_timerTask.IsCompleted;

    public HeartbeatService(
        string managerIp,
        int managerPort = 8080,
        int intervalSeconds = 5,
        ScreenCaptureService? captureService = null,
        HttpClient? httpClient = null)
    {
        _managerIp = managerIp;
        _managerPort = managerPort;
        _intervalSeconds = Math.Max(1, intervalSeconds);
        _captureService = captureService ?? new ScreenCaptureService();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _timerTask = Task.Run(() => RunHeartbeatLoopAsync(_cts.Token));
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatOnceAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Manager offline or network glitch; wait and retry
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<bool> SendHeartbeatOnceAsync(CancellationToken token = default)
    {
        var msg = CollectHeartbeatData();
        string json = MessageSerializer.Serialize(msg);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        string url = $"http://{_managerIp}:{_managerPort}/agent/heartbeat";
        var response = await _httpClient.PostAsync(url, content, token);
        return response.IsSuccessStatusCode;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    private static ulong _prevIdleTime;
    private static ulong _prevKernelTime;
    private static ulong _prevUserTime;
    private static double _lastCpuUsage = 5.0;

    public HeartbeatMessage CollectHeartbeatData()
    {
        var msg = new HeartbeatMessage
        {
            Hostname = Environment.MachineName,
            IpAddress = NetworkHelper.GetLocalIPv4Addresses().FirstOrDefault() ?? "127.0.0.1",
            MacAddress = NetworkHelper.GetDefaultMacAddress(),
            OsVersion = RuntimeInformation.OSDescription
        };

        // Disk info
        try
        {
            var systemDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
            if (systemDrive != null)
            {
                msg.DiskFreeGb = Math.Round((double)systemDrive.AvailableFreeSpace / (1024 * 1024 * 1024), 1);
            }
        }
        catch { }

        // Memory info (Real Win32 GlobalMemoryStatusEx with cross-platform fallback)
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    msg.RamTotalMb = Math.Round((double)mem.ullTotalPhys / (1024 * 1024), 0);
                    double availMb = Math.Round((double)mem.ullAvailPhys / (1024 * 1024), 0);
                    msg.RamUsageMb = Math.Max(0, msg.RamTotalMb - availMb);
                }
            }
            else
            {
                var memInfo = GC.GetGCMemoryInfo();
                double totalMb = Math.Round((double)memInfo.TotalAvailableMemoryBytes / (1024 * 1024), 0);
                if (totalMb <= 0) totalMb = 8192;
                msg.RamTotalMb = totalMb;
                msg.RamUsageMb = Math.Round((double)Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024), 0);
            }
        }
        catch
        {
            msg.RamTotalMb = 8192;
            msg.RamUsageMb = 2048;
        }

        // CPU calculation (Real Win32 GetSystemTimes with cross-platform fallback)
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    ulong idl = ((ulong)idle.dwHighDateTime << 32) | (uint)idle.dwLowDateTime;
                    ulong ker = ((ulong)kernel.dwHighDateTime << 32) | (uint)kernel.dwLowDateTime;
                    ulong usr = ((ulong)user.dwHighDateTime << 32) | (uint)user.dwLowDateTime;

                    if (_prevKernelTime > 0 && _prevUserTime > 0)
                    {
                        ulong usrDiff = usr - _prevUserTime;
                        ulong kerDiff = ker - _prevKernelTime;
                        ulong idlDiff = idl - _prevIdleTime;
                        ulong total = usrDiff + kerDiff;

                        if (total > 0 && total >= idlDiff)
                        {
                            double cpu = (double)(total - idlDiff) * 100.0 / total;
                            _lastCpuUsage = Math.Clamp(Math.Round(cpu, 1), 0.0, 100.0);
                        }
                    }

                    _prevIdleTime = idl;
                    _prevKernelTime = ker;
                    _prevUserTime = usr;
                }
                msg.CpuUsage = _lastCpuUsage;
            }
            else
            {
                msg.CpuUsage = 5.0;
            }
        }
        catch
        {
            msg.CpuUsage = 5.0;
        }

        // Capture thumbnail
        try
        {
            byte[] thumbJpeg = _captureService.CaptureThumbnailJpeg(320, 180, 50);
            msg.ScreenshotBase64 = Convert.ToBase64String(thumbJpeg);
        }
        catch { }

        return msg;
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
