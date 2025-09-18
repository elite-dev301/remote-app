using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

public class CompleteMouseInterceptor
{
    private const int WH_MOUSE_LL = 14;

    // Mouse message types
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E; // Horizontal wheel
    private const int WM_XBUTTONDOWN = 0x020B; // Extra mouse buttons (X1, X2)
    private const int WM_XBUTTONUP = 0x020C;

    private LowLevelMouseProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;
    private static CompleteMouseInterceptor _instance;

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    public event Action<MouseEventInfo> MouseEvent;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public struct MouseEventInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public MouseEventType EventType { get; set; }
        public MouseButton Button { get; set; }
        public int WheelDelta { get; set; }
        public uint Flags { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum MouseEventType
    {
        Move,
        LeftButtonDown,
        LeftButtonUp,
        RightButtonDown,
        RightButtonUp,
        MiddleButtonDown,
        MiddleButtonUp,
        XButtonDown,
        XButtonUp,
        VerticalWheel,
        HorizontalWheel
    }

    public enum MouseButton
    {
        None,
        Left,
        Right,
        Middle,
        X1,
        X2
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook,
        LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
        IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    public void StartIntercepting()
    {
        _instance = this;
        _hookID = SetHook(_proc);
    }

    public void StopIntercepting()
    {
        UnhookWindowsHookEx(_hookID);
        _instance = null;
    }

    private static IntPtr SetHook(LowLevelMouseProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule)
        {
            IntPtr moduleHandle = GetModuleHandle(curModule.ModuleName);
            return SetWindowsHookEx(WH_MOUSE_LL, proc, moduleHandle, 0);
        }
    }

    private static bool IsTargetApplicationFocused()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(foregroundWindow, out uint foregroundProcessId);
        uint currentProcessId = (uint)Process.GetCurrentProcess().Id;

        return foregroundProcessId == currentProcessId;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            // Only intercept if our application has focus
            if (!IsTargetApplicationFocused())
            {
                return CallNextHookEx(_hookID, nCode, wParam, lParam);
            }

            MSLLHOOKSTRUCT mouseStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

            var eventInfo = new MouseEventInfo
            {
                X = mouseStruct.pt.x,
                Y = mouseStruct.pt.y,
                Flags = mouseStruct.flags,
                Timestamp = DateTime.Now
            };

            // Parse the mouse event
            switch ((int)wParam)
            {
                case WM_MOUSEMOVE:
                    eventInfo.EventType = MouseEventType.Move;
                    eventInfo.Button = MouseButton.None;
                    break;

                case WM_LBUTTONDOWN:
                    eventInfo.EventType = MouseEventType.LeftButtonDown;
                    eventInfo.Button = MouseButton.Left;
                    break;

                case WM_LBUTTONUP:
                    eventInfo.EventType = MouseEventType.LeftButtonUp;
                    eventInfo.Button = MouseButton.Left;
                    break;

                case WM_RBUTTONDOWN:
                    eventInfo.EventType = MouseEventType.RightButtonDown;
                    eventInfo.Button = MouseButton.Right;
                    break;

                case WM_RBUTTONUP:
                    eventInfo.EventType = MouseEventType.RightButtonUp;
                    eventInfo.Button = MouseButton.Right;
                    break;

                case WM_MBUTTONDOWN:
                    eventInfo.EventType = MouseEventType.MiddleButtonDown;
                    eventInfo.Button = MouseButton.Middle;
                    break;

                case WM_MBUTTONUP:
                    eventInfo.EventType = MouseEventType.MiddleButtonUp;
                    eventInfo.Button = MouseButton.Middle;
                    break;

                case WM_XBUTTONDOWN:
                    eventInfo.EventType = MouseEventType.XButtonDown;
                    // Extract which X button (X1 or X2) from mouseData
                    int xButtonDown = (int)((mouseStruct.mouseData >> 16) & 0xFFFF);
                    eventInfo.Button = xButtonDown == 1 ? MouseButton.X1 : MouseButton.X2;
                    break;

                case WM_XBUTTONUP:
                    eventInfo.EventType = MouseEventType.XButtonUp;
                    // Extract which X button (X1 or X2) from mouseData
                    int xButtonUp = (int)((mouseStruct.mouseData >> 16) & 0xFFFF);
                    eventInfo.Button = xButtonUp == 1 ? MouseButton.X1 : MouseButton.X2;
                    break;

                case WM_MOUSEWHEEL:
                    eventInfo.EventType = MouseEventType.VerticalWheel;
                    eventInfo.Button = MouseButton.None;
                    // Extract wheel delta (positive = up, negative = down)
                    eventInfo.WheelDelta = (short)((mouseStruct.mouseData >> 16) & 0xFFFF);
                    break;

                case WM_MOUSEHWHEEL:
                    eventInfo.EventType = MouseEventType.HorizontalWheel;
                    eventInfo.Button = MouseButton.None;
                    // Extract horizontal wheel delta
                    eventInfo.WheelDelta = (short)((mouseStruct.mouseData >> 16) & 0xFFFF);
                    break;
            }

            // Notify about the mouse event
            _instance?.MouseEvent?.Invoke(eventInfo);

            // Block ALL mouse events when our app has focus
            // return (IntPtr)1;
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }
}