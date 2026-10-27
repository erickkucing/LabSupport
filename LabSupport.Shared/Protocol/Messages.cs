using System;
using System.Text.Json.Serialization;

namespace LabSupport.Shared.Protocol;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommandType
{
    Ping,
    Shutdown,
    Restart,
    LockScreen,
    ShowMessage,
    ExecuteScript,
    KillProcess,
    GetProcessList,
    MouseEvent,
    KeyboardEvent,
    UploadFile,
    DownloadFile,
    GetFileList,
    StartBroadcast,
    StopBroadcast
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScriptType
{
    PowerShell,
    Batch
}

public class HeartbeatMessage
{
    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public double RamUsageMb { get; set; }
    public double RamTotalMb { get; set; }
    public double DiskFreeGb { get; set; }
    public string? ScreenshotBase64 { get; set; }
}

public class CommandMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CommandType Type { get; set; }
    public string? Payload { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ScriptPayload
{
    public ScriptType Type { get; set; } = ScriptType.PowerShell;
    public string ScriptContent { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}

public class CommandResponse
{
    public string CommandId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public long ExecutionTimeMs { get; set; }
}

public class ProcessInfoItem
{
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double MemoryMb { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MouseAction
{
    Move,
    LeftDown,
    LeftUp,
    LeftClick,
    RightDown,
    RightUp,
    RightClick,
    DoubleClick,
    MiddleClick,
    Scroll
}

public class MouseInputPayload
{
    public MouseAction Action { get; set; } = MouseAction.LeftClick;
    public double NormalizedX { get; set; } // 0.0 to 1.0 relative to target screen
    public double NormalizedY { get; set; } // 0.0 to 1.0 relative to target screen
    public int WheelDelta { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KeyboardAction
{
    Text,
    Shortcut,
    KeyDown,
    KeyUp
}

public class KeyboardInputPayload
{
    public KeyboardAction Action { get; set; } = KeyboardAction.Text;
    public string? Text { get; set; }
    public string? Key { get; set; } // e.g. "Win+D", "Alt+F4", "Ctrl+Shift+Esc"
}

public class FileTransferPayload
{
    public string FileName { get; set; } = string.Empty;
    public string? TargetDirectory { get; set; }
    public string ContentBase64 { get; set; } = string.Empty;
}

public class FileListPayload
{
    public string? DirectoryPath { get; set; }
}

public class FileItemInfo
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
}
