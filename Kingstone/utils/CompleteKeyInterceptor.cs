using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

public class CompleteKeyInterceptor
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private LowLevelKeyboardProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;
    private static CompleteKeyInterceptor _instance;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public event Action<int, bool, bool> KeyEvent;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook,
        LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

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

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule)
        {
            IntPtr moduleHandle = GetModuleHandle(curModule.ModuleName);
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
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
            if (!IsTargetApplicationFocused())
            {
                return CallNextHookEx(_hookID, nCode, wParam, lParam);
            }

            KBDLLHOOKSTRUCT keyInfo = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            int vkCode = keyInfo.vkCode;

            bool isKeyDown = false;
            bool isSystemKey = false;

            switch ((int)wParam)
            {
                case WM_KEYDOWN:
                    isKeyDown = true;
                    isSystemKey = false;
                    break;
                case WM_KEYUP:
                    isKeyDown = false;
                    isSystemKey = false;
                    break;
                case WM_SYSKEYDOWN:
                    isKeyDown = true;
                    isSystemKey = true;
                    break;
                case WM_SYSKEYUP:
                    isKeyDown = false;
                    isSystemKey = true;
                    break;
            }

            _instance?.KeyEvent?.Invoke(vkCode, isKeyDown, isSystemKey);

            // Block ALL keys when our app has focus
            return (IntPtr)1;
        }

        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    public static string GetKeyName(int vkCode)
    {
        return vkCode switch
        {
            // Modifier keys
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x5B => "LeftWin",
            0x5C => "RightWin",
            0x5D => "Menu",

            // Navigation keys
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x2D => "Insert",
            0x2E => "Delete",
            0x25 => "LeftArrow",
            0x26 => "UpArrow",
            0x27 => "RightArrow",
            0x28 => "DownArrow",

            // Special keys
            0x14 => "CapsLock",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0x13 => "Pause",
            0x2C => "PrintScreen",

            // Function keys
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",

            // Standard keys
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Escape",
            0x20 => "Space",

            // Numbers
            0x30 => "0",
            0x31 => "1",
            0x32 => "2",
            0x33 => "3",
            0x34 => "4",
            0x35 => "5",
            0x36 => "6",
            0x37 => "7",
            0x38 => "8",
            0x39 => "9",

            // Letters
            0x41 => "A",
            0x42 => "B",
            0x43 => "C",
            0x44 => "D",
            0x45 => "E",
            0x46 => "F",
            0x47 => "G",
            0x48 => "H",
            0x49 => "I",
            0x4A => "J",
            0x4B => "K",
            0x4C => "L",
            0x4D => "M",
            0x4E => "N",
            0x4F => "O",
            0x50 => "P",
            0x51 => "Q",
            0x52 => "R",
            0x53 => "S",
            0x54 => "T",
            0x55 => "U",
            0x56 => "V",
            0x57 => "W",
            0x58 => "X",
            0x59 => "Y",
            0x5A => "Z",

            // Numpad
            0x60 => "Numpad0",
            0x61 => "Numpad1",
            0x62 => "Numpad2",
            0x63 => "Numpad3",
            0x64 => "Numpad4",
            0x65 => "Numpad5",
            0x66 => "Numpad6",
            0x67 => "Numpad7",
            0x68 => "Numpad8",
            0x69 => "Numpad9",
            0x6A => "NumpadMultiply",
            0x6B => "NumpadAdd",
            0x6C => "NumpadSeparator",
            0x6D => "NumpadSubtract",
            0x6E => "NumpadDecimal",
            0x6F => "NumpadDivide",

            // Punctuation and symbols
            0xBA => "Semicolon",     // ;:
            0xBB => "Equal",         // =+
            0xBC => "Comma",         // ,
            0xBD => "Minus",         // -_
            0xBE => "Period",        // .>
            0xBF => "Slash",         // /?
            0xC0 => "Backtick",      // `~
            0xDB => "LeftBracket",   // [{
            0xDC => "Backslash",     // \|
            0xDD => "RightBracket",  // ]}
            0xDE => "Quote",         // '"

            // Additional keys
            0x92 => "OEM102",        // Additional key on some keyboards
            0x93 => "Clear",
            0x95 => "Help",
            0x96 => "F13",
            0x97 => "F14",
            0x98 => "F15",
            0x99 => "F16",
            0x9A => "F17",
            0x9B => "F18",
            0x9C => "F19",
            0x9D => "F20",
            0x9E => "F21",
            0x9F => "F22",
            0xA0 => "F23",
            0xA1 => "F24",

            // Mouse buttons (some keyboards have these)
            0x01 => "LeftMouseButton",
            0x02 => "RightMouseButton",
            0x04 => "MiddleMouseButton",

            _ => $"VK_{vkCode:X2}"
        };
    }
}