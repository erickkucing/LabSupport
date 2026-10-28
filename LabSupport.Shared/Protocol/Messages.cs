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
    StopBroadcast,
    LockAttention,
    UnlockAttention,
    SetAudioMute,
    SetAudioVolume,
    GetAudioState,
    SetExamMode,
    SetUsbBlock,
    SetInternetBlock,
    LaunchProcess,
    CollectFiles,
    SetClipboardText,
    SetProcessGuard,
    CleanWorkspace,
    UpdateManagerEndpoint,
    SetWebFilter,
    DownloadAndDeployFile,
    UninstallAgent,
    Sleep,
    SetBlackScreen,
    ShowNotification
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationType
{
    Info,
    Warning,
    Urgent,
    ConnectionCheck
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
    public string ManagerId { get; set; } = string.Empty;
    public string RoomName { get; set; } = "General";
    public string? DisplayName { get; set; }
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
    public string? ScreenshotBase64 { get; set; }
}

public class CommandMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ManagerId { get; set; } = string.Empty;
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

public class FileDistributionPayload
{
    public string TransferToken { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public string TargetDirectory { get; set; } = "Desktop";
    public string CustomSubfolder { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool AutoOpen { get; set; }
    public bool ShowNotification { get; set; } = true;
    public bool Overwrite { get; set; } = true;
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

public class AttentionPayload
{
    public bool Lock { get; set; } = true;
    public string Message { get; set; } = "PERHATIKAN GURU";
}

public class BlackScreenPayload
{
    public bool Enable { get; set; } = true;
}

public class AudioPayload
{
    public bool? Mute { get; set; }
    public float? Volume { get; set; } // 0 - 100 or 0.0 - 1.0
}

public class AudioStateResult
{
    public bool IsMuted { get; set; }
    public float MasterVolume { get; set; }
}

public class ExamModePayload
{
    public bool Enabled { get; set; } = true;
}

public class ToggleBlockPayload
{
    public bool Blocked { get; set; } = true;
}

public class LaunchPayload
{
    public string Target { get; set; } = string.Empty;
    public string? Arguments { get; set; }
}

public class CollectFilesPayload
{
    public string SourcePath { get; set; } = string.Empty;
    public string SearchPattern { get; set; } = "*.*";
    public int MaxFiles { get; set; } = 50;
}

public class CollectedFileItem
{
    public string RelativePath { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public class CollectFilesResponse
{
    public string Hostname { get; set; } = string.Empty;
    public System.Collections.Generic.List<CollectedFileItem> Files { get; set; } = new();
}

public class ClipboardPayload
{
    public string Text { get; set; } = string.Empty;
}

public class ProcessGuardPayload
{
    public bool Enabled { get; set; } = true;
    public System.Collections.Generic.List<string> BlacklistedProcesses { get; set; } = new();
}

public class CleanWorkspacePayload
{
    public bool CleanDownloads { get; set; } = true;
    public bool CleanTemp { get; set; } = true;
    public bool CleanRecycleBin { get; set; } = true;
    public bool CleanDesktopDocs { get; set; } = true;
    public bool CleanSsh { get; set; } = true;
    public bool CleanVirtualBoxVms { get; set; } = true;
}

public class CleanWorkspaceResult
{
    public bool Success { get; set; } = true;
    public int FilesDeletedCount { get; set; }
    public long BytesFreed { get; set; }
    public string SummaryText { get; set; } = string.Empty;
}

public class ManagerBeaconMessage
{
    public string Type { get; set; } = "ManagerBeacon";
    public string ManagerId { get; set; } = string.Empty;
    public string ManagerIp { get; set; } = string.Empty;
    public int HttpPort { get; set; } = 8080;
    public int CommandPort { get; set; } = 5902;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AgentDiscoveryRequest
{
    public string Type { get; set; } = "FindManager";
    public string ManagerId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class UpdateManagerEndpointPayload
{
    public string ManagerIp { get; set; } = string.Empty;
    public int ManagerPort { get; set; } = 8080;
    public string? ManagerId { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WebFilterMode
{
    Disabled,
    Blacklist,
    Whitelist
}

public class WebFilterPayload
{
    public WebFilterMode Mode { get; set; } = WebFilterMode.Blacklist;
    public System.Collections.Generic.List<string> Domains { get; set; } = new();
}

public class UnregisterAgentMessage
{
    public string Hostname { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string ManagerId { get; set; } = string.Empty;
    public string Reason { get; set; } = "User initiated uninstall";
}

public class HeartbeatResponsePayload
{
    public string Status { get; set; } = "OK";
    public string? Directive { get; set; }
}

public class NotificationPayload
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.Info;
    public int DurationSeconds { get; set; } = 5;
    public bool ShowHostInfo { get; set; } = false;
    public string? Hostname { get; set; }
    public string? IpAddress { get; set; }
    public string? RoomName { get; set; }
    public string? ManagerHost { get; set; }
}




