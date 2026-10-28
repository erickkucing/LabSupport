using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabSupport.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScriptActionType
{
    ClearTempCache,
    FlushDnsNetwork,
    LaunchExecutable,
    InstallWingetSoftware,
    KillProcess,
    SetUsbStorageLock,
    OpenUrlBrowser,
    SetSystemVolume,
    SetWallpaper,
    ShutdownRestart,
    LogoffUser,
    CustomPowerShellBlock
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExecutableLaunchMode
{
    InteractiveElevated, // Run on active student desktop with Admin rights without UAC prompt
    BackgroundSilent     // Run in background / session 0 headless
}

public class ScriptActionStep
{
    private readonly Dictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScriptActionType ActionType { get; set; } = ScriptActionType.ClearTempCache;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public Dictionary<string, string> Parameters
    {
        get => _parameters;
        set
        {
            if (value != null)
            {
                foreach (var kvp in value)
                {
                    _parameters[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    [JsonIgnore]
    public Dictionary<string, string> AdditionalParameters => Parameters;

    public ScriptActionStep()
    {
        UsbLocked = true;
        VolumePercent = 50;
        DelaySeconds = 0;
        IsRestart = false;
        LaunchMode = ExecutableLaunchMode.InteractiveElevated;
    }

    public ScriptActionStep(ScriptActionType actionType, string title = "") : this()
    {
        ActionType = actionType;
        Title = string.IsNullOrWhiteSpace(title) ? actionType.ToString() : title;
    }

    private string GetParam(string key, string defaultValue = "")
    {
        if (_parameters.TryGetValue(key, out var val))
            return val;
        return defaultValue;
    }

    private void SetParam(string key, string value)
    {
        _parameters[key] = value;
    }

    public string ExecutablePath
    {
        get => GetParam(nameof(ExecutablePath), string.Empty);
        set => SetParam(nameof(ExecutablePath), value ?? string.Empty);
    }

    public string Arguments
    {
        get => GetParam(nameof(Arguments), string.Empty);
        set => SetParam(nameof(Arguments), value ?? string.Empty);
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExecutableLaunchMode LaunchMode
    {
        get => Enum.TryParse<ExecutableLaunchMode>(GetParam(nameof(LaunchMode)), true, out var mode) ? mode : ExecutableLaunchMode.InteractiveElevated;
        set => SetParam(nameof(LaunchMode), value.ToString());
    }

    public string SoftwarePackageId
    {
        get => GetParam(nameof(SoftwarePackageId), GetParam("WingetPackageId", string.Empty));
        set
        {
            SetParam(nameof(SoftwarePackageId), value ?? string.Empty);
            SetParam("WingetPackageId", value ?? string.Empty);
        }
    }

    public string WingetPackageId
    {
        get => SoftwarePackageId;
        set => SoftwarePackageId = value;
    }

    public string ProcessName
    {
        get => GetParam(nameof(ProcessName), string.Empty);
        set => SetParam(nameof(ProcessName), value ?? string.Empty);
    }

    public string TargetUrl
    {
        get => GetParam(nameof(TargetUrl), GetParam("Url", string.Empty));
        set
        {
            SetParam(nameof(TargetUrl), value ?? string.Empty);
            SetParam("Url", value ?? string.Empty);
        }
    }

    public string Url
    {
        get => TargetUrl;
        set => TargetUrl = value;
    }

    public bool UsbLocked
    {
        get => !bool.TryParse(GetParam(nameof(UsbLocked)), out var locked) || locked;
        set => SetParam(nameof(UsbLocked), value.ToString().ToLowerInvariant());
    }

    public int VolumePercent
    {
        get
        {
            string val = GetParam(nameof(VolumePercent), GetParam("VolumeLevel", "50"));
            return int.TryParse(val, out var v) ? v : 50;
        }
        set
        {
            SetParam(nameof(VolumePercent), value.ToString());
            SetParam("VolumeLevel", value.ToString());
        }
    }

    public int VolumeLevel
    {
        get => VolumePercent;
        set => VolumePercent = value;
    }

    public string WallpaperPath
    {
        get => GetParam(nameof(WallpaperPath), string.Empty);
        set => SetParam(nameof(WallpaperPath), value ?? string.Empty);
    }

    public bool IsRestart
    {
        get
        {
            if (bool.TryParse(GetParam(nameof(IsRestart)), out var r))
                return r;
            return string.Equals(GetParam("ShutdownAction"), "Restart", StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            SetParam(nameof(IsRestart), value.ToString().ToLowerInvariant());
            SetParam("ShutdownAction", value ? "Restart" : "Shutdown");
        }
    }

    public string ShutdownAction
    {
        get => IsRestart ? "Restart" : "Shutdown";
        set => IsRestart = string.Equals(value, "Restart", StringComparison.OrdinalIgnoreCase);
    }

    public int DelaySeconds
    {
        get
        {
            string val = GetParam(nameof(DelaySeconds), GetParam("TimeoutSeconds", "0"));
            return int.TryParse(val, out var s) ? s : 0;
        }
        set
        {
            SetParam(nameof(DelaySeconds), value.ToString());
            SetParam("TimeoutSeconds", value.ToString());
        }
    }

    public int TimeoutSeconds
    {
        get => DelaySeconds;
        set => DelaySeconds = value;
    }

    public string CustomScript
    {
        get => GetParam(nameof(CustomScript), string.Empty);
        set => SetParam(nameof(CustomScript), value ?? string.Empty);
    }

    private static string EscapeForPowerShellSingleQuote(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Replace("'", "''");
    }

    public string ToPowerShellSnippet()
    {
        var sb = new StringBuilder();

        switch (ActionType)
        {
            case ScriptActionType.ClearTempCache:
                sb.AppendLine("# [Langkah: Bersihkan File Sementara & Cache]");
                sb.AppendLine("Write-Host \"Membersihkan temporary files dan cache sistem...\" -ForegroundColor Cyan");
                sb.AppendLine("try {");
                sb.AppendLine("    Remove-Item -Path \"$env:TEMP\\*\" -Recurse -Force -ErrorAction SilentlyContinue");
                sb.AppendLine("    Remove-Item -Path \"$env:SystemRoot\\Temp\\*\" -Recurse -Force -ErrorAction SilentlyContinue");
                sb.AppendLine("    if (Get-Command Clear-RecycleBin -ErrorAction SilentlyContinue) {");
                sb.AppendLine("        Clear-RecycleBin -Force -ErrorAction SilentlyContinue");
                sb.AppendLine("    }");
                sb.AppendLine("    Write-Host \"Cache dan temporary files berhasil dibersihkan.\" -ForegroundColor Green");
                sb.AppendLine("} catch {");
                sb.AppendLine("    Write-Warning \"Peringatan saat membersihkan temp: $_\"");
                sb.AppendLine("}");
                break;

            case ScriptActionType.FlushDnsNetwork:
                sb.AppendLine("# [Langkah: Reset DNS & Jaringan]");
                sb.AppendLine("Write-Host \"Mereset cache DNS dan memperbarui konfigurasi DHCP IP...\" -ForegroundColor Cyan");
                sb.AppendLine("try {");
                sb.AppendLine("    Clear-DnsClientCache -ErrorAction SilentlyContinue");
                sb.AppendLine("    ipconfig /flushdns | Out-Null");
                sb.AppendLine("    ipconfig /renew | Out-Null");
                sb.AppendLine("    Write-Host \"Cache DNS berhasil di-flush dan IP diperbarui.\" -ForegroundColor Green");
                sb.AppendLine("} catch {");
                sb.AppendLine("    Write-Warning \"Peringatan saat flush DNS: $_\"");
                sb.AppendLine("}");
                break;

            case ScriptActionType.LaunchExecutable:
                string escapedExe = EscapeForPowerShellSingleQuote(ExecutablePath);
                string escapedArgs = EscapeForPowerShellSingleQuote(Arguments);

                if (LaunchMode == ExecutableLaunchMode.InteractiveElevated)
                {
                    sb.AppendLine("# [Langkah: Jalankan Executable - Layar Siswa (Interactive Tanpa UAC)]");
                    sb.AppendLine("Write-Host \"Menjalankan executable di layar aktif siswa dengan hak Administrator (Bypass UAC)...\" -ForegroundColor Cyan");
                    sb.AppendLine("$taskName = \"LabSupport_Launch_\" + [System.Guid]::NewGuid().ToString(\"N\").Substring(0, 8)");
                    sb.AppendLine($"$exePath = '{escapedExe}'");
                    sb.AppendLine($"$exeArgs = '{escapedArgs}'");
                    sb.AppendLine("try {");
                    sb.AppendLine("    $taskCmd = if ([string]::IsNullOrWhiteSpace($exeArgs)) { \"`\"$exePath`\"\" } else { \"`\"$exePath`\" $exeArgs\" }");
                    sb.AppendLine("    schtasks.exe /create /tn \"$taskName\" /tr \"$taskCmd\" /sc ONCE /st 00:00 /rl HIGHEST /f /it | Out-Null");
                    sb.AppendLine("    schtasks.exe /run /tn \"$taskName\" | Out-Null");
                    sb.AppendLine("    Start-Sleep -Seconds 2");
                    sb.AppendLine("    schtasks.exe /delete /tn \"$taskName\" /f | Out-Null");
                    sb.AppendLine("    Write-Host \"Executable berhasil diluncurkan ke sesi layar siswa: $exePath\" -ForegroundColor Green");
                    sb.AppendLine("} catch {");
                    sb.AppendLine("    Write-Warning \"Gagal meluncurkan executable interaktif: $_\"");
                    sb.AppendLine("}");
                }
                else
                {
                    sb.AppendLine("# [Langkah: Jalankan Executable - Background Silent]");
                    sb.AppendLine("Write-Host \"Menjalankan executable di background secara silent...\" -ForegroundColor Cyan");
                    sb.AppendLine($"$exePath = '{escapedExe}'");
                    sb.AppendLine($"$exeArgs = '{escapedArgs}'");
                    sb.AppendLine("try {");
                    sb.AppendLine("    $proc = Start-Process -FilePath $exePath -ArgumentList $exeArgs -WindowStyle Hidden -PassThru");
                    sb.AppendLine("    Write-Host \"Executable background diluncurkan (PID: $($proc.Id)): $exePath\" -ForegroundColor Green");
                    sb.AppendLine("} catch {");
                    sb.AppendLine("    Write-Warning \"Gagal meluncurkan executable background: $_\"");
                    sb.AppendLine("}");
                }
                break;

            case ScriptActionType.InstallWingetSoftware:
                string escapedPackage = EscapeForPowerShellSingleQuote(SoftwarePackageId);
                sb.AppendLine("# [Langkah: Pasang Software via Winget]");
                sb.AppendLine("Write-Host \"Memasang software via Windows Package Manager (winget)...\" -ForegroundColor Cyan");
                sb.AppendLine($"$packageId = '{escapedPackage}'");
                sb.AppendLine("try {");
                sb.AppendLine("    winget install --id \"$packageId\" --exact --silent --accept-source-agreements --accept-package-agreements --disable-interactivity");
                sb.AppendLine("    Write-Host \"Pemasangan software $packageId selesai.\" -ForegroundColor Green");
                sb.AppendLine("} catch {");
                sb.AppendLine("    Write-Warning \"Gagal menginstall software via winget: $_\"");
                sb.AppendLine("}");
                break;

            case ScriptActionType.KillProcess:
                string escapedProc = EscapeForPowerShellSingleQuote(ProcessName);
                sb.AppendLine("# [Langkah: Hentikan Proses]");
                sb.AppendLine("Write-Host \"Menghentikan proses yang ditentukan...\" -ForegroundColor Cyan");
                sb.AppendLine($"$procName = '{escapedProc}'");
                sb.AppendLine("try {");
                sb.AppendLine("    Stop-Process -Name \"$procName\" -Force -ErrorAction SilentlyContinue");
                sb.AppendLine("    taskkill.exe /F /IM \"$procName.exe\" /T 2>$null | Out-Null");
                sb.AppendLine("    Write-Host \"Proses $procName berhasil dihentikan.\" -ForegroundColor Green");
                sb.AppendLine("} catch {");
                sb.AppendLine("    Write-Warning \"Gagal menghentikan proses $procName: $_\"");
                sb.AppendLine("}");
                break;

            case ScriptActionType.SetUsbStorageLock:
                sb.AppendLine("# [Langkah: Kunci / Buka Penyimpanan USB]");
                if (UsbLocked)
                {
                    sb.AppendLine("Write-Host \"Mengunci akses penyimpanan massal USB (Registry Block)...\" -ForegroundColor Yellow");
                    sb.AppendLine("Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR' -Name 'Start' -Value 4 -Type DWord -Force -ErrorAction SilentlyContinue");
                    sb.AppendLine("Write-Host \"Penyimpanan USB berhasil dikunci.\" -ForegroundColor Green");
                }
                else
                {
                    sb.AppendLine("Write-Host \"Membuka kunci akses penyimpanan USB...\" -ForegroundColor Cyan");
                    sb.AppendLine("Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR' -Name 'Start' -Value 3 -Type DWord -Force -ErrorAction SilentlyContinue");
                    sb.AppendLine("Write-Host \"Penyimpanan USB berhasil dibuka.\" -ForegroundColor Green");
                }
                break;

            case ScriptActionType.OpenUrlBrowser:
                string escapedUrl = EscapeForPowerShellSingleQuote(TargetUrl);
                sb.AppendLine("# [Langkah: Buka URL di Browser]");
                sb.AppendLine("Write-Host \"Membuka URL di browser siswa...\" -ForegroundColor Cyan");
                sb.AppendLine($"$targetUrl = '{escapedUrl}'");
                sb.AppendLine("try {");
                sb.AppendLine("    Start-Process \"$targetUrl\"");
                sb.AppendLine("    Write-Host \"URL $targetUrl berhasil dibuka.\" -ForegroundColor Green");
                sb.AppendLine("} catch {");
                sb.AppendLine("    Write-Warning \"Gagal membuka URL: $_\"");
                sb.AppendLine("}");
                break;

            case ScriptActionType.SetSystemVolume:
                int vol = Math.Clamp(VolumePercent, 0, 100);
                sb.AppendLine("# [Langkah: Atur Volume Suara Master]");
                sb.AppendLine($"$volPercent = {vol}");
                sb.AppendLine("Write-Host \"Mengatur volume suara master ke $volPercent%...\" -ForegroundColor Cyan");
                sb.AppendLine("try {");
                sb.AppendLine("    $wsh = New-Object -ComObject WScript.Shell");
                sb.AppendLine("    1..50 | ForEach-Object { $wsh.SendKeys([char]174) } # Mute down completely");
                sb.AppendLine("    if ($volPercent -gt 0) {");
                sb.AppendLine("        $steps = [math]::Round($volPercent / 2)");
                sb.AppendLine("        1..$steps | ForEach-Object { $wsh.SendKeys([char]175) } # Step up");
                sb.AppendLine("    }");
                sb.AppendLine("    Write-Host \"Volume master disetel ke $volPercent%.\" -ForegroundColor Green");
                sb.AppendLine("} catch {");
                sb.AppendLine("    Write-Warning \"Peringatan saat mengatur volume: $_\"");
                sb.AppendLine("}");
                break;

            case ScriptActionType.SetWallpaper:
                string escapedWp = EscapeForPowerShellSingleQuote(WallpaperPath);
                sb.AppendLine("# [Langkah: Ganti Wallpaper Desktop]");
                sb.AppendLine($"$wpPath = '{escapedWp}'");
                sb.AppendLine("Write-Host \"Mengganti wallpaper desktop...\" -ForegroundColor Cyan");
                sb.AppendLine("try {");
                sb.AppendLine("    $def = @'");
                sb.AppendLine("    using System.Runtime.InteropServices;");
                sb.AppendLine("    public class WallpaperSetter {");
                sb.AppendLine("        [DllImport(\"user32.dll\", CharSet = CharSet.Auto)]");
                sb.AppendLine("        public static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);");
                sb.AppendLine("    }");
                sb.AppendLine("'@");
                sb.AppendLine("    if (-not ([System.Management.Automation.PSTypeName]'WallpaperSetter').Type) {");
                sb.AppendLine("        Add-Type -TypeDefinition $def");
                sb.AppendLine("    }");
                sb.AppendLine("    [WallpaperSetter]::SystemParametersInfo(0x0014, 0, $wpPath, 0x01 -bor 0x02) | Out-Null");
                sb.AppendLine("    Write-Host \"Wallpaper desktop berhasil diperbarui ke: $wpPath\" -ForegroundColor Green");
                sb.AppendLine("} catch {");
                sb.AppendLine("    Write-Warning \"Peringatan saat mengganti wallpaper: $_\"");
                sb.AppendLine("}");
                break;

            case ScriptActionType.ShutdownRestart:
                int delaySec = Math.Max(0, DelaySeconds);
                sb.AppendLine("# [Langkah: Matikan atau Restart Komputer]");
                if (IsRestart)
                {
                    sb.AppendLine($"Write-Host \"Merestart komputer dalam {delaySec} detik...\" -ForegroundColor Yellow");
                    sb.AppendLine($"shutdown.exe /r /t {delaySec} /f /c \"Restart diminta oleh LabSupport.\"");
                }
                else
                {
                    sb.AppendLine($"Write-Host \"Mematikan komputer dalam {delaySec} detik...\" -ForegroundColor Yellow");
                    sb.AppendLine($"shutdown.exe /s /t {delaySec} /f /c \"Shutdown diminta oleh LabSupport.\"");
                }
                break;

            case ScriptActionType.LogoffUser:
                sb.AppendLine("# [Langkah: Log Off Sesi Pengguna Aktif]");
                sb.AppendLine("Write-Host \"Melakukan log off sesi pengguna aktif...\" -ForegroundColor Yellow");
                sb.AppendLine("try {");
                sb.AppendLine("    shutdown.exe /l");
                sb.AppendLine("    Write-Host \"Perintah log off berhasil dikirim.\" -ForegroundColor Green");
                sb.AppendLine("} catch {");
                sb.AppendLine("    Write-Warning \"Gagal melakukan log off: $_\"");
                sb.AppendLine("}");
                break;

            case ScriptActionType.CustomPowerShellBlock:
                string safeTitle = string.IsNullOrWhiteSpace(Title) ? "Blok PowerShell Kustom" : Title;
                sb.AppendLine($"# [Langkah Kustom: {EscapeForPowerShellSingleQuote(safeTitle)}]");
                sb.AppendLine(string.IsNullOrWhiteSpace(CustomScript) ? "# (Tidak ada skrip kustom ditentukan)" : CustomScript);
                break;
        }

        return sb.ToString().TrimEnd();
    }
}

public class ScriptRecipe
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Resep Script Baru";
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = "LabSupport Manager";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0";
    public List<ScriptActionStep> Steps { get; set; } = new();

    public string GeneratePowerShellScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ========================================================");
        sb.AppendLine($"# LabSupport Recipe: {Name}");
        if (!string.IsNullOrWhiteSpace(Description))
            sb.AppendLine($"# Deskripsi        : {Description}");
        sb.AppendLine($"# Dibuat Pada      : {CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Penulis          : {Author}");
        sb.AppendLine("# ========================================================");
        sb.AppendLine();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        sb.AppendLine();
        sb.AppendLine("Write-Host '==================================================' -ForegroundColor Cyan");
        sb.AppendLine($"Write-Host '  Mulai Menjalankan Resep: {Name}' -ForegroundColor Cyan");
        sb.AppendLine("Write-Host '==================================================' -ForegroundColor Cyan");
        sb.AppendLine();

        var activeSteps = Steps.Where(s => s.IsEnabled).ToList();
        int stepNum = 1;
        foreach (var step in activeSteps)
        {
            sb.AppendLine("# --------------------------------------------------------");
            string stepTitle = string.IsNullOrWhiteSpace(step.Title) ? step.ActionType.ToString() : step.Title;
            sb.AppendLine($"# [{stepNum}/{activeSteps.Count}] {stepTitle}");
            sb.AppendLine("# --------------------------------------------------------");
            sb.AppendLine(step.ToPowerShellSnippet());
            sb.AppendLine();
            stepNum++;
        }

        sb.AppendLine("Write-Host '==================================================' -ForegroundColor Green");
        sb.AppendLine("Write-Host ' [SUKSES] Seluruh langkah resep selesai dieksekusi.' -ForegroundColor Green");
        sb.AppendLine("Write-Host '==================================================' -ForegroundColor Green");
        return sb.ToString();
    }

    public string ToPowerShellSnippet() => GeneratePowerShellScript();

    public string GenerateBatchWrapper()
    {
        var psPayload = GeneratePowerShellScript();
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal EnableDelayedExpansion");
        sb.AppendLine($"title LabSupport - {Name}");
        sb.AppendLine();
        sb.AppendLine("REM ========================================================");
        sb.AppendLine($"REM Resep LabSupport: {Name}");
        sb.AppendLine("REM Standalone Batch Wrapper dengan Auto-Elevasi Administrator");
        sb.AppendLine("REM ========================================================");
        sb.AppendLine();
        sb.AppendLine("REM 1. Verifikasi & Permintaan Hak Administrator (Auto-Elevation)");
        sb.AppendLine("net session >nul 2>&1");
        sb.AppendLine("if '%errorlevel%' NEQ '0' (");
        sb.AppendLine("    echo [INFO] Memerlukan hak Administrator. Meminta elevasi...");
        sb.AppendLine("    powershell -NoProfile -ExecutionPolicy Bypass -Command \"Start-Process -FilePath '%~f0' -ArgumentList '%*' -WorkingDirectory '%~dp0' -Verb RunAs\"");
        sb.AppendLine("    exit /b");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("REM 2. Jalankan Payload PowerShell yang Tersemat di Bawah");
        sb.AppendLine("pushd \"%~dp0\"");
        sb.AppendLine("powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^");
        sb.AppendLine("    \"$f='%~f0'; $raw=[System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8); $m=':::' + '___LABSUPPORT_POWERSHELL_PAYLOAD___'; $i=$raw.LastIndexOf($m); if($i -ge 0){ $code=$raw.Substring($i+$m.Length); $sb=[ScriptBlock]::Create($code); & $sb %* }\"");
        sb.AppendLine("set EXIT_CODE=%errorlevel%");
        sb.AppendLine("popd");
        sb.AppendLine("if %EXIT_CODE% NEQ 0 (");
        sb.AppendLine("    echo.");
        sb.AppendLine("    echo [ERROR] Eksekusi selesai dengan kode error %EXIT_CODE%.");
        sb.AppendLine("    pause");
        sb.AppendLine(")");
        sb.AppendLine("exit /b %EXIT_CODE%");
        sb.AppendLine();
        sb.AppendLine(":::___LABSUPPORT_POWERSHELL_PAYLOAD___");
        sb.Append(psPayload);
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ScriptRecipe? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<ScriptRecipe>(json, JsonOptions);
    }
}
