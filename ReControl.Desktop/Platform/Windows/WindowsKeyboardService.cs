using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Windows keyboard input simulation via Win32 SendInput.
/// Ported from WPF KeyboardService with KEYEVENTF_EXTENDEDKEY fix for arrow/nav keys.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsKeyboardService : IKeyboardService
{
    private readonly LogService _log;

    // Win32 constants
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    /// <summary>
    /// Virtual key codes that require the KEYEVENTF_EXTENDEDKEY flag.
    /// Without this flag, arrow keys, navigation keys, and certain modifier keys
    /// may not be recognized correctly by target applications.
    /// </summary>
    private static readonly HashSet<ushort> ExtendedKeys = new()
    {
        0x21, // VK_PRIOR (PageUp)
        0x22, // VK_NEXT (PageDown)
        0x23, // VK_END
        0x24, // VK_HOME
        0x25, // VK_LEFT
        0x26, // VK_UP
        0x27, // VK_RIGHT
        0x28, // VK_DOWN
        0x2C, // VK_SNAPSHOT (PrintScreen)
        0x2D, // VK_INSERT
        0x2E, // VK_DELETE
        0x5B, // VK_LWIN
        0x5C, // VK_RWIN
        0x6F, // VK_DIVIDE (Numpad /)
        0x90, // VK_NUMLOCK
        0xA3, // VK_RCONTROL
        0xA5, // VK_RMENU (RAlt)
    };

    public WindowsKeyboardService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsXtestAvailable => true;

    public void KeyDown(ushort vk)
    {
        uint flags = 0;
        if (ExtendedKeys.Contains(vk))
            flags |= KEYEVENTF_EXTENDEDKEY;

        SendKey(vk, flags);
    }

    public void KeyUp(ushort vk)
    {
        uint flags = KEYEVENTF_KEYUP;
        if (ExtendedKeys.Contains(vk))
            flags |= KEYEVENTF_EXTENDEDKEY;

        SendKey(vk, flags);
    }

    public void Press(ushort vk, int holdMs = 30)
    {
        KeyDown(vk);
        if (holdMs > 0) Thread.Sleep(holdMs);
        KeyUp(vk);
    }

    private void SendKey(ushort vk, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD
        };
        input.U.ki.wVk = vk;
        input.U.ki.wScan = 0;
        input.U.ki.dwFlags = flags;
        input.U.ki.time = 0;
        input.U.ki.dwExtraInfo = nint.Zero;

        uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            _log.Warning($"WindowsKeyboardService: SendInput failed. Win32Error={err}, vk=0x{vk:X2}, flags=0x{flags:X4}");
        }
    }

    // Win32 PInvoke declarations

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
