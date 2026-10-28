using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class ScriptGeneratorTests
{
    [Fact]
    public void GeneratePowerShellScript_ShouldContainRequiredPingAndInstallLogic()
    {
        string managerIp = "192.168.10.25";
        int port = 8080;
        string managerId = "LAB-NET-1";
        string serviceName = "LabSupportAgent";

        string script = ScriptGenerator.GeneratePowerShellScript(managerIp, port, managerId, serviceName);

        Assert.NotNull(script);
        // Requirement 2: Must check ping from PC Agent to Manager to ensure network connectivity
        Assert.Contains("Test-Connection -ComputerName $ManagerIP", script);
        Assert.Contains("192.168.10.25", script);
        Assert.Contains("8080", script);
        Assert.Contains("LAB-NET-1", script);
        Assert.Contains("http://${ManagerIP}:${ManagerPort}/agent/download", script);
        Assert.Contains("New-Service -Name $ServiceName", script);
        Assert.Contains("Start-Service -Name $ServiceName", script);
        Assert.Contains("-StartupType Automatic", script);
        Assert.Contains("schtasks.exe /create", script);
        Assert.Contains("netsh advfirewall firewall add rule", script);
        Assert.Contains("5901,5902", script);
        Assert.Contains("exit 1", script);
    }

    [Fact]
    public void GenerateSingleBatchInstaller_ShouldContainAdminElevationAndEmbeddedPayload()
    {
        string batchScript = ScriptGenerator.GenerateSingleBatchInstaller("192.168.1.50", 8080, "LAB-ROOM-A");

        Assert.NotNull(batchScript);
        Assert.Contains("net session", batchScript);
        Assert.Contains("Start-Process -FilePath '%~f0'", batchScript);
        Assert.Contains("-Verb RunAs", batchScript);
        Assert.Contains("___LABSUPPORT_POWERSHELL_PAYLOAD___", batchScript);
        Assert.Contains("192.168.1.50", batchScript);
        Assert.Contains("LAB-ROOM-A", batchScript);
    }

    [Fact]
    public void GenerateSingleBatchInstaller_WithCustomConfig_ShouldEmbedAllParameters()
    {
        string batchScript = ScriptGenerator.GenerateSingleBatchInstaller(
            "10.0.0.12",
            9090,
            "LAB-SEC-77",
            "CustomLabService",
            @"D:\CustomAgent");

        Assert.NotNull(batchScript);
        Assert.Contains("10.0.0.12", batchScript);
        Assert.Contains("9090", batchScript);
        Assert.Contains("LAB-SEC-77", batchScript);
        Assert.Contains("CustomLabService", batchScript);
        Assert.Contains(@"D:\CustomAgent", batchScript);
    }

    [Fact]
    public async Task GenerateDeploymentPackage_ShouldCreateFilesOnDisk()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"lab_test_deploy_{Guid.NewGuid():N}");

        try
        {
            await ScriptGenerator.GenerateDeploymentPackageAsync(
                tempDir,
                "192.168.1.100",
                8080,
                "LAB-UUID-99",
                "LabSupportAgent");

            string ps1Path = Path.Combine(tempDir, "install_agent.ps1");
            string batPath = Path.Combine(tempDir, "install_agent.bat");

            Assert.True(File.Exists(ps1Path));
            Assert.True(File.Exists(batPath));

            string batContent = await File.ReadAllTextAsync(batPath);
            Assert.Contains("192.168.1.100", batContent);
            Assert.Contains("LAB-UUID-99", batContent);
            Assert.Contains("Test-Connection", batContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void GeneratePowerShellScript_ShouldStopServicesAndProcessesBeforeDownload()
    {
        string script = ScriptGenerator.GeneratePowerShellScript("192.168.1.1", 8080, "LAB-1", "LabSupportAgent");

        int stopServiceIndex = script.IndexOf("Stop-Service -Name $ServiceName");
        int stopProcessIndex = script.IndexOf("Stop-Process -Force");
        int downloadIndex = script.IndexOf("Mengunduh binary agent");

        Assert.True(stopServiceIndex > 0, "Stop-Service should be present");
        Assert.True(stopProcessIndex > 0, "Stop-Process should be present");
        Assert.True(downloadIndex > 0, "Download step should be present");
        Assert.True(stopServiceIndex < downloadIndex, "Service and processes must be stopped before download to prevent file-lock");
        Assert.True(stopProcessIndex < downloadIndex, "Process must be stopped before download to prevent file-lock");
    }

    [Fact]
    public void GeneratePowerShellScript_ShouldHandleNullPSScriptRootFallback()
    {
        string script = ScriptGenerator.GeneratePowerShellScript("192.168.1.1", 8080, "LAB-1", "LabSupportAgent");

        Assert.Contains("if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot))", script);
        Assert.Contains("(Get-Location).Path", script);
    }

    [Fact]
    public void GenerateSingleBatchInstaller_ShouldUseBatchPathEnvVariable()
    {
        string bat = ScriptGenerator.GenerateSingleBatchInstaller("10.10.10.10", 8080, "LAB-TEST");

        Assert.Contains("set \"BATCH_PATH=%~f0\"", bat);
        Assert.Contains("set \"BATCH_DIR=%~dp0\"", bat);
        Assert.Contains("Start-Process -FilePath '%~f0'", bat);
        Assert.Contains("-ArgumentList '%*'", bat);
    }

    [Fact]
    public void GenerateSingleBatchInstaller_WithRoomName_EmbedsRoomAndZeroInputLogic()
    {
        string bat = ScriptGenerator.GenerateSingleBatchInstaller(
            "192.168.1.100",
            8080,
            "LAB-MGR-01",
            "LabSupportAgent",
            @"C:\ProgramData\LabSupport",
            "Lab 209A",
            30);

        Assert.NotNull(bat);
        Assert.Contains("Lab 209A", bat);
        Assert.Contains("30 PC", bat);
        Assert.DoesNotContain("Read-Host", bat);
        Assert.Contains("Zero Input / One-Click Deploy", bat);
        Assert.Contains("\"RoomName\": \"$RoomName\"", bat);
        Assert.Contains("\"DisplayName\": \"$assignedDisplayName\"", bat);
    }

    [Fact]
    public void GeneratePowerShellScript_ZeroInput_ContainsNoReadHostPrompts()
    {
        string script = ScriptGenerator.GeneratePowerShellScript("192.168.1.10", 8080, "LAB-TEST");
        Assert.DoesNotContain("Read-Host", script);
        Assert.Contains("[System.Environment]::MachineName", script);
        Assert.Contains("Start-Sleep -Seconds 1", script);
    }

    [Fact]
    public async Task GenerateMultiLabPackageAsync_CreatesSeparateBatFilesPerRoom()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"lab_multi_test_{Guid.NewGuid():N}");
        try
        {
            var rooms = new[]
            {
                new LabSupport.Shared.Models.LabRoom { Name = "Lab 209A", Capacity = 30 },
                new LabSupport.Shared.Models.LabRoom { Name = "Lab 209B", Capacity = 20 }
            };

            await ScriptGenerator.GenerateMultiLabPackageAsync(
                tempDir,
                "192.168.1.50",
                8080,
                "LAB-MGR-MASTER",
                rooms);

            string bat209A = Path.Combine(tempDir, "pasang_agent_Lab_209A.bat");
            string bat209B = Path.Combine(tempDir, "pasang_agent_Lab_209B.bat");

            Assert.True(File.Exists(bat209A), "File pasang_agent_Lab_209A.bat should be created");
            Assert.True(File.Exists(bat209B), "File pasang_agent_Lab_209B.bat should be created");

            string contentA = await File.ReadAllTextAsync(bat209A);
            Assert.Contains("Lab 209A", contentA);
            Assert.Contains("30 PC", contentA);
            Assert.Contains("LAB-MGR-MASTER", contentA);

            string contentB = await File.ReadAllTextAsync(bat209B);
            Assert.Contains("Lab 209B", contentB);
            Assert.Contains("20 PC", contentB);
            Assert.Contains("LAB-MGR-MASTER", contentB);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
