using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Windows mouse input simulation via Win32 SetCursorPos and mouse_event.
/// Ported from WPF MouseService. Uses absolute positioning (primary monitor).
/// Button mapping: 0=left, 1=right, 2=middle (matches frontend mapButtonToBackend).
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsMouseService : IMouseService
{
    private readonly LogService _log;

    // mouse_event flag constants
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    public WindowsMouseService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void MoveTo(int x, int y)
    {
        if (!SetCursorPos(x, y))
        {
            int err = Marshal.GetLastWin32Error();
            _log.Warning($"WindowsMouseService: SetCursorPos failed. Win32Error={err}, x={x}, y={y}");
        }
    }

    public void MouseDown(int button)
    {
        var flag = MapButtonToDownFlag(button);
        mouse_event(flag, 0, 0, 0, UIntPtr.Zero);
    }

    public void MouseUp(int button)
    {
        var flag = MapButtonToUpFlag(button);
        mouse_event(flag, 0, 0, 0, UIntPtr.Zero);
    }

    public void Scroll(int clicks)
    {
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, clicks * 120, UIntPtr.Zero);
    }

    public void Click(int button = 0, int delayMs = 30)
    {
        if (delayMs < 0) delayMs = 0;
        MouseDown(button);
        if (delayMs > 0) Thread.Sleep(delayMs);
        MouseUp(button);
    }

    public void DoubleClick(int delayMs = 120)
    {
        if (delayMs < 0) delayMs = 0;
        Click(0, delayMs / 2);
        if (delayMs > 0) Thread.Sleep(delayMs / 2);
        Click(0, delayMs / 2);
    }

    private uint MapButtonToDownFlag(int button) => button switch
    {
        0 => MOUSEEVENTF_LEFTDOWN,
        1 => MOUSEEVENTF_RIGHTDOWN,
        2 => MOUSEEVENTF_MIDDLEDOWN,
        _ => MOUSEEVENTF_LEFTDOWN,
    };

    private uint MapButtonToUpFlag(int button) => button switch
    {
        0 => MOUSEEVENTF_LEFTUP,
        1 => MOUSEEVENTF_RIGHTUP,
        2 => MOUSEEVENTF_MIDDLEUP,
        _ => MOUSEEVENTF_LEFTUP,
    };

    // Win32 PInvoke declarations

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, UIntPtr dwExtraInfo);
}
