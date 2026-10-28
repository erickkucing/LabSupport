using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LabSupport.Agent.Services;

public static class HardwareInfoService
{
    private static string? _cpuModel;
    private static string? _gpuModel;
    private static string? _motherboardSerial;
    private static double? _diskTotalGb;

    public static string GetCpuModel()
    {
        if (_cpuModel != null) return _cpuModel;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var val = key?.GetValue("ProcessorNameString") as string;
                if (!string.IsNullOrWhiteSpace(val))
                {
                    _cpuModel = val.Trim();
                    return _cpuModel;
                }
            }
            catch { }
        }

        string env = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty;
        _cpuModel = !string.IsNullOrWhiteSpace(env) ? env : RuntimeInformation.OSArchitecture.ToString();
        return _cpuModel;
    }

    public static string GetGpuModel()
    {
        if (_gpuModel != null) return _gpuModel;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (classKey != null)
                {
                    foreach (var subName in classKey.GetSubKeyNames())
                    {
                        if (subName.StartsWith("00", StringComparison.OrdinalIgnoreCase))
                        {
                            using var sub = classKey.OpenSubKey(subName);
                            var desc = sub?.GetValue("DriverDesc") as string;
                            if (!string.IsNullOrWhiteSpace(desc) && !desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                            {
                                _gpuModel = desc.Trim();
                                return _gpuModel;
                            }
                            if (!string.IsNullOrWhiteSpace(desc) && _gpuModel == null)
                            {
                                _gpuModel = desc.Trim();
                            }
                        }
                    }
                }
            }
            catch { }
        }

        _gpuModel = _gpuModel ?? "Integrated / Generic GPU";
        return _gpuModel;
    }

    public static string GetMotherboardSerial()
    {
        if (_motherboardSerial != null) return _motherboardSerial;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                var mbSerial = key?.GetValue("BaseBoardSerialNumber") as string;
                if (!string.IsNullOrWhiteSpace(mbSerial) && !mbSerial.Contains("None", StringComparison.OrdinalIgnoreCase))
                {
                    _motherboardSerial = mbSerial.Trim();
                    return _motherboardSerial;
                }

                var sysSerial = key?.GetValue("SystemSerialNumber") as string;
                if (!string.IsNullOrWhiteSpace(sysSerial) && !sysSerial.Contains("None", StringComparison.OrdinalIgnoreCase))
                {
                    _motherboardSerial = sysSerial.Trim();
                    return _motherboardSerial;
                }
            }
            catch { }
        }

        _motherboardSerial = "N/A";
        return _motherboardSerial;
    }

    public static (double totalGb, double freeGb) GetDiskInfo()
    {
        try
        {
            var systemDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
            if (systemDrive != null)
            {
                double free = Math.Round((double)systemDrive.AvailableFreeSpace / (1024 * 1024 * 1024), 1);
                double total = Math.Round((double)systemDrive.TotalSize / (1024 * 1024 * 1024), 1);
                _diskTotalGb = total;
                return (total, free);
            }
        }
        catch { }

        return (_diskTotalGb ?? 0, 0);
    }
}
