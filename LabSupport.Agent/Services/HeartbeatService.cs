using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Agent.Services;

public class HeartbeatService : IDisposable
{
    private string _managerIp;
    private int _managerPort;
    private readonly int _intervalSeconds;
    private readonly string _managerId;
    private readonly string _roomName;
    private readonly string? _displayName;
    private readonly ScreenCaptureService _captureService;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cts;
    private Task? _timerTask;
    private int _consecutiveFailures;

    public bool IsRunning => _timerTask != null && !_timerTask.IsCompleted;
    public string ManagerId => _managerId;
    public string ManagerIp => _managerIp;
    public int ManagerPort => _managerPort;
    public int ConsecutiveFailures => _consecutiveFailures;

    public event Action? OnConnectionLost;
    public event Action? OnConnectionRestored;
    public event Action? OnUninstallDirectiveReceived;

    public void UpdateManagerEndpoint(string newIp, int newPort)
    {
        if (string.IsNullOrWhiteSpace(newIp)) return;
        _managerIp = newIp;
        _managerPort = newPort > 0 ? newPort : 8080;
        _consecutiveFailures = 0;
    }

    public HeartbeatService(
        string managerIp,
        int managerPort = 8080,
        int intervalSeconds = 5,
        string managerId = "",
        ScreenCaptureService? captureService = null,
        HttpClient? httpClient = null,
        string roomName = "General",
        string? displayName = null)
    {
        _managerIp = managerIp;
        _managerPort = managerPort;
        _intervalSeconds = Math.Max(1, intervalSeconds);
        _managerId = managerId;
        _roomName = string.IsNullOrWhiteSpace(roomName) ? "General" : roomName;
        _displayName = displayName;
        _captureService = captureService ?? new ScreenCaptureService();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _timerTask = Task.Run(() => RunHeartbeatLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool success = false;
            try
            {
                success = await SendHeartbeatOnceAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                success = false;
            }

            if (success)
            {
                if (_consecutiveFailures > 0)
                {
                    _consecutiveFailures = 0;
                    OnConnectionRestored?.Invoke();
                }
            }
            else
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= 2)
                {
                    OnConnectionLost?.Invoke();
                }
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
        if (response.IsSuccessStatusCode)
        {
            try
            {
                string respJson = await response.Content.ReadAsStringAsync(token);
                if (!string.IsNullOrWhiteSpace(respJson))
                {
                    var respObj = MessageSerializer.Deserialize<HeartbeatResponsePayload>(respJson);
                    if (string.Equals(respObj?.Directive, "uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        OnUninstallDirectiveReceived?.Invoke();
                    }
                }
            }
            catch { }
            return true;
        }
        return false;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private static ulong _prevIdleTime;
    private static ulong _prevKernelTime;
    private static ulong _prevUserTime;
    private static double _lastCpuUsage = 5.0;

    private static long _prevBytesReceived;
    private static long _prevBytesSent;
    private static DateTime _prevNetworkTime = DateTime.MinValue;

    public HeartbeatMessage CollectHeartbeatData()
    {
        var msg = new HeartbeatMessage
        {
            Hostname = Environment.MachineName,
            IpAddress = NetworkHelper.GetLocalIPv4Addresses().FirstOrDefault() ?? "127.0.0.1",
            MacAddress = NetworkHelper.GetDefaultMacAddress(),
            OsVersion = RuntimeInformation.OSDescription,
            ManagerId = _managerId,
            RoomName = _roomName,
            DisplayName = _displayName
        };

        // Hardware & Disk info
        try
        {
            var (totalGb, freeGb) = HardwareInfoService.GetDiskInfo();
            msg.DiskTotalGb = totalGb;
            msg.DiskFreeGb = freeGb;
            msg.CpuModel = HardwareInfoService.GetCpuModel();
            msg.GpuModel = HardwareInfoService.GetGpuModel();
            msg.MotherboardSerial = HardwareInfoService.GetMotherboardSerial();
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

        // Network Speed & Inactivity Idle Tracking
        try
        {
            var (downKbps, upKbps) = CalculateNetworkSpeed();
            msg.NetworkDownloadKbps = downKbps;
            msg.NetworkUploadKbps = upKbps;
        }
        catch { }

        try
        {
            msg.IdleSeconds = GetIdleTimeSeconds();
        }
        catch { }

        // Capture thumbnail
        try
        {
            byte[] thumbJpeg = _captureService.CaptureThumbnailJpeg(320, 180, 50);
            msg.ScreenshotBase64 = Convert.ToBase64String(thumbJpeg);
        }
        catch { }

        return msg;
    }

    private static (double downloadKbps, double uploadKbps) CalculateNetworkSpeed()
    {
        try
        {
            long currentReceived = 0;
            long currentSent = 0;
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var stats = ni.GetIPStatistics();
                    currentReceived += stats.BytesReceived;
                    currentSent += stats.BytesSent;
                }
            }

            DateTime now = DateTime.UtcNow;
            if (_prevNetworkTime == DateTime.MinValue)
            {
                _prevBytesReceived = currentReceived;
                _prevBytesSent = currentSent;
                _prevNetworkTime = now;
                return (0.0, 0.0);
            }

            double elapsedSeconds = (now - _prevNetworkTime).TotalSeconds;
            if (elapsedSeconds <= 0.1) return (0.0, 0.0);

            long rxDiff = Math.Max(0, currentReceived - _prevBytesReceived);
            long txDiff = Math.Max(0, currentSent - _prevBytesSent);

            _prevBytesReceived = currentReceived;
            _prevBytesSent = currentSent;
            _prevNetworkTime = now;

            double downKbps = Math.Round((double)rxDiff / 1024.0 / elapsedSeconds, 1);
            double upKbps = Math.Round((double)txDiff / 1024.0 / elapsedSeconds, 1);
            return (downKbps, upKbps);
        }
        catch
        {
            return (0.0, 0.0);
        }
    }

    private static int GetIdleTimeSeconds()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var lii = new LASTINPUTINFO();
                lii.cbSize = (uint)Marshal.SizeOf(lii);
                if (GetLastInputInfo(ref lii))
                {
                    uint envTicks = (uint)Environment.TickCount;
                    if (envTicks >= lii.dwTime)
                    {
                        return (int)((envTicks - lii.dwTime) / 1000);
                    }
                }
            }
        }
        catch { }
        return 0;
    }

    public void Dispose()
    {
        Stop();
    }
}
