using System;
using System.Runtime.InteropServices;
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

    /// <summary>Lazily-resolved spare keycode for typeText remapping. -1 = not yet scanned, 0 = none free.</summary>
    private int _spareKeycode = -1;

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

    public void TypeText(string text)
    {
        if (!_xtestAvailable || string.IsNullOrEmpty(text)) return;

        // Layout-independent Unicode entry (xdotool approach): temporarily bind each
        // codepoint's keysym to a spare keycode and fake a press/release. Going through
        // XKeysymToKeycode on the current layout would both miss non-Latin chars and
        // ignore shift state (keysym 'A' resolves to the 'a' keycode), so the remap
        // path is used for every character.
        int spare = GetSpareKeycode();
        if (spare == 0)
        {
            _log.Warning("LinuxKeyboardService: no spare keycode available; typeText dropped.");
            return;
        }

        try
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r') continue; // \r\n pairs: \n alone produces Return

                ulong keysym;
                if (c == '\n') keysym = 0xFF0D;      // XK_Return
                else if (c == '\t') keysym = 0xFF09; // XK_Tab
                else if (char.IsSurrogatePair(text, i))
                {
                    keysym = 0x01000000UL + (ulong)char.ConvertToUtf32(text, i);
                    i++; // consume low surrogate
                }
                else if (char.IsControl(c)) continue;
                else if (c < 0x100) keysym = c;                  // Latin-1 keysyms map directly
                else keysym = 0x01000000UL + c;                  // Unicode keysym convention

                // Bind to both shift levels so the unshifted fake press always yields the char.
                X11Interop.XChangeKeyboardMapping(_display, spare, 2, new[] { keysym, keysym }, 1);
                X11Interop.XSync(_display, false);

                X11Interop.XTestFakeKeyEvent(_display, (uint)spare, true, 0);
                X11Interop.XTestFakeKeyEvent(_display, (uint)spare, false, 0);
                X11Interop.XSync(_display, false);
            }
        }
        finally
        {
            // Restore the spare keycode to NoSymbol so the temporary binding never leaks.
            X11Interop.XChangeKeyboardMapping(_display, spare, 2, new ulong[] { 0, 0 }, 1);
            X11Interop.XSync(_display, false);
        }
    }

    /// <summary>
    /// Finds (and caches) a keycode with no keysyms bound, scanning from the top of the
    /// device's keycode range where unused codes live. Returns 0 if none is free.
    /// </summary>
    private int GetSpareKeycode()
    {
        if (_spareKeycode != -1) return _spareKeycode;

        _spareKeycode = 0;
        X11Interop.XDisplayKeycodes(_display, out int minKc, out int maxKc);
        IntPtr mapPtr = X11Interop.XGetKeyboardMapping(_display, (byte)minKc, maxKc - minKc + 1, out int perKc);
        if (mapPtr == IntPtr.Zero) return _spareKeycode;

        try
        {
            for (int kc = maxKc; kc >= minKc; kc--)
            {
                bool empty = true;
                for (int s = 0; s < perKc; s++)
                {
                    long offset = ((long)(kc - minKc) * perKc + s) * IntPtr.Size;
                    if (Marshal.ReadIntPtr(mapPtr, (int)offset) != IntPtr.Zero)
                    {
                        empty = false;
                        break;
                    }
                }
                if (empty)
                {
                    _spareKeycode = kc;
                    break;
                }
            }
        }
        finally
        {
            X11Interop.XFree(mapPtr);
        }

        return _spareKeycode;
    }

    public void Dispose()
    {
        if (_display != IntPtr.Zero)
        {
            X11Interop.XCloseDisplay(_display);
        }
    }
}
