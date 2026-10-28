using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;
using LabSupport.Shared.Services;

namespace LabSupport.Agent.Services;

public static class AgentUninstallerService
{
    private static bool _isUninstalling;
    private static readonly object _lock = new();

    public static void TriggerSelfUninstall(int delayMs = 1500, string? managerIp = null, int managerPort = 8080, string? managerId = null)
    {
        lock (_lock)
        {
            if (_isUninstalling) return;
            _isUninstalling = true;
        }

        Task.Run(async () =>
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }
            await ExecuteDirectUninstallAsync(managerIp, managerPort, managerId);
        });
    }

    public static async Task ExecuteDirectUninstallAsync(string? managerIp = null, int managerPort = 8080, string? managerId = null)
    {
        // 1. Notify Manager server if reachable
        try
        {
            string targetIp = !string.IsNullOrWhiteSpace(managerIp) ? managerIp : "127.0.0.1";
            var unregisterMsg = new UnregisterAgentMessage
            {
                Hostname = Environment.MachineName,
                MacAddress = NetworkHelper.GetDefaultMacAddress(),
                ManagerId = managerId ?? string.Empty,
                Reason = "Agent self-uninstall initiated"
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            string url = $"http://{targetIp}:{managerPort}/agent/unregister";
            string json = MessageSerializer.Serialize(unregisterMsg);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            await http.PostAsync(url, content);
        }
        catch { }

        // 2. Revert any active web filter hosts block
        try
        {
            var webFilter = new WebFilterService();
            webFilter.ClearFilter();
        }
        catch { }

        // 3. Remove Windows Service, Scheduled Task, Firewall & Self-delete on Windows
        if (OperatingSystem.IsWindows())
        {
            try
            {
                string installDir = @"C:\ProgramData\LabSupport";
                string serviceName = "LabSupportAgent";

                // Stop & delete service
                RunProcessSilently("sc.exe", $"stop \"{serviceName}\"");
                RunProcessSilently("sc.exe", $"delete \"{serviceName}\"");
                RunProcessSilently("schtasks.exe", $"/delete /tn \"{serviceName}\" /f");

                // Clean firewall rules
                RunProcessSilently("netsh", "advfirewall firewall delete rule name=\"LabSupport Agent Program\"");
                RunProcessSilently("netsh", "advfirewall firewall delete rule name=\"LabSupport Agent Ports\"");
                RunProcessSilently("netsh", "advfirewall firewall delete rule name=\"LabSupport Discovery Inbound\"");
                RunProcessSilently("netsh", "advfirewall firewall delete rule name=\"LabSupport WoL Inbound\"");

                // Spawn detached cleanup process to remove C:\ProgramData\LabSupport after current process exits
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 2 >nul & rmdir /s /q \"{installDir}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
            catch { }
        }

        // 4. Terminate agent process
        Environment.Exit(0);
    }

    private static void RunProcessSilently(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
        }
        catch { }
    }
}
