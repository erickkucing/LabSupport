using System;
using Microsoft.Win32;

namespace LabSupport.Shared.Services;

public class UsbBlockerService
{
    private const string SubKeyPath = @"SYSTEM\CurrentControlSet\Services\USBSTOR";
    private const string ValueName = "Start";
    private const int StartDisabled = 4; // USB storage blocked
    private const int StartEnabled = 3;  // USB storage allowed

    // In-memory simulation fallback for non-Windows testing
    private static bool _simulatedBlocked = false;

    public virtual bool SetUsbStorageBlocked(bool blocked)
    {
        if (!OperatingSystem.IsWindows())
        {
            _simulatedBlocked = blocked;
            return true;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SubKeyPath, writable: true);
            if (key == null)
            {
                // Key might not exist if USBSTOR driver was never initialized
                using var createKey = Registry.LocalMachine.CreateSubKey(SubKeyPath);
                createKey?.SetValue(ValueName, blocked ? StartDisabled : StartEnabled, RegistryValueKind.DWord);
                return true;
            }

            key.SetValue(ValueName, blocked ? StartDisabled : StartEnabled, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            _simulatedBlocked = blocked;
            return false;
        }
    }

    public virtual bool IsUsbStorageBlocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return _simulatedBlocked;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SubKeyPath, writable: false);
            if (key == null) return false;

            object? val = key.GetValue(ValueName);
            if (val is int intVal)
            {
                return intVal == StartDisabled;
            }
            return false;
        }
        catch
        {
            return _simulatedBlocked;
        }
    }
}
