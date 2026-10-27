using System;

namespace LabSupport.Shared.Models;

public class ComputerInfo
{
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public double RamUsageMb { get; set; }
    public double RamTotalMb { get; set; }
    public double DiskFreeGb { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public bool IsOnline { get; set; }
    public string? Notes { get; set; }
    public byte[]? ThumbnailData { get; set; }

    public double RamPercent => RamTotalMb > 0 ? Math.Clamp((RamUsageMb / RamTotalMb) * 100.0, 0, 100) : 0;
    public string CpuFormatted => $"{CpuUsage:F1}%";
    public string RamFormatted => $"{RamUsageMb:F0} / {RamTotalMb:F0} MB ({RamPercent:F0}%)";
    public string DiskFormatted => $"{DiskFreeGb:F1} GB";
    public string LastSeenFormatted => LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
