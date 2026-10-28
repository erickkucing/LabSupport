using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LabSupport.Shared.Models;

namespace LabSupport.Shared.Services;

public class ScriptGenerator
{
    private static string SanitizeParameter(string? input, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(input)) return fallback;
        return input.Replace("\r", "").Replace("\n", "").Replace("\"", "").Trim();
    }

    private static string EscapeForPowerShellSingleQuote(string input)
    {
        return input.Replace("'", "''");
    }

    public static string GeneratePowerShellScript(
        string managerIp,
        int managerPort = 8080,
        string managerId = "LAB-DEFAULT",
        string serviceName = "LabSupportAgent",
        string installDir = @"C:\ProgramData\LabSupport",
        string roomName = "General",
        int targetCapacity = 30)
    {
        string cleanIp = SanitizeParameter(managerIp, "127.0.0.1");
        string cleanId = SanitizeParameter(managerId, "LAB-DEFAULT");
        string cleanService = SanitizeParameter(serviceName, "LabSupportAgent");
        string cleanDir = SanitizeParameter(installDir, @"C:\ProgramData\LabSupport");
        string cleanRoom = SanitizeParameter(roomName, "General");
        int cleanPort = Math.Clamp(managerPort, 1, 65535);
        int cleanCap = Math.Clamp(targetCapacity, 1, 1000);

        var sb = new StringBuilder();
        sb.AppendLine(@"# ========================================================");
        sb.AppendLine(@"# LabSupport Agent - Automated Headless Installation Script");
        sb.AppendLine(@"# Generated automatically by LabSupport.Manager");
        sb.AppendLine(@"# ========================================================");
        sb.AppendLine();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        sb.AppendLine($"$ManagerIP      = '{EscapeForPowerShellSingleQuote(cleanIp)}'");
        sb.AppendLine($"$ManagerPort    = {cleanPort}");
        sb.AppendLine($"$ManagerId      = '{EscapeForPowerShellSingleQuote(cleanId)}'");
        sb.AppendLine($"$ServiceName    = '{EscapeForPowerShellSingleQuote(cleanService)}'");
        sb.AppendLine($"$InstallDir     = '{EscapeForPowerShellSingleQuote(cleanDir)}'");
        sb.AppendLine($"$RoomName       = '{EscapeForPowerShellSingleQuote(cleanRoom)}'");
        sb.AppendLine($"$TargetCapacity = {cleanCap}");
        sb.AppendLine();
        sb.AppendLine(@"Write-Host '==================================================' -ForegroundColor Cyan");
        sb.AppendLine(@"Write-Host '         LabSupport Agent Installer              ' -ForegroundColor Cyan");
        sb.AppendLine(@"Write-Host ""   PC Manager      : $ManagerIP:$ManagerPort"" -ForegroundColor Cyan");
        sb.AppendLine(@"Write-Host ""   Lab Pairing Code: $ManagerId"" -ForegroundColor Cyan");
        sb.AppendLine(@"Write-Host ""   Kategori Ruang  : $RoomName ($TargetCapacity PC)"" -ForegroundColor Cyan");
        sb.AppendLine(@"Write-Host '==================================================' -ForegroundColor Cyan");
        sb.AppendLine();
        sb.AppendLine(@"# Deteksi Identitas Komputer secara otomatis (Zero Input / One-Click Deploy)");
        sb.AppendLine(@"$curHost = [System.Environment]::MachineName");
        sb.AppendLine(@"$assignedDisplayName = if ($args -and $args.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($args[0])) { $args[0].Trim().Replace('""', '') } else { $curHost.Replace('""', '') }");
        sb.AppendLine(@"Write-Host ""Identitas Komputer : $assignedDisplayName"" -ForegroundColor Green");
        sb.AppendLine();
        sb.AppendLine(@"# 1. Cek Koneksi Jaringan ke PC Manajemen (Wajib Ping check)");
        sb.AppendLine(@"Write-Host '[1/5] Memeriksa koneksi jaringan ke PC Manajemen (Ping)...' -ForegroundColor Yellow");
        sb.AppendLine(@"$connected = $false");
        sb.AppendLine(@"try {");
        sb.AppendLine(@"    if (Test-Connection -ComputerName $ManagerIP -Count 2 -Quiet) {");
        sb.AppendLine(@"        $connected = $true");
        sb.AppendLine(@"    }");
        sb.AppendLine(@"} catch {}");
        sb.AppendLine();
        sb.AppendLine(@"if (-not $connected) {");
        sb.AppendLine(@"    # Fallback cek via HTTP Health port jika ICMP dinonaktifkan firewall");
        sb.AppendLine(@"    try {");
        sb.AppendLine(@"        $req = [System.Net.WebRequest]::Create(""http://${ManagerIP}:${ManagerPort}/health"")");
        sb.AppendLine(@"        $req.Timeout = 4000");
        sb.AppendLine(@"        $res = $req.GetResponse()");
        sb.AppendLine(@"        if ($res.StatusCode -eq 200) { $connected = $true }");
        sb.AppendLine(@"        $res.Close()");
        sb.AppendLine(@"    } catch {}");
        sb.AppendLine(@"}");
        sb.AppendLine();
        sb.AppendLine(@"if (-not $connected) {");
        sb.AppendLine(@"    Write-Host '--------------------------------------------------' -ForegroundColor Red");
        sb.AppendLine(@"    Write-Host ""[ERROR] GAGAL: PC Manajemen ($ManagerIP) TIDAK TERJANGKAU!"" -ForegroundColor Red");
        sb.AppendLine(@"    Write-Host 'Pastikan komputer terhubung ke jaringan LAN laboratorium.' -ForegroundColor Yellow");
        sb.AppendLine(@"    Write-Host '--------------------------------------------------' -ForegroundColor Red");
        sb.AppendLine(@"    exit 1");
        sb.AppendLine(@"}");
        sb.AppendLine(@"Write-Host ""OK: PC Manajemen ($ManagerIP) terverifikasi terhubung."" -ForegroundColor Green");
        sb.AppendLine();
        sb.AppendLine(@"# 2. Persiapkan Direktori Instalasi");
        sb.AppendLine(@"Write-Host ""[2/5] Menyiapkan direktori $InstallDir..."" -ForegroundColor Yellow");
        sb.AppendLine(@"if (-not (Test-Path -Path $InstallDir)) {");
        sb.AppendLine(@"    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null");
        sb.AppendLine(@"}");
        sb.AppendLine();
        sb.AppendLine(@"# Hentikan proses/service lama jika sedang berjalan agar file executable tidak terkunci");
        sb.AppendLine(@"try { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue } catch {}");
        sb.AppendLine(@"try { Get-Process -Name 'LabSupport.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}");
        sb.AppendLine(@"Start-Sleep -Milliseconds 500");
        sb.AppendLine();
        sb.AppendLine(@"# 3. Download Binary Agent dari PC Manajemen (Fast Download)");
        sb.AppendLine(@"$agentExe = Join-Path $InstallDir 'LabSupport.Agent.exe'");
        sb.AppendLine(@"$downloadUrl = ""http://${ManagerIP}:${ManagerPort}/agent/download""");
        sb.AppendLine(@"Write-Host ""[3/5] Mengunduh binary agent dari $downloadUrl..."" -ForegroundColor Yellow");
        sb.AppendLine(@"$scriptDir = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) { $PSScriptRoot } else { (Get-Location).Path }");
        sb.AppendLine(@"$localCopy = Join-Path $scriptDir 'LabSupport.Agent.exe'");
        sb.AppendLine(@"if (Test-Path $localCopy) {");
        sb.AppendLine(@"    Write-Host 'Menemukan binary lokal di USB/Folder. Menggunakan file lokal...' -ForegroundColor Cyan");
        sb.AppendLine(@"    Copy-Item -Path $localCopy -Destination $agentExe -Force");
        sb.AppendLine(@"} else {");
        sb.AppendLine(@"    try {");
        sb.AppendLine(@"        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12");
        sb.AppendLine(@"        $webClient = New-Object System.Net.WebClient");
        sb.AppendLine(@"        $webClient.DownloadFile($downloadUrl, $agentExe)");
        sb.AppendLine(@"    } catch {");
        sb.AppendLine(@"        try {");
        sb.AppendLine(@"            Invoke-WebRequest -Uri $downloadUrl -OutFile $agentExe -UseBasicParsing");
        sb.AppendLine(@"        } catch {");
        sb.AppendLine(@"            Write-Host ""[ERROR] Gagal mengunduh agent: $_"" -ForegroundColor Red");
        sb.AppendLine(@"            exit 1");
        sb.AppendLine(@"        }");
        sb.AppendLine(@"    }");
        sb.AppendLine(@"}");
        sb.AppendLine();
        sb.AppendLine(@"# 4. Konfigurasi Windows Firewall & Wake-on-LAN (WoL)");
        sb.AppendLine(@"Write-Host '[4/5] Mengonfigurasi Windows Firewall & Wake-on-LAN...' -ForegroundColor Yellow");
        sb.AppendLine(@"try {");
        sb.AppendLine(@"    netsh advfirewall firewall add rule name=""LabSupport Agent Program"" dir=in action=allow program=""$agentExe"" enable=yes profile=any | Out-Null");
        sb.AppendLine(@"    netsh advfirewall firewall add rule name=""LabSupport Agent Ports"" dir=in action=allow protocol=TCP localport=5901,5902 enable=yes profile=any | Out-Null");
        sb.AppendLine(@"    netsh advfirewall firewall add rule name=""LabSupport Discovery Inbound"" dir=in action=allow protocol=UDP localport=5903 enable=yes profile=any | Out-Null");
        sb.AppendLine(@"    netsh advfirewall firewall add rule name=""LabSupport WoL Inbound"" dir=in action=allow protocol=UDP localport=7,9 enable=yes profile=any | Out-Null");
        sb.AppendLine();
        sb.AppendLine(@"    # Nonaktifkan Windows Fast Startup (Hiberboot) agar chip ethernet tetap standby mendengarkan Magic Packet saat shutdown");
        sb.AppendLine(@"    Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name 'HiberbootEnabled' -Value 0 -ErrorAction SilentlyContinue");
        sb.AppendLine();
        sb.AppendLine(@"    # Aktifkan Wake on Magic Packet pada adapter jaringan fisik");
        sb.AppendLine(@"    Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.PhysicalMediaType -match '802.3' -or $_.InterfaceDescription -notmatch 'Virtual|Hyper-V|VMware|Loopback' } | ForEach-Object {");
        sb.AppendLine(@"        Enable-NetAdapterPowerManagement -Name $_.Name -WakeOnMagicPacket -ErrorAction SilentlyContinue");
        sb.AppendLine(@"        Set-NetAdapterPowerManagement -Name $_.Name -WakeOnMagicPacket Enabled -ErrorAction SilentlyContinue");
        sb.AppendLine(@"        Set-NetAdapterAdvancedProperty -Name $_.Name -DisplayName 'Wake on Magic Packet' -DisplayValue 'Enabled' -ErrorAction SilentlyContinue");
        sb.AppendLine(@"        Set-NetAdapterAdvancedProperty -Name $_.Name -RegistryKeyword '*WakeOnMagicPacket' -RegistryValue '1' -ErrorAction SilentlyContinue");
        sb.AppendLine(@"    }");
        sb.AppendLine(@"} catch {}");
        sb.AppendLine();
        sb.AppendLine(@"# 5. Pasang dan Autostart Headless (Windows Service & Scheduled Task)");
        sb.AppendLine(@"Write-Host ""[5/5] Memasang autostart headless untuk $ServiceName..."" -ForegroundColor Yellow");
        sb.AppendLine(@"$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue");
        sb.AppendLine(@"if ($existingService) {");
        sb.AppendLine(@"    Write-Host 'Service sudah ada, memperbarui...' -ForegroundColor Gray");
        sb.AppendLine(@"    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue");
        sb.AppendLine(@"    sc.exe delete ""`""$ServiceName`"""" | Out-Null");
        sb.AppendLine(@"    Start-Sleep -Seconds 1");
        sb.AppendLine(@"}");
        sb.AppendLine();
        // Buat konfigurasi appsettings lokal dengan IP Manager, Lab Pairing Code, dan Kategori Ruang
        sb.AppendLine(@"$configJson = @""");
        sb.AppendLine(@"{");
        sb.AppendLine(@"  ""Manager"": {");
        sb.AppendLine(@"    ""Ip"": ""$ManagerIP"",");
        sb.AppendLine(@"    ""Port"": $ManagerPort,");
        sb.AppendLine(@"    ""ManagerId"": ""$ManagerId"",");
        sb.AppendLine(@"    ""RoomName"": ""$RoomName""");
        sb.AppendLine(@"  },");
        sb.AppendLine(@"  ""Agent"": {");
        sb.AppendLine(@"    ""DisplayName"": ""$assignedDisplayName"",");
        sb.AppendLine(@"    ""CommandPort"": 5902,");
        sb.AppendLine(@"    ""MjpegPort"": 5901,");
        sb.AppendLine(@"    ""HeartbeatIntervalSeconds"": 5");
        sb.AppendLine(@"  }");
        sb.AppendLine(@"}");
        sb.AppendLine(@"""@");
        sb.AppendLine(@"Set-Content -Path (Join-Path $InstallDir 'appsettings.json') -Value $configJson -Encoding UTF8");
        sb.AppendLine();
        sb.AppendLine(@"# Siapkan file uninstaller di direktori instalasi agar teknisi dapat menghapus agent kapan saja");
        sb.AppendLine(@"$uninstBat = Join-Path $InstallDir 'uninstall_agent.bat'");
        sb.AppendLine(@"$uninstScript = @""");
        sb.AppendLine(@"@echo off");
        sb.AppendLine(@"net session >nul 2>&1");
        sb.AppendLine(@"if %errorLevel% neq 0 (");
        sb.AppendLine(@"    powershell -ExecutionPolicy Bypass -Command ""Start-Process cmd.exe -ArgumentList '/c `""%~f0`"" %*' -Verb RunAs""");
        sb.AppendLine(@"    exit /b");
        sb.AppendLine(@")");
        sb.AppendLine(@"echo [1/3] Memberitahu PC Manajemen (Unregister)...");
        sb.AppendLine(@"powershell -ExecutionPolicy Bypass -Command ""try { $mac = (Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Select-Object -First 1).MacAddress; $b = @{ Hostname = [System.Environment]::MachineName; MacAddress = $mac; ManagerId = '$ManagerId' } | ConvertTo-Json; Invoke-RestMethod -Uri 'http://$ManagerIP:$ManagerPort/agent/unregister' -Method Post -Body $b -ContentType 'application/json' -TimeoutSec 3 | Out-Null } catch {}""");
        sb.AppendLine(@"echo [2/3] Menghentikan & menghapus service $ServiceName...");
        sb.AppendLine(@"sc.exe stop ""$ServiceName"" >nul 2>&1");
        sb.AppendLine(@"sc.exe delete ""$ServiceName"" >nul 2>&1");
        sb.AppendLine(@"taskkill.exe /f /im LabSupport.Agent.exe >nul 2>&1");
        sb.AppendLine(@"schtasks.exe /delete /tn ""$ServiceName"" /f >nul 2>&1");
        sb.AppendLine(@"netsh advfirewall firewall delete rule name=""LabSupport Agent Program"" >nul 2>&1");
        sb.AppendLine(@"netsh advfirewall firewall delete rule name=""LabSupport Agent Ports"" >nul 2>&1");
        sb.AppendLine(@"echo [3/3] Menghapus file instalasi...");
        sb.AppendLine(@"echo [SUKSES] LabSupport Agent berhasil di-uninstall.");
        sb.AppendLine(@"start /b cmd /c ""timeout /t 2 >nul & rmdir /s /q `""$InstallDir`""""");
        sb.AppendLine(@"exit /b 0");
        sb.AppendLine(@"""@");
        sb.AppendLine(@"Set-Content -Path $uninstBat -Value $uninstScript -Encoding UTF8");
        sb.AppendLine();
        sb.AppendLine(@"# Register Windows Service dengan startup Automatic");
        sb.AppendLine(@"New-Service -Name $ServiceName `");
        sb.AppendLine(@"            -BinaryPathName ""`""$agentExe`"""" `");
        sb.AppendLine(@"            -DisplayName ""LabSupport Agent Service"" `");
        sb.AppendLine(@"            -Description ""Headless management and remote client for LabSupport."" `");
        sb.AppendLine(@"            -StartupType Automatic | Out-Null");
        sb.AppendLine();
        sb.AppendLine(@"# Register Scheduled Task untuk autostart sesi interaktif pengguna (proses saja, highest privilege)");
        sb.AppendLine(@"try {");
        sb.AppendLine(@"    schtasks.exe /create /tn ""LabSupportAgent"" /tr ""`""$agentExe`"""" /sc onlogon /rl highest /f | Out-Null");
        sb.AppendLine(@"} catch {}");
        sb.AppendLine();
        sb.AppendLine(@"# Jalankan Service jika berhasil");
        sb.AppendLine(@"$svcStarted = $false");
        sb.AppendLine(@"try {");
        sb.AppendLine(@"    Start-Service -Name $ServiceName -ErrorAction Stop");
        sb.AppendLine(@"    $svcStarted = $true");
        sb.AppendLine(@"} catch {}");
        sb.AppendLine();
        sb.AppendLine(@"# Jika Service tidak berjalan di sesi ini, jalankan sebagai proses interaktif headless");
        sb.AppendLine(@"if (-not $svcStarted) {");
        sb.AppendLine(@"    try { Start-Process -FilePath $agentExe -WindowStyle Hidden } catch {}");
        sb.AppendLine(@"}");
        sb.AppendLine();
        sb.AppendLine(@"Write-Host '==================================================' -ForegroundColor Green");
        sb.AppendLine(@"Write-Host ' [SUKSES] Agent berhasil terpasang & autostart!' -ForegroundColor Green");
        sb.AppendLine(@"Write-Host "" Terhubung ke PC Manajemen ($ManagerIP) [Lab: $ManagerId]."" -ForegroundColor Green");
        sb.AppendLine(@"Write-Host '==================================================' -ForegroundColor Green");
        sb.AppendLine(@"Start-Sleep -Seconds 1");
        sb.AppendLine();
        sb.AppendLine(@"return $true");
        sb.AppendLine();

        return sb.ToString();
    }

    public static string GenerateUninstallBatchScript(
        string managerIp,
        int managerPort = 8080,
        string managerId = "LAB-DEFAULT",
        string serviceName = "LabSupportAgent",
        string installDir = @"C:\ProgramData\LabSupport")
    {
        string cleanIp = SanitizeParameter(managerIp, "127.0.0.1");
        string cleanId = SanitizeParameter(managerId, "LAB-DEFAULT");
        string cleanService = SanitizeParameter(serviceName, "LabSupportAgent");
        string cleanDir = SanitizeParameter(installDir, @"C:\ProgramData\LabSupport");
        int cleanPort = Math.Clamp(managerPort, 1, 65535);

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal EnableDelayedExpansion");
        sb.AppendLine();
        sb.AppendLine(":: Auto-Elevate to Administrator");
        sb.AppendLine("net session >nul 2>&1");
        sb.AppendLine("if %errorLevel% neq 0 (");
        sb.AppendLine("    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"Start-Process cmd.exe -ArgumentList '/c `\"%~f0`\" %*' -Verb RunAs\"");
        sb.AppendLine("    exit /b");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("title LabSupport Agent Uninstaller");
        sb.AppendLine("color 0C");
        sb.AppendLine("echo ==================================================");
        sb.AppendLine("echo        LabSupport Agent - Uninstaller             ");
        sb.AppendLine("echo ==================================================");
        sb.AppendLine();
        sb.AppendLine("echo [1/4] Memberitahu PC Manajemen (Unregister)...");
        sb.AppendLine($"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"try {{ $mac = (Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Select-Object -First 1).MacAddress; $body = @{{ Hostname = [System.Environment]::MachineName; MacAddress = $mac; ManagerId = '{cleanId}'; Reason = 'Manual uninstall script' }} | ConvertTo-Json; Invoke-RestMethod -Uri 'http://{cleanIp}:{cleanPort}/agent/unregister' -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 3 | Out-Null; Write-Host 'PC Manajemen berhasil diberitahu.' -ForegroundColor Green }} catch {{ Write-Host 'PC Manajemen tidak merespon (dilewati).' -ForegroundColor Yellow }}\"");
        sb.AppendLine();
        sb.AppendLine($"echo [2/4] Menghentikan & menghapus Windows Service '{cleanService}'...");
        sb.AppendLine($"sc.exe stop \"{cleanService}\" >nul 2>&1");
        sb.AppendLine($"sc.exe delete \"{cleanService}\" >nul 2>&1");
        sb.AppendLine("taskkill.exe /f /im LabSupport.Agent.exe >nul 2>&1");
        sb.AppendLine($"schtasks.exe /delete /tn \"{cleanService}\" /f >nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("echo [3/4] Menghapus aturan Windows Firewall...");
        sb.AppendLine("netsh advfirewall firewall delete rule name=\"LabSupport Agent Program\" >nul 2>&1");
        sb.AppendLine("netsh advfirewall firewall delete rule name=\"LabSupport Agent Ports\" >nul 2>&1");
        sb.AppendLine("netsh advfirewall firewall delete rule name=\"LabSupport Discovery Inbound\" >nul 2>&1");
        sb.AppendLine("netsh advfirewall firewall delete rule name=\"LabSupport WoL Inbound\" >nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("echo [4/4] Menghapus file instalasi...");
        sb.AppendLine("color 0A");
        sb.AppendLine("echo ==================================================");
        sb.AppendLine("echo  [SUKSES] LabSupport Agent BERHASIL DI-UNINSTALL! ");
        sb.AppendLine("echo ==================================================");
        sb.AppendLine("timeout /t 2 >nul");
        sb.AppendLine();
        sb.AppendLine(":: Hapus folder instalasi di background setelah proses batch selesai");
        sb.AppendLine($"start /b cmd /c \"timeout /t 2 >nul & rmdir /s /q \"{cleanDir}\"\"");
        sb.AppendLine("exit /b 0");
        return sb.ToString();
    }

    public static string GenerateBatchScript()
    {
        return GenerateSingleBatchInstaller("127.0.0.1", 8080, "LAB-DEFAULT");
    }

    public static string GenerateSingleBatchInstaller(
        string managerIp,
        int managerPort = 8080,
        string managerId = "LAB-DEFAULT",
        string serviceName = "LabSupportAgent",
        string installDir = @"C:\ProgramData\LabSupport",
        string roomName = "General",
        int targetCapacity = 30)
    {
        string cleanIp = SanitizeParameter(managerIp, "127.0.0.1");
        string cleanId = SanitizeParameter(managerId, "LAB-DEFAULT");
        string cleanRoom = SanitizeParameter(roomName, "General");
        int cleanPort = Math.Clamp(managerPort, 1, 65535);
        int cleanCap = Math.Clamp(targetCapacity, 1, 1000);

        var psPayload = GeneratePowerShellScript(managerIp, managerPort, managerId, serviceName, installDir, roomName, targetCapacity);

        var sb = new StringBuilder();
        sb.AppendLine(@"@echo off");
        sb.AppendLine(@"setlocal EnableDelayedExpansion");
        sb.AppendLine(@"title LabSupport Agent - Standalone Single-File Deployment");
        sb.AppendLine();
        sb.AppendLine(@"REM ========================================================");
        sb.AppendLine(@"REM LabSupport Agent - Automated Standalone Installer");
        sb.AppendLine($@"REM PC Manajemen  : {cleanIp}:{cleanPort}");
        sb.AppendLine($@"REM Pairing Code  : {cleanId}");
        sb.AppendLine($@"REM Kategori Ruang : {cleanRoom} ({cleanCap} PC)");
        sb.AppendLine(@"REM ========================================================");
        sb.AppendLine();
        sb.AppendLine(@"set ""BATCH_PATH=%~f0""");
        sb.AppendLine(@"set ""BATCH_DIR=%~dp0""");
        sb.AppendLine();
        sb.AppendLine(@"REM 1. Verifikasi & Permintaan Hak Akses Administrator (Auto-Elevation)");
        sb.AppendLine(@"net session >nul 2>&1");
        sb.AppendLine(@"if '%errorlevel%' NEQ '0' (");
        sb.AppendLine(@"    echo [INFO] Memerlukan hak Administrator. Meminta elevasi...");
        sb.AppendLine(@"    powershell -NoProfile -ExecutionPolicy Bypass -Command ""Start-Process -FilePath '%~f0' -ArgumentList '%*' -WorkingDirectory '%~dp0' -Verb RunAs""");
        sb.AppendLine(@"    exit /b");
        sb.AppendLine(@")");
        sb.AppendLine();
        sb.AppendLine(@"REM 2. Jalankan Payload PowerShell yang Tersemat di Bawah");
        sb.AppendLine(@"pushd ""%~dp0""");
        sb.AppendLine(@"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^");
        sb.AppendLine(@"    ""$f='%~f0'; $raw=[System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8); $m=':::' + '___LABSUPPORT_POWERSHELL_PAYLOAD___'; $i=$raw.LastIndexOf($m); if($i -ge 0){ $code=$raw.Substring($i+$m.Length); $sb=[ScriptBlock]::Create($code); & $sb %* }""");
        sb.AppendLine(@"set EXIT_CODE=%errorlevel%");
        sb.AppendLine(@"popd");
        sb.AppendLine(@"if %EXIT_CODE% NEQ 0 (");
        sb.AppendLine(@"    echo.");
        sb.AppendLine(@"    echo [ERROR] Instalasi selesai dengan kode error %EXIT_CODE%.");
        sb.AppendLine(@"    pause");
        sb.AppendLine(@")");
        sb.AppendLine(@"exit /b %EXIT_CODE%");
        sb.AppendLine();
        sb.AppendLine(@":::___LABSUPPORT_POWERSHELL_PAYLOAD___");
        sb.Append(psPayload);

        return sb.ToString();
    }

    public static async Task GenerateDeploymentPackageAsync(
        string outputDirectory,
        string managerIp,
        int managerPort = 8080,
        string managerId = "LAB-DEFAULT",
        string serviceName = "LabSupportAgent",
        string? agentSourceBinaryPath = null)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string ps1Content = GeneratePowerShellScript(managerIp, managerPort, managerId, serviceName);
        string batContent = GenerateSingleBatchInstaller(managerIp, managerPort, managerId, serviceName);

        string ps1Path = Path.Combine(outputDirectory, "install_agent.ps1");
        string batPath = Path.Combine(outputDirectory, "install_agent.bat");

        await File.WriteAllTextAsync(ps1Path, ps1Content, Encoding.UTF8);
        await File.WriteAllTextAsync(batPath, batContent, Encoding.UTF8);

        // Resolve candidate paths for LabSupport.Agent.exe
        string? resolvedAgentPath = null;
        var candidates = new List<string?>
        {
            agentSourceBinaryPath,
            Path.Combine(AppContext.BaseDirectory, "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "agent", "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "agent", "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "dist", "agent", "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", "agent", "LabSupport.Agent.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "dist", "agent", "LabSupport.Agent.exe")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            try
            {
                string fn = Path.GetFileName(candidate);
                if (!fn.Equals("LabSupport.Agent.exe", StringComparison.OrdinalIgnoreCase) &&
                    !fn.Equals("LabSupport.Agent", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    resolvedAgentPath = candidate;
                    break;
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(resolvedAgentPath))
        {
            string destExe = Path.Combine(outputDirectory, "LabSupport.Agent.exe");
            File.Copy(resolvedAgentPath, destExe, overwrite: true);
        }
    }

    public static async Task GenerateMultiLabPackageAsync(
        string outputDirectory,
        string managerIp,
        int managerPort,
        string managerId,
        IEnumerable<LabRoom> rooms,
        string serviceName = "LabSupportAgent",
        string? agentSourceBinaryPath = null)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0' })
            .Distinct()
            .ToArray();

        foreach (var room in rooms)
        {
            if (string.IsNullOrWhiteSpace(room.Name)) continue;

            string safeName = string.Join("_", room.Name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries))
                .Replace(" ", "_");

            string batContent = GenerateSingleBatchInstaller(
                managerIp,
                managerPort,
                managerId,
                serviceName,
                @"C:\ProgramData\LabSupport",
                room.Name,
                room.Capacity);

            string batPath = Path.Combine(outputDirectory, $"pasang_agent_{safeName}.bat");
            await File.WriteAllTextAsync(batPath, batContent, Encoding.UTF8);
        }

        // Copy agent binary if found
        string? resolvedAgentPath = null;
        var candidates = new List<string?>
        {
            agentSourceBinaryPath,
            Path.Combine(AppContext.BaseDirectory, "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "agent", "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "agent", "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "dist", "agent", "LabSupport.Agent.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", "agent", "LabSupport.Agent.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "dist", "agent", "LabSupport.Agent.exe")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            try
            {
                string fn = Path.GetFileName(candidate);
                if (!fn.Equals("LabSupport.Agent.exe", StringComparison.OrdinalIgnoreCase) &&
                    !fn.Equals("LabSupport.Agent", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    resolvedAgentPath = candidate;
                    break;
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(resolvedAgentPath))
        {
            string destExe = Path.Combine(outputDirectory, "LabSupport.Agent.exe");
            File.Copy(resolvedAgentPath, destExe, overwrite: true);
        }
    }
}
