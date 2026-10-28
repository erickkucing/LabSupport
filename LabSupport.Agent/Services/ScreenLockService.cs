using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LabSupport.Agent.Services;

public class ScreenLockService : IDisposable
{
    private static readonly object _syncLock = new();
    private bool _isLocked;
    private bool _isBlackScreen;
    private Thread? _overlayThread;
    private IntPtr _overlayHwnd = IntPtr.Zero;
    private string _overlayMessage = "PERHATIKAN GURU";

    public bool IsLocked
    {
        get
        {
            lock (_syncLock)
            {
                return _isLocked;
            }
        }
    }

    public bool IsBlackScreen
    {
        get
        {
            lock (_syncLock)
            {
                return _isBlackScreen;
            }
        }
    }

    public string CurrentMessage
    {
        get
        {
            lock (_syncLock)
            {
                return _overlayMessage;
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BlockInput(bool fBlockIt);

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, [In] ref RECT lprc, IntPtr hrbr);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, [In] ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int DrawText(IntPtr hDC, string lpchText, int nCount, ref RECT lpRect, uint uFormat);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint color);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFont(
        int nHeight, int nWidth, int nEscapement, int nOrientation, int fnWeight,
        uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, uint fdwCharSet,
        uint fdwOutputPrecision, uint fdwClipPrecision, uint fdwQuality,
        uint fdwPitchAndFamily, string lpszFace);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const uint WM_PAINT = 0x000F;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_SETCURSOR = 0x0020;
    private const int TRANSPARENT = 1;
    private const uint DT_CENTER = 0x00000001;
    private const uint DT_VCENTER = 0x00000004;
    private const uint DT_WORDBREAK = 0x00000010;
    private const uint DT_SINGLELINE = 0x00000020;

    private static WndProcDelegate? _staticWndProc;

    public virtual bool Lock(string message = "PERHATIKAN GURU")
    {
        lock (_syncLock)
        {
            _isBlackScreen = false;
            _overlayMessage = string.IsNullOrWhiteSpace(message) ? "PERHATIKAN GURU" : message;

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    BlockInput(true);
                }
                catch { }

                if (_overlayThread == null || !_overlayThread.IsAlive)
                {
                    var readyEvent = new ManualResetEvent(false);
                    _overlayThread = new Thread(() => RunOverlayMessageLoop(readyEvent))
                    {
                        IsBackground = true,
                        Name = "ScreenLockOverlayThread"
                    };
                    _overlayThread.SetApartmentState(ApartmentState.STA);
                    _overlayThread.Start();
                    readyEvent.WaitOne(2000);
                }
                else if (_overlayHwnd != IntPtr.Zero)
                {
                    try { ShowCursor(true); } catch { }
                    InvalidateRect(_overlayHwnd, IntPtr.Zero, true);
                    UpdateWindow(_overlayHwnd);
                }
            }

            _isLocked = true;
            return true;
        }
    }

    public virtual bool Unlock()
    {
        lock (_syncLock)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    BlockInput(false);
                }
                catch { }

                try
                {
                    ShowCursor(true);
                }
                catch { }

                if (_overlayHwnd != IntPtr.Zero)
                {
                    PostMessage(_overlayHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    _overlayHwnd = IntPtr.Zero;
                }

                if (_overlayThread != null && _overlayThread.IsAlive)
                {
                    _overlayThread.Join(1500);
                    _overlayThread = null;
                }
            }

            _isBlackScreen = false;
            _isLocked = false;
            return true;
        }
    }

    public virtual bool SetBlackScreen(bool enable)
    {
        lock (_syncLock)
        {
            if (enable)
            {
                _isBlackScreen = true;
                _overlayMessage = string.Empty;

                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        BlockInput(true);
                    }
                    catch { }

                    try
                    {
                        ShowCursor(false);
                    }
                    catch { }

                    if (_overlayThread == null || !_overlayThread.IsAlive)
                    {
                        var readyEvent = new ManualResetEvent(false);
                        _overlayThread = new Thread(() => RunOverlayMessageLoop(readyEvent))
                        {
                            IsBackground = true,
                            Name = "ScreenLockOverlayThread"
                        };
                        _overlayThread.SetApartmentState(ApartmentState.STA);
                        _overlayThread.Start();
                        readyEvent.WaitOne(2000);
                    }
                    else if (_overlayHwnd != IntPtr.Zero)
                    {
                        InvalidateRect(_overlayHwnd, IntPtr.Zero, true);
                        UpdateWindow(_overlayHwnd);
                    }
                }

                _isLocked = true;
                return true;
            }
            else
            {
                return Unlock();
            }
        }
    }

    private void RunOverlayMessageLoop(ManualResetEvent readyEvent)
    {
        string className = "LabSupportAttentionOverlayClass";
        IntPtr hInstance = GetModuleHandle(null);

        _staticWndProc = (hWnd, msg, wParam, lParam) =>
        {
            switch (msg)
            {
                case WM_PAINT:
                    {
                        var hdc = BeginPaint(hWnd, out var ps);
                        GetClientRect(hWnd, out var rect);

                        if (_isBlackScreen)
                        {
                            IntPtr blackBrush = CreateSolidBrush(0x00000000); // Pure pitch black #000000 in BGR
                            FillRect(hdc, ref rect, blackBrush);
                            DeleteObject(blackBrush);
                            EndPaint(hWnd, ref ps);
                            return IntPtr.Zero;
                        }

                        // Draw background
                        IntPtr brush = CreateSolidBrush(0x001A0F0B); // Dark slate blue-gray in BGR (0x0B, 0x0F, 0x1A)
                        FillRect(hdc, ref rect, brush);

                        SetBkMode(hdc, TRANSPARENT);

                        // Draw Title
                        SetTextColor(hdc, 0x00F8BD38); // Cyan / Gold in BGR (0x38, 0xBD, 0xF8)
                        IntPtr titleFont = CreateFont(52, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
                        IntPtr oldFont = SelectObject(hdc, titleFont);

                        var titleRect = rect;
                        titleRect.Bottom = rect.Bottom - 80;
                        DrawText(hdc, _overlayMessage.ToUpperInvariant(), -1, ref titleRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

                        // Draw Subtitle
                        SetTextColor(hdc, 0x00CBD5E1); // Light slate gray in BGR
                        IntPtr subFont = CreateFont(24, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
                        SelectObject(hdc, subFont);

                        var subRect = rect;
                        subRect.Top = rect.Bottom / 2 + 50;
                        subRect.Bottom = rect.Bottom;
                        DrawText(hdc, "Layar & input sedang dikunci oleh guru / pengawas lab.", -1, ref subRect, DT_CENTER | DT_WORDBREAK);

                        SelectObject(hdc, oldFont);
                        DeleteObject(titleFont);
                        DeleteObject(subFont);
                        DeleteObject(brush);

                        EndPaint(hWnd, ref ps);
                        return IntPtr.Zero;
                    }

                case WM_ERASEBKGND:
                    return (IntPtr)1;

                case WM_SETCURSOR:
                    if (_isBlackScreen)
                    {
                        SetCursor(IntPtr.Zero);
                        return (IntPtr)1;
                    }
                    return DefWindowProc(hWnd, msg, wParam, lParam);

                case 0x0006: // WM_ACTIVATE
                case 0x0007: // WM_SETFOCUS
                    SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002); // SWP_NOSIZE | SWP_NOMOVE
                    return IntPtr.Zero;

                case WM_CLOSE:
                    DestroyWindow(hWnd);
                    return IntPtr.Zero;

                case WM_DESTROY:
                    PostQuitMessage(0);
                    return IntPtr.Zero;

                default:
                    return DefWindowProc(hWnd, msg, wParam, lParam);
            }
        };

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0002 | 0x0001, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = _staticWndProc,
            hInstance = hInstance,
            hbrBackground = CreateSolidBrush(0x001A0F0B),
            lpszClassName = className
        };

        RegisterClassEx(ref wc);

        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int cx = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int cy = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (cx <= 0 || cy <= 0)
        {
            x = 0;
            y = 0;
            cx = GetSystemMetrics(SM_CXSCREEN);
            cy = GetSystemMetrics(SM_CYSCREEN);
            if (cx <= 0) cx = 1920;
            if (cy <= 0) cy = 1080;
        }

        _overlayHwnd = CreateWindowEx(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            className,
            "LabSupport_Overlay",
            WS_POPUP | WS_VISIBLE,
            x, y, cx, cy,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_overlayHwnd != IntPtr.Zero)
        {
            SetWindowPos(_overlayHwnd, HWND_TOPMOST, x, y, cx, cy, SWP_SHOWWINDOW);
            ShowWindow(_overlayHwnd, 1);
            UpdateWindow(_overlayHwnd);
        }

        // Ensure Win32 message queue exists before notifying caller
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        readyEvent.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnregisterClass(className, hInstance);
        if (wc.hbrBackground != IntPtr.Zero)
        {
            DeleteObject(wc.hbrBackground);
        }
    }

    public void Dispose()
    {
        Unlock();
    }
}
