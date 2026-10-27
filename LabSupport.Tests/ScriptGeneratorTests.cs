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
        string serviceName = "LabSupportAgent";

        string script = ScriptGenerator.GeneratePowerShellScript(managerIp, port, serviceName);

        Assert.NotNull(script);
        // Requirement 2: Must check ping from PC Agent to Manager to ensure network connectivity
        Assert.Contains("Test-Connection -ComputerName $ManagerIP", script);
        Assert.Contains("192.168.10.25", script);
        Assert.Contains("8080", script);
        Assert.Contains("http://${ManagerIP}:${ManagerPort}/agent/download", script);
        Assert.Contains("New-Service -Name $ServiceName", script);
        Assert.Contains("Start-Service -Name $ServiceName", script);
        Assert.Contains("-StartupType Automatic", script);
        Assert.Contains("exit 1", script);
    }

    [Fact]
    public void GenerateBatchScript_ShouldContainAdminElevationAndBypass()
    {
        string batchScript = ScriptGenerator.GenerateBatchScript();

        Assert.NotNull(batchScript);
        Assert.Contains("getadmin.vbs", batchScript);
        Assert.Contains("-ExecutionPolicy Bypass", batchScript);
        Assert.Contains("install_agent.ps1", batchScript);
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
                "LabSupportAgent");

            string ps1Path = Path.Combine(tempDir, "install_agent.ps1");
            string batPath = Path.Combine(tempDir, "install_agent.bat");

            Assert.True(File.Exists(ps1Path));
            Assert.True(File.Exists(batPath));

            string ps1Content = await File.ReadAllTextAsync(ps1Path);
            Assert.Contains("192.168.1.100", ps1Content);
            Assert.Contains("Test-Connection", ps1Content);
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
