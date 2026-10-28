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
    public double DiskTotalGb { get; set; }
    public string CpuModel { get; set; } = string.Empty;
    public string GpuModel { get; set; } = string.Empty;
    public string MotherboardSerial { get; set; } = string.Empty;
    public double NetworkDownloadKbps { get; set; }
    public double NetworkUploadKbps { get; set; }
    public int IdleSeconds { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public bool IsOnline { get; set; }
    public bool IsSelected { get; set; }
    public string? Notes { get; set; }
    public string GroupName { get; set; } = "Default";
    public string RoomName { get; set; } = "General";
    public string? DisplayName { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public byte[]? ThumbnailData { get; set; }

    public string EffectiveName => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Hostname;

    public double RamPercent => RamTotalMb > 0 ? Math.Clamp((RamUsageMb / RamTotalMb) * 100.0, 0, 100) : 0;
    public string CpuFormatted => $"{CpuUsage:F1}%";
    public string RamFormatted => $"{RamUsageMb:F0} / {RamTotalMb:F0} MB ({RamPercent:F0}%)";
    public string DiskFormatted => $"{DiskFreeGb:F1} GB";
    public string LastSeenFormatted => LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string NetworkFormatted
    {
        get
        {
            string downStr = NetworkDownloadKbps >= 1024.0
                ? $"{NetworkDownloadKbps / 1024.0:F1} MB/s"
                : $"{NetworkDownloadKbps:F0} KB/s";
            string upStr = NetworkUploadKbps >= 1024.0
                ? $"{NetworkUploadKbps / 1024.0:F1} MB/s"
                : $"{NetworkUploadKbps:F0} KB/s";
            return $"⬇ {downStr}  ⬆ {upStr}";
        }
    }

    public string IdleFormatted
    {
        get
        {
            if (IdleSeconds < 60) return "Aktif";
            if (IdleSeconds < 3600) return $"💤 Pasif {IdleSeconds / 60}m";
            return $"💤 Pasif {IdleSeconds / 3600}j";
        }
    }

    public bool IsBandwidthHog => NetworkDownloadKbps >= 3000.0;
    public bool IsIdle => IdleSeconds >= 300;
    public bool IsLowDiskSpace => DiskFreeGb < 10.0 || (DiskTotalGb > 0 && (DiskFreeGb / DiskTotalGb) < 0.10);
    public bool IsHighRamUsage => RamPercent >= 90.0;
    public string DiskStorageFormatted => DiskTotalGb > 0 ? $"Sisa {DiskFreeGb:F1} GB dari {DiskTotalGb:F0} GB" : $"{DiskFreeGb:F1} GB";
}
