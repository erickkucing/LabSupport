using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using LabSupport.Shared.Protocol;

namespace LabSupport.Agent.Services;

public class InputSimulator
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_TAB = 0x09;
    private const byte VK_LWIN = 0x5B;
    private const byte VK_F4 = 0x73;

    public bool SimulateMouse(MouseInputPayload payload)
    {
        if (!OperatingSystem.IsWindows()) return true;

        try
        {
            int screenWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (screenWidth <= 0) screenWidth = GetSystemMetrics(SM_CXSCREEN);
            if (screenHeight <= 0) screenHeight = GetSystemMetrics(SM_CYSCREEN);
            if (screenWidth <= 0) screenWidth = 1920;
            if (screenHeight <= 0) screenHeight = 1080;

            int targetX = (int)Math.Round(Math.Clamp(payload.NormalizedX, 0.0, 1.0) * screenWidth);
            int targetY = (int)Math.Round(Math.Clamp(payload.NormalizedY, 0.0, 1.0) * screenHeight);

            switch (payload.Action)
            {
                case MouseAction.Move:
                    SetCursorPos(targetX, targetY);
                    break;

                case MouseAction.LeftDown:
                    SetCursorPos(targetX, targetY);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    break;

                case MouseAction.LeftUp:
                    SetCursorPos(targetX, targetY);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case MouseAction.LeftClick:
                    SetCursorPos(targetX, targetY);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case MouseAction.RightDown:
                    SetCursorPos(targetX, targetY);
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    break;

                case MouseAction.RightUp:
                    SetCursorPos(targetX, targetY);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case MouseAction.RightClick:
                    SetCursorPos(targetX, targetY);
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case MouseAction.DoubleClick:
                    SetCursorPos(targetX, targetY);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(60);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case MouseAction.MiddleClick:
                    SetCursorPos(targetX, targetY);
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case MouseAction.Scroll:
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)payload.WheelDelta, UIntPtr.Zero);
                    break;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SimulateKeyboard(KeyboardInputPayload payload)
    {
        if (!OperatingSystem.IsWindows()) return true;

        try
        {
            if (payload.Action == KeyboardAction.Shortcut)
            {
                string key = (payload.Key ?? string.Empty).Trim().ToUpperInvariant();
                switch (key)
                {
                    case "WIN+D":
                        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                        keybd_event((byte)'D', 0, 0, UIntPtr.Zero);
                        keybd_event((byte)'D', 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        break;

                    case "ALT+F4":
                        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_F4, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_F4, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        break;

                    case "WIN+R":
                        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                        keybd_event((byte)'R', 0, 0, UIntPtr.Zero);
                        keybd_event((byte)'R', 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        break;

                    case "TASKMGR":
                    case "CTRL+SHIFT+ESC":
                        try
                        {
                            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                        }
                        catch
                        {
                            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        }
                        break;

                    case "WIN+L":
                    case "LOCK":
                    case "CTRL+ALT+DEL":
                        LockWorkStation();
                        break;

                    case "ENTER":
                        keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        break;

                    case "ESC":
                    case "ESCAPE":
                        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        break;

                    case "TAB":
                        keybd_event(VK_TAB, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        break;
                }
            }
            else if (payload.Action == KeyboardAction.Text && !string.IsNullOrEmpty(payload.Text))
            {
                foreach (char c in payload.Text)
                {
                    if (c == '\r') continue;
                    if (c == '\n')
                    {
                        keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        Thread.Sleep(5);
                        continue;
                    }

                    keybd_event(0, (byte)c, KEYEVENTF_UNICODE, UIntPtr.Zero);
                    keybd_event(0, (byte)c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Thread.Sleep(5);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
