using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LabSupport.Shared.Services;

public class ScriptGenerator
{
    public static string GeneratePowerShellScript(string managerIp, int managerPort = 8080, string serviceName = "LabSupportAgent", string installDir = @"C:\LabSupportAgent")
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"# ========================================================");
        sb.AppendLine(@"# LabSupport Agent - Automated Headless Installation Script");
        sb.AppendLine(@"# Generated automatically by LabSupport.Manager");
        sb.AppendLine(@"# ========================================================");
        sb.AppendLine();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        sb.AppendLine($"$ManagerIP    = \"{managerIp}\"");
        sb.AppendLine($"$ManagerPort  = {managerPort}");
        sb.AppendLine($"$ServiceName  = \"{serviceName}\"");
        sb.AppendLine($"$InstallDir   = \"{installDir}\"");
        sb.AppendLine();
        sb.AppendLine(@"Write-Host '==================================================' -ForegroundColor Cyan");
        sb.AppendLine(@"Write-Host '         LabSupport Agent Installer              ' -ForegroundColor Cyan");
        sb.AppendLine(@"Write-Host '==================================================' -ForegroundColor Cyan");
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
        sb.AppendLine(@"    Read-Host 'Tekan Enter untuk keluar'");
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
        sb.AppendLine(@"# 3. Download Binary Agent dari PC Manajemen (Fast Download)");
        sb.AppendLine(@"$agentExe = Join-Path $InstallDir 'LabSupport.Agent.exe'");
        sb.AppendLine(@"$downloadUrl = ""http://${ManagerIP}:${ManagerPort}/agent/download""");
        sb.AppendLine(@"Write-Host ""[3/5] Mengunduh binary agent dari $downloadUrl..."" -ForegroundColor Yellow");
        sb.AppendLine(@"$localCopy = Join-Path $PSScriptRoot 'LabSupport.Agent.exe'");
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
        sb.AppendLine(@"            Read-Host 'Tekan Enter untuk keluar'");
        sb.AppendLine(@"            exit 1");
        sb.AppendLine(@"        }");
        sb.AppendLine(@"    }");
        sb.AppendLine(@"}");
        sb.AppendLine();
        sb.AppendLine(@"# 4. Konfigurasi Windows Firewall untuk Port Agent (5901 MJPEG, 5902 TCP)");
        sb.AppendLine(@"Write-Host '[4/5] Mengonfigurasi Windows Firewall...' -ForegroundColor Yellow");
        sb.AppendLine(@"try {");
        sb.AppendLine(@"    netsh advfirewall firewall add rule name=""LabSupport Agent Program"" dir=in action=allow program=""$agentExe"" enable=yes profile=any | Out-Null");
        sb.AppendLine(@"    netsh advfirewall firewall add rule name=""LabSupport Agent Ports"" dir=in action=allow protocol=TCP localport=5901,5902 enable=yes profile=any | Out-Null");
        sb.AppendLine(@"} catch {}");
        sb.AppendLine();
        sb.AppendLine(@"# 5. Pasang dan Autostart Headless (Windows Service & Scheduled Task)");
        sb.AppendLine(@"Write-Host ""[5/5] Memasang autostart headless untuk $ServiceName..."" -ForegroundColor Yellow");
        sb.AppendLine(@"$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue");
        sb.AppendLine(@"if ($existingService) {");
        sb.AppendLine(@"    Write-Host 'Service sudah ada, memperbarui...' -ForegroundColor Gray");
        sb.AppendLine(@"    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue");
        sb.AppendLine(@"    sc.exe delete $ServiceName | Out-Null");
        sb.AppendLine(@"    Start-Sleep -Seconds 1");
        sb.AppendLine(@"}");
        sb.AppendLine();
        sb.AppendLine(@"# Buat konfigurasi appsettings lokal dengan IP Manager");
        sb.AppendLine(@"$configJson = @""");
        sb.AppendLine(@"{");
        sb.AppendLine(@"  ""Manager"": {");
        sb.AppendLine(@"    ""Ip"": ""$ManagerIP"",");
        sb.AppendLine(@"    ""Port"": $ManagerPort");
        sb.AppendLine(@"  },");
        sb.AppendLine(@"  ""Agent"": {");
        sb.AppendLine(@"    ""CommandPort"": 5902,");
        sb.AppendLine(@"    ""MjpegPort"": 5901,");
        sb.AppendLine(@"    ""HeartbeatIntervalSeconds"": 5");
        sb.AppendLine(@"  }");
        sb.AppendLine(@"}");
        sb.AppendLine(@"""@");
        sb.AppendLine(@"Set-Content -Path (Join-Path $InstallDir 'appsettings.json') -Value $configJson -Encoding UTF8");
        sb.AppendLine();
        sb.AppendLine(@"# Register Windows Service dengan startup Automatic");
        sb.AppendLine(@"New-Service -Name $ServiceName `");
        sb.AppendLine(@"            -BinaryPathName ""$agentExe"" `");
        sb.AppendLine(@"            -DisplayName ""LabSupport Agent Service"" `");
        sb.AppendLine(@"            -Description ""Headless management and remote client for LabSupport."" `");
        sb.AppendLine(@"            -StartupType Automatic | Out-Null");
        sb.AppendLine();
        sb.AppendLine(@"# Register Scheduled Task untuk autostart sesi interaktif pengguna (proses saja, highest privilege)");
        sb.AppendLine(@"try {");
        sb.AppendLine(@"    schtasks.exe /create /tn ""LabSupportAgent"" /tr ""`""$agentExe`"""" /sc onlogon /rl highest /f | Out-Null");
        sb.AppendLine(@"} catch {}");
        sb.AppendLine();
        sb.AppendLine(@"# Jalankan Service");
        sb.AppendLine(@"try { Start-Service -Name $ServiceName -ErrorAction SilentlyContinue } catch {}");
        sb.AppendLine();
        sb.AppendLine(@"# Jalankan juga proses interaktif headless langsung");
        sb.AppendLine(@"try { Start-Process -FilePath $agentExe -WindowStyle Hidden } catch {}");
        sb.AppendLine();
        sb.AppendLine(@"Write-Host '==================================================' -ForegroundColor Green");
        sb.AppendLine(@"Write-Host ' [SUKSES] Agent berhasil terpasang & autostart!' -ForegroundColor Green");
        sb.AppendLine(@"Write-Host ' Komputer ini sekarang terhubung ke PC Manajemen.' -ForegroundColor Green");
        sb.AppendLine(@"Write-Host '==================================================' -ForegroundColor Green");
        sb.AppendLine(@"Start-Sleep -Seconds 3");

        return sb.ToString();
    }

    public static string GenerateBatchScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"@echo off");
        sb.AppendLine(@"REM ========================================================");
        sb.AppendLine(@"REM LabSupport Agent Launcher (Run as Administrator)");
        sb.AppendLine(@"REM ========================================================");
        sb.AppendLine(@"setlocal EnableDelayedExpansion");
        sb.AppendLine();
        sb.AppendLine(@"REM Cek apakah sudah Administrator menggunakan net session");
        sb.AppendLine(@"net session >nul 2>&1");
        sb.AppendLine(@"if '%errorlevel%' == '0' goto gotAdmin");
        sb.AppendLine();
        sb.AppendLine(@">nul 2>&1 ""%SYSTEMROOT%\system32\cacls.exe"" ""%SYSTEMROOT%\system32\config\system""");
        sb.AppendLine(@"if '%errorlevel%' NEQ '0' (");
        sb.AppendLine(@"    echo Meminta hak akses Administrator...");
        sb.AppendLine(@"    goto UACPrompt");
        sb.AppendLine(@") else ( goto gotAdmin )");
        sb.AppendLine();
        sb.AppendLine(@":UACPrompt");
        sb.AppendLine(@"    echo Set UAC = CreateObject^(""Shell.Application""^) > ""%temp%\getadmin.vbs""");
        sb.AppendLine(@"    echo UAC.ShellExecute ""cmd.exe"", ""/c """"%~s0"""" "", """", ""runas"", 1 >> ""%temp%\getadmin.vbs""");
        sb.AppendLine(@"    ""%temp%\getadmin.vbs""");
        sb.AppendLine(@"    del ""%temp%\getadmin.vbs""");
        sb.AppendLine(@"    exit /B");
        sb.AppendLine();
        sb.AppendLine(@":gotAdmin");
        sb.AppendLine(@"    pushd ""%~dp0""");
        sb.AppendLine(@"    echo ========================================================");
        sb.AppendLine(@"    echo  LabSupport Agent Installer (Running as Administrator)");
        sb.AppendLine(@"    echo ========================================================");
        sb.AppendLine(@"    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%~dp0install_agent.ps1""");
        sb.AppendLine(@"    popd");
        sb.AppendLine(@"    pause");
        return sb.ToString();
    }

    public static async Task GenerateDeploymentPackageAsync(
        string outputDirectory,
        string managerIp,
        int managerPort = 8080,
        string serviceName = "LabSupportAgent",
        string? agentSourceBinaryPath = null)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string ps1Content = GeneratePowerShellScript(managerIp, managerPort, serviceName);
        string batContent = GenerateBatchScript();

        string ps1Path = Path.Combine(outputDirectory, "install_agent.ps1");
        string batPath = Path.Combine(outputDirectory, "install_agent.bat");

        await File.WriteAllTextAsync(ps1Path, ps1Content, Encoding.UTF8);
        await File.WriteAllTextAsync(batPath, batContent, Encoding.Default);

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
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                resolvedAgentPath = candidate;
                break;
            }
        }

        if (!string.IsNullOrEmpty(resolvedAgentPath))
        {
            string destExe = Path.Combine(outputDirectory, "LabSupport.Agent.exe");
            File.Copy(resolvedAgentPath, destExe, overwrite: true);
        }
    }
}
