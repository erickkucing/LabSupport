using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LabSupport.Shared.Protocol;

namespace LabSupport.Agent.Services;

public class NotificationOverlayService : IDisposable
{
    private static readonly object _syncLock = new();
    private Thread? _overlayThread;
    private IntPtr _overlayHwnd = IntPtr.Zero;
    private CancellationTokenSource? _autoCloseCts;
    private NotificationPayload? _currentPayload;
    private int _remainingSeconds;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true)]
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
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, [In] ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
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

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool WTSSendMessage(
        IntPtr hServer,
        uint SessionId,
        string pTitle,
        int TitleLength,
        string pMessage,
        int MessageLength,
        uint Style,
        uint Timeout,
        out uint pResponse,
        bool bWait);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

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
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

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
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_KEYDOWN = 0x0100;
    private const int TRANSPARENT = 1;
    private const uint DT_CENTER = 0x00000001;
    private const uint DT_VCENTER = 0x00000004;
    private const uint DT_WORDBREAK = 0x00000010;
    private const uint DT_SINGLELINE = 0x00000020;

    private static WndProcDelegate? _staticWndProc;

    public bool ShowNotification(NotificationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        lock (_syncLock)
        {
            _currentPayload = payload;
            _remainingSeconds = payload.DurationSeconds;

            if (!OperatingSystem.IsWindows())
            {
                // Headless test or non-windows environment
                return true;
            }

            try
            {
                CloseWindowInternal();

                _autoCloseCts?.Cancel();
                _autoCloseCts = new CancellationTokenSource();
                var ct = _autoCloseCts.Token;

                var readyEvent = new ManualResetEvent(false);
                _overlayThread = new Thread(() => RunNotificationMessageLoop(readyEvent, ct))
                {
                    IsBackground = true,
                    Name = "LabSupportNotificationOverlayThread"
                };
                _overlayThread.SetApartmentState(ApartmentState.STA);
                _overlayThread.Start();

                bool ready = readyEvent.WaitOne(2000);
                if (!ready)
                {
                    // Fallback to WTSSendMessage if desktop overlay cannot be initialized
                    TryWtsFallback(payload);
                }

                if (payload.DurationSeconds > 0)
                {
                    StartCountdownTimer(payload.DurationSeconds, ct);
                }

                return true;
            }
            catch
            {
                TryWtsFallback(payload);
                return true;
            }
        }
    }

    private void StartCountdownTimer(int totalSeconds, CancellationToken ct)
    {
        Task.Run(async () =>
        {
            try
            {
                for (int s = totalSeconds; s > 0; s--)
                {
                    if (ct.IsCancellationRequested) return;
                    _remainingSeconds = s;

                    if (_overlayHwnd != IntPtr.Zero)
                    {
                        InvalidateRect(_overlayHwnd, IntPtr.Zero, false);
                    }

                    await Task.Delay(1000, ct);
                }

                if (!ct.IsCancellationRequested)
                {
                    Close();
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }, ct);
    }

    public void Close()
    {
        lock (_syncLock)
        {
            _autoCloseCts?.Cancel();
            CloseWindowInternal();
        }
    }

    private void CloseWindowInternal()
    {
        if (_overlayHwnd != IntPtr.Zero)
        {
            PostMessage(_overlayHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            _overlayHwnd = IntPtr.Zero;
        }

        if (_overlayThread != null && _overlayThread.IsAlive)
        {
            _overlayThread.Join(1000);
            _overlayThread = null;
        }
    }

    private void RunNotificationMessageLoop(ManualResetEvent readyEvent, CancellationToken ct)
    {
        string className = "LabSupportNotificationClass";
        IntPtr hInstance = GetModuleHandle(null);

        _staticWndProc = (hWnd, msg, wParam, lParam) =>
        {
            switch (msg)
            {
                case WM_PAINT:
                    {
                        var hdc = BeginPaint(hWnd, out var ps);
                        GetClientRect(hWnd, out var rect);

                        var payload = _currentPayload ?? new NotificationPayload();

                        // 1. Fill Dark Sleek Background (Fluent dark slate #0F172A)
                        IntPtr bgBrush = CreateSolidBrush(0x002A170F);
                        FillRect(hdc, ref rect, bgBrush);
                        DeleteObject(bgBrush);

                        SetBkMode(hdc, TRANSPARENT);

                        // 2. Accent Top Header Bar
                        uint accentColorBgr = payload.Type switch
                        {
                            NotificationType.Warning => 0x000B9EF5,        // Amber / Orange
                            NotificationType.Urgent => 0x004444EF,         // Crimson / Red
                            NotificationType.ConnectionCheck => 0x0081B910,// Emerald Green
                            _ => 0x00E9A50E                                // Sky Blue (Info)
                        };

                        IntPtr accentBrush = CreateSolidBrush(accentColorBgr);
                        var headerRect = new RECT { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Top + 48 };
                        FillRect(hdc, ref headerRect, accentBrush);
                        DeleteObject(accentBrush);

                        // Header Icon & Title Badge
                        SetTextColor(hdc, 0x00FFFFFF);
                        IntPtr headerFont = CreateFont(22, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
                        IntPtr oldFont = SelectObject(hdc, headerFont);

                        string headerPrefix = payload.Type switch
                        {
                            NotificationType.Warning => "[!] PERINGATAN GURU",
                            NotificationType.Urgent => "[!] PENGUMUMAN PENTING",
                            NotificationType.ConnectionCheck => "[v] LABSUPPORT: TERHUBUNG",
                            _ => "[i] INFORMASI LAB"
                        };

                        string headerText = string.IsNullOrWhiteSpace(payload.Title)
                            ? headerPrefix
                            : $"{headerPrefix} : {payload.Title.ToUpperInvariant()}";

                        DrawText(hdc, headerText, -1, ref headerRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

                        // 3. Message Body
                        SetTextColor(hdc, 0x00F8FAFC); // Crisp Light Slate White
                        IntPtr msgFont = CreateFont(20, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
                        SelectObject(hdc, msgFont);

                        int msgTop = rect.Top + 60;
                        int msgBottom = payload.ShowHostInfo ? rect.Bottom - 65 : rect.Bottom - 40;
                        var msgRect = new RECT { Left = rect.Left + 24, Top = msgTop, Right = rect.Right - 24, Bottom = msgBottom };
                        DrawText(hdc, payload.Message, -1, ref msgRect, DT_CENTER | DT_VCENTER | DT_WORDBREAK);

                        // 4. Physical Host Info Banner (if ShowHostInfo)
                        if (payload.ShowHostInfo)
                        {
                            SetTextColor(hdc, 0x00F8BD38); // Cyan / Gold in BGR
                            IntPtr hostFont = CreateFont(16, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
                            SelectObject(hdc, hostFont);

                            string hostInfo = $"Komputer: {payload.Hostname ?? Environment.MachineName}   |   IP: {payload.IpAddress ?? "LAN"}   |   Ruang: {payload.RoomName ?? "General"}";
                            var hostRect = new RECT { Left = rect.Left + 10, Top = rect.Bottom - 62, Right = rect.Right - 10, Bottom = rect.Bottom - 38 };
                            DrawText(hdc, hostInfo, -1, ref hostRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                            DeleteObject(hostFont);
                        }

                        // 5. Countdown / Dismiss Instruction Footer
                        SetTextColor(hdc, 0x0094A3B8); // Muted Slate Gray
                        IntPtr footerFont = CreateFont(14, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
                        SelectObject(hdc, footerFont);

                        string footerText = payload.DurationSeconds > 0
                            ? $"Menutup otomatis dalam {_remainingSeconds} detik... (Klik atau tekan Space/Esc untuk menutup)"
                            : "(Klik jendela ini atau tekan Space/Esc untuk menutup)";

                        var footerRect = new RECT { Left = rect.Left + 10, Top = rect.Bottom - 34, Right = rect.Right - 10, Bottom = rect.Bottom - 8 };
                        DrawText(hdc, footerText, -1, ref footerRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

                        // Clean up fonts
                        SelectObject(hdc, oldFont);
                        DeleteObject(headerFont);
                        DeleteObject(msgFont);
                        DeleteObject(footerFont);

                        EndPaint(hWnd, ref ps);
                        return IntPtr.Zero;
                    }

                case WM_LBUTTONDOWN:
                case WM_KEYDOWN:
                    Close();
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
            cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
            style = 0,
            lpfnWndProc = _staticWndProc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = className,
            hIconSm = IntPtr.Zero
        };

        RegisterClassEx(ref wc);

        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);

        int cardWidth = 580;
        int cardHeight = (_currentPayload?.ShowHostInfo == true) ? 270 : 230;

        int posX = Math.Max(0, (screenWidth - cardWidth) / 2);
        int posY = Math.Max(0, (screenHeight - cardHeight) / 2);

        _overlayHwnd = CreateWindowEx(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            className,
            "LabSupport Notification",
            WS_POPUP | WS_VISIBLE,
            posX,
            posY,
            cardWidth,
            cardHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_overlayHwnd == IntPtr.Zero)
        {
            readyEvent.Set();
            return;
        }

        SetWindowPos(_overlayHwnd, HWND_TOPMOST, posX, posY, cardWidth, cardHeight, SWP_SHOWWINDOW);
        ShowWindow(_overlayHwnd, 5); // SW_SHOW
        UpdateWindow(_overlayHwnd);

        readyEvent.Set();

        while (!ct.IsCancellationRequested && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnregisterClass(className, hInstance);
    }

    private static void TryWtsFallback(NotificationPayload payload)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return;
            uint consoleSession = WTSGetActiveConsoleSessionId();
            if (consoleSession == 0xFFFFFFFF) return;

            string title = string.IsNullOrWhiteSpace(payload.Title) ? "LabSupport" : payload.Title;
            string message = payload.Message;
            if (payload.ShowHostInfo)
            {
                message += $"\n[PC: {payload.Hostname ?? Environment.MachineName} | IP: {payload.IpAddress ?? "LAN"}]";
            }

            uint timeout = (uint)Math.Max(1, payload.DurationSeconds);
            WTSSendMessage(
                IntPtr.Zero,
                consoleSession,
                title,
                title.Length * 2,
                message,
                message.Length * 2,
                0x00000040, // MB_ICONINFORMATION
                timeout,
                out _,
                false);
        }
        catch { }
    }

    public void Dispose()
    {
        Close();
    }
}
