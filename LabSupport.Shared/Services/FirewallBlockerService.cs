using System;
using System.Diagnostics;

namespace LabSupport.Shared.Services;

public class FirewallBlockerService
{
    public const string RuleName = "LabSupport_Block_Internet";
    public const string PublicRanges = "0.0.0.0-9.255.255.255,11.0.0.0-172.15.255.255,172.32.0.0-192.167.255.255,192.169.0.0-255.255.255.255";

    // In-memory simulation fallback for non-Windows testing
    private static bool _simulatedBlocked = false;

    public virtual bool SetInternetBlocked(bool block)
    {
        if (!OperatingSystem.IsWindows())
        {
            _simulatedBlocked = block;
            return true;
        }

        try
        {
            // First remove existing rule if present
            RemoveFirewallRule();

            if (block)
            {
                // Add outbound block rule for public 80/443 excluding LAN ranges
                string args = $"advfirewall firewall add rule name=\"{RuleName}\" dir=out action=block protocol=TCP remoteport=80,443 remoteip=\"{PublicRanges}\"";
                var psi = new ProcessStartInfo("netsh", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
                bool success = proc != null && proc.ExitCode == 0;

                // Fallback for Windows editions that reject 0.0.0.0 in remoteip
                if (!success)
                {
                    string safeRanges = "1.0.0.0-9.255.255.255,11.0.0.0-172.15.255.255,172.32.0.0-192.167.255.255,192.169.0.0-255.255.255.254";
                    string fallbackArgs = $"advfirewall firewall add rule name=\"{RuleName}\" dir=out action=block protocol=TCP remoteport=80,443 remoteip=\"{safeRanges}\"";
                    var fallbackPsi = new ProcessStartInfo("netsh", fallbackArgs)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var fallbackProc = Process.Start(fallbackPsi);
                    fallbackProc?.WaitForExit(5000);
                    success = fallbackProc != null && fallbackProc.ExitCode == 0;
                }

                _simulatedBlocked = success;
                return success;
            }
            else
            {
                _simulatedBlocked = false;
                return true;
            }
        }
        catch
        {
            _simulatedBlocked = block;
            return false;
        }
    }

    public virtual bool IsInternetBlocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return _simulatedBlocked;
        }

        try
        {
            string args = $"advfirewall firewall show rule name=\"{RuleName}\"";
            var psi = new ProcessStartInfo("netsh", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            return proc.ExitCode == 0 && output.Contains(RuleName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return _simulatedBlocked;
        }
    }

    private static void RemoveFirewallRule()
    {
        try
        {
            string args = $"advfirewall firewall delete rule name=\"{RuleName}\"";
            var psi = new ProcessStartInfo("netsh", args)
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
