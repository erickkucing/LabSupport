using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LabSupport.Agent.Services;

public class PowerManager
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    public virtual bool Shutdown()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo("shutdown.exe", "/s /t 0 /f")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public virtual bool Restart()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo("shutdown.exe", "/r /t 0 /f")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public virtual bool LockScreen()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return LockWorkStation();
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
