using System;
using System.Runtime.Versioning;
using System.Threading;
using ReControl.Desktop.Native;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Linux;

/// <summary>
/// Linux mouse input simulation via X11 XTEST extension.
/// Opens its own X11 display connection (X11 is not thread-safe).
/// Button mapping: frontend 0=left, 1=right, 2=middle -> X11 1=left, 2=middle, 3=right.
/// Scroll uses button 4 (up) and button 5 (down) with press+release pairs.
/// </summary>
[SupportedOSPlatform("linux")]
internal class LinuxMouseService : IMouseService, IDisposable
{
    private readonly LogService _log;
    private readonly IntPtr _display;
    private readonly int _screen;

    public LinuxMouseService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _display = X11Interop.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("Failed to open X11 display for mouse service. Ensure DISPLAY is set.");

        _screen = X11Interop.XDefaultScreen(_display);
    }

    /// <summary>
    /// Maps frontend button codes to X11 button numbers.
    /// Frontend: 0=left, 1=right, 2=middle
    /// X11:      1=left, 2=middle, 3=right
    /// </summary>
    private static uint MapButton(int frontendButton) => frontendButton switch
    {
        0 => 1,  // left -> X11 button 1
        1 => 3,  // right -> X11 button 3 (frontend swaps: 1=right)
        2 => 2,  // middle -> X11 button 2
        _ => 1   // default left
    };

    public void MoveTo(int x, int y)
    {
        X11Interop.XTestFakeMotionEvent(_display, _screen, x, y, 0);
        X11Interop.XFlush(_display);
    }

    public void MouseDown(int button)
    {
        var x11Button = MapButton(button);
        X11Interop.XTestFakeButtonEvent(_display, x11Button, true, 0);
        X11Interop.XFlush(_display);
    }

    public void MouseUp(int button)
    {
        var x11Button = MapButton(button);
        X11Interop.XTestFakeButtonEvent(_display, x11Button, false, 0);
        X11Interop.XFlush(_display);
    }

    public void Scroll(int clicks)
    {
        if (clicks == 0) return;

        // X11 scroll: button 4 = scroll up, button 5 = scroll down
        // Each scroll unit requires a press+release pair
        uint scrollButton = clicks > 0 ? 4u : 5u;
        int count = Math.Abs(clicks);

        for (int i = 0; i < count; i++)
        {
            X11Interop.XTestFakeButtonEvent(_display, scrollButton, true, 0);
            X11Interop.XTestFakeButtonEvent(_display, scrollButton, false, 0);
        }

        X11Interop.XFlush(_display);
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

    public void Dispose()
    {
        if (_display != IntPtr.Zero)
        {
            X11Interop.XCloseDisplay(_display);
        }
    }
}
