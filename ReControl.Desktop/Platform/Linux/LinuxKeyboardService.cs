using System;
using System.Runtime.Versioning;
using System.Threading;
using ReControl.Desktop.Models;
using ReControl.Desktop.Native;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Linux;

/// <summary>
/// Linux keyboard input simulation via X11 XTEST extension.
/// Uses VK-to-keysym-to-keycode conversion pipeline.
/// Opens its own X11 display connection (X11 is not thread-safe).
/// </summary>
[SupportedOSPlatform("linux")]
internal class LinuxKeyboardService : IKeyboardService, IDisposable
{
    private readonly LogService _log;
    private readonly IntPtr _display;
    private readonly bool _xtestAvailable;

    public LinuxKeyboardService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _display = X11Interop.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("Failed to open X11 display for keyboard service. Ensure DISPLAY is set.");

        _xtestAvailable = X11Interop.XTestQueryExtension(
            _display, out _, out _, out _, out _);

        if (!_xtestAvailable)
        {
            _log.Warning("LinuxKeyboardService: XTEST extension not available. Keyboard simulation disabled.");
        }
        else
        {
            _log.Info("LinuxKeyboardService: XTEST extension available.");
        }
    }

    public bool IsXtestAvailable => _xtestAvailable;

    public void KeyDown(ushort vk)
    {
        if (!_xtestAvailable) return;

        if (!VkToKeysymMap.TryGetKeysym(vk, out var keysym))
        {
            _log.Warning($"LinuxKeyboardService: Unmapped VK code: 0x{vk:X2}");
            return;
        }

        var keycode = X11Interop.XKeysymToKeycode(_display, keysym);
        if (keycode == 0)
        {
            _log.Warning($"LinuxKeyboardService: No keycode for keysym 0x{keysym:X4} (VK 0x{vk:X2})");
            return;
        }

        X11Interop.XTestFakeKeyEvent(_display, keycode, true, 0);
        X11Interop.XFlush(_display);
    }

    public void KeyUp(ushort vk)
    {
        if (!_xtestAvailable) return;

        if (!VkToKeysymMap.TryGetKeysym(vk, out var keysym))
        {
            _log.Warning($"LinuxKeyboardService: Unmapped VK code: 0x{vk:X2}");
            return;
        }

        var keycode = X11Interop.XKeysymToKeycode(_display, keysym);
        if (keycode == 0)
        {
            _log.Warning($"LinuxKeyboardService: No keycode for keysym 0x{keysym:X4} (VK 0x{vk:X2})");
            return;
        }

        X11Interop.XTestFakeKeyEvent(_display, keycode, false, 0);
        X11Interop.XFlush(_display);
    }

    public void Press(ushort vk, int holdMs = 30)
    {
        KeyDown(vk);
        if (holdMs > 0) Thread.Sleep(holdMs);
        KeyUp(vk);
    }

    public void Dispose()
    {
        if (_display != IntPtr.Zero)
        {
            X11Interop.XCloseDisplay(_display);
        }
    }
}
