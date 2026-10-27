using System;

namespace LabSupport.Shared.Models;

public class ActivityLog
{
    public long Id { get; set; }
    public string TargetHost { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string TimestampFormatted => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
