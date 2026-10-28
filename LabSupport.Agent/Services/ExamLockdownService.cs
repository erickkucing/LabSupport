using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace LabSupport.Agent.Services;

public class ExamLockdownService : IDisposable
{
    private static readonly object _syncLock = new();
    private bool _isExamModeActive;
    private IntPtr _hookId = IntPtr.Zero;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private HookProc? _hookProcDelegate; // Prevent GC collection

    public bool IsExamModeActive
    {
        get
        {
            lock (_syncLock)
            {
                return _isExamModeActive;
            }
        }
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_QUIT = 0x0012;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_APPS = 0x5D;
    private const int VK_F4 = 0x73;

    private const uint LLKHF_ALTDOWN = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public bool EnableExamMode()
    {
        lock (_syncLock)
        {
            if (_isExamModeActive) return true;

            if (OperatingSystem.IsWindows())
            {
                SetTaskMgrDisabled(true);

                var readyEvent = new ManualResetEvent(false);
                _hookThread = new Thread(() => RunHookLoop(readyEvent))
                {
                    IsBackground = true,
                    Name = "ExamLockdownHookThread"
                };
                _hookThread.Start();
                readyEvent.WaitOne(2000);
            }

            _isExamModeActive = true;
            return true;
        }
    }

    public bool DisableExamMode()
    {
        lock (_syncLock)
        {
            if (!_isExamModeActive) return true;

            if (OperatingSystem.IsWindows())
            {
                SetTaskMgrDisabled(false);

                if (_hookThreadId != 0)
                {
                    PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                }

                if (_hookThread != null && _hookThread.IsAlive)
                {
                    _hookThread.Join(1500);
                    _hookThread = null;
                }
            }

            _isExamModeActive = false;
            return true;
        }
    }

    private void RunHookLoop(ManualResetEvent readyEvent)
    {
        _hookThreadId = GetCurrentThreadId();
        _hookProcDelegate = HookCallback;

        IntPtr modHandle = IntPtr.Zero;
        try
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            if (curModule != null) modHandle = GetModuleHandle(curModule.ModuleName);
        }
        catch { }
        if (modHandle == IntPtr.Zero) modHandle = GetModuleHandle(null);

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProcDelegate, modHandle, 0);

        // Initialize Win32 message queue before signalling caller
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        readyEvent.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _hookThreadId = 0;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbd.vkCode;
            bool altDown = (kbd.flags & LLKHF_ALTDOWN) != 0;
            bool ctrlDown = (GetKeyState(VK_CONTROL) & 0x8000) != 0;

            // Block Windows Keys & Context Menu Key
            if (vk == VK_LWIN || vk == VK_RWIN || vk == VK_APPS)
            {
                return (IntPtr)1;
            }

            // Block Alt+Tab
            if (vk == VK_TAB && altDown)
            {
                return (IntPtr)1;
            }

            // Block Alt+F4
            if (vk == VK_F4 && altDown)
            {
                return (IntPtr)1;
            }

            // Block Alt+Esc, Ctrl+Esc, Ctrl+Shift+Esc
            if (vk == VK_ESCAPE && (altDown || ctrlDown))
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void SetTaskMgrDisabled(bool disabled)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using var cuKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            if (cuKey != null)
            {
                if (disabled)
                {
                    cuKey.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                }
                else
                {
                    cuKey.DeleteValue("DisableTaskMgr", false);
                }
            }
        }
        catch { }

        try
        {
            using var lmKey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            if (lmKey != null)
            {
                if (disabled)
                {
                    lmKey.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                }
                else
                {
                    lmKey.DeleteValue("DisableTaskMgr", false);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        DisableExamMode();
    }
}
