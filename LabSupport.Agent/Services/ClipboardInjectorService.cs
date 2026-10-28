using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LabSupport.Agent.Services;

public class ClipboardInjectorService
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    // Memory buffer for non-Windows / test environments
    private string _lastSetText = string.Empty;
    public string LastSetText => _lastSetText;

    public bool SetClipboardText(string text)
    {
        _lastSetText = text ?? string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        bool success = false;
        var staThread = new Thread(() =>
        {
            success = SetClipboardWindows(text ?? string.Empty);
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();
        staThread.Join(3000);

        return success;
    }

    private static bool SetClipboardWindows(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    if (!EmptyClipboard())
                    {
                        CloseClipboard();
                        return false;
                    }

                    int byteCount = (text.Length + 1) * sizeof(char);
                    IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
                    if (hGlobal == IntPtr.Zero)
                    {
                        CloseClipboard();
                        return false;
                    }

                    IntPtr target = GlobalLock(hGlobal);
                    if (target == IntPtr.Zero)
                    {
                        CloseClipboard();
                        return false;
                    }

                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    Marshal.WriteInt16(target + (text.Length * sizeof(char)), 0); // Null terminator
                    GlobalUnlock(hGlobal);

                    if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                    {
                        CloseClipboard();
                        return false;
                    }

                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(50);
        }

        return false;
    }
}
