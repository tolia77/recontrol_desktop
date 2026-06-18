using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using ReControl.Desktop.Native;

namespace ReControl.Desktop.Services.Clipboard.Platforms;

/// <summary>
/// X11 clipboard change watcher built on a dedicated XOpenDisplay + background thread.
/// Uses its own X11 display rather than piggybacking on Avalonia's X11 connection
/// because Avalonia's Display* is internal and Xlib is not thread-safe by default.
///
/// Responsibilities:
///   - Subscribe via XFixesSelectSelectionInput to the CLIPBOARD selection ONLY (never PRIMARY).
///   - On selection-owner-notify, request UTF8_STRING via XConvertSelection on a hidden 1x1 window.
///   - Wait for SelectionNotify, then XGetWindowProperty -> Encoding.UTF8.GetString -> raise ClipboardChanged.
///
/// READ-ONLY: the watcher does NOT write the X11 selection. The orchestrator (ClipboardSyncService)
/// writes via Avalonia.Input.Platform.IClipboard.SetTextAsync on the UI thread.
///
/// Wayland: detected via XDG_SESSION_TYPE; if "wayland", logs a warning and Start() is a no-op
/// (X11 only; Wayland is out of scope).
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxClipboardWatcher : IClipboardWatcher
{
    private readonly LogService _log;
    private readonly object _gate = new();

    private IntPtr _display;
    private IntPtr _ourWindow;
    private IntPtr _atomClipboard;
    private IntPtr _atomUtf8String;
    private IntPtr _atomTargets;
    private IntPtr _atomReadProp;        // our own per-instance property atom for XConvertSelection
    private IntPtr _atomQuit;            // synthetic ClientMessage atom used by Stop() to wake XNextEvent
    private int _xfixesEventBase;

    private Thread? _eventThread;
    private CancellationTokenSource? _cts;
    private bool _started;
    private bool _disposed;

    public event Action<string>? ClipboardChanged;

    public LinuxClipboardWatcher(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        // Wayland short-circuit: clipboard sync is X11-only.
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase))
        {
            _log.Warning("clipboard: Wayland session detected (XDG_SESSION_TYPE=wayland); clipboard sync disabled (X11 only in v1.3)");
            return;
        }

        // Use a dedicated X11 display; never share Avalonia's (Xlib is not thread-safe by default).
        X11Interop.XInitThreads(); // idempotent on repeat calls per X11ScreenCaptureService precedent
        _display = X11Interop.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
        {
            _log.Warning("clipboard: XOpenDisplay returned null; clipboard sync disabled");
            return;
        }

        if (X11Interop.XFixesQueryExtension(_display, out _xfixesEventBase, out _) == 0)
        {
            _log.Warning("clipboard: XFixes extension unavailable; clipboard sync disabled");
            X11Interop.XCloseDisplay(_display);
            _display = IntPtr.Zero;
            return;
        }
        if (X11Interop.XFixesQueryVersion(_display, out var major, out _) == 0 || major < 2)
        {
            _log.Warning($"clipboard: XFixes version {major} unsupported (need >=2); clipboard sync disabled");
            X11Interop.XCloseDisplay(_display);
            _display = IntPtr.Zero;
            return;
        }

        var screen = X11Interop.XDefaultScreen(_display);
        var root = X11Interop.XRootWindow(_display, screen);

        _atomClipboard  = X11Interop.XInternAtom(_display, "CLIPBOARD",   false);
        _atomUtf8String = X11Interop.XInternAtom(_display, "UTF8_STRING", false);
        _atomTargets    = X11Interop.XInternAtom(_display, "TARGETS",     false);
        _atomReadProp   = X11Interop.XInternAtom(_display, "RECONTROL_CLIP_READ", false);
        _atomQuit       = X11Interop.XInternAtom(_display, "RECONTROL_CLIP_QUIT", false);

        // Hidden 1x1 owner window we own; we'll request selection conversions onto it.
        _ourWindow = X11Interop.XCreateSimpleWindow(
            _display, root, x: -10, y: -10, width: 1, height: 1,
            borderWidth: 0, border: 0, background: 0);

        // Subscribe to the CLIPBOARD selection only; never PRIMARY.
        X11Interop.XFixesSelectSelectionInput(
            _display, _ourWindow, _atomClipboard,
            X11Interop.XFixesSetSelectionOwnerNotifyMask);
        X11Interop.XFlush(_display);

        _cts = new CancellationTokenSource();
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "X11Clipboard" };
        _eventThread.Start();

        _log.Info("clipboard: LinuxClipboardWatcher started (XFixes on CLIPBOARD)");
    }

    private void EventLoop()
    {
        var token = _cts!.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Block on next event; Stop() wakes us by sending a ClientMessage of type _atomQuit.
                if (X11Interop.XNextEvent(_display, out var ev) != 0) break;

                // XFixesSelectionNotify -> selection owner changed -> request the new contents
                if (ev.type == _xfixesEventBase) // XFixesSelectionNotify is opcode 0 within the XFixes range
                {
                    RequestClipboardContents();
                    continue;
                }
                if (ev.type == X11Interop.SelectionNotify)
                {
                    ReadAndRaiseFromProperty();
                    continue;
                }
                if (ev.type == X11Interop.ClientMessage)
                {
                    // Wakeup -- check token, exit cleanly if cancelled.
                    if (token.IsCancellationRequested) break;
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"clipboard: X11 event loop terminated by exception: {ex.Message}");
        }
    }

    private void RequestClipboardContents()
    {
        // Ask the owner of CLIPBOARD to convert to UTF8_STRING and stash the result on _ourWindow as _atomReadProp.
        X11Interop.XConvertSelection(
            _display, _atomClipboard, _atomUtf8String,
            _atomReadProp, _ourWindow, IntPtr.Zero /* CurrentTime */);
        X11Interop.XFlush(_display);
    }

    // XGetWindowProperty's long_length is in 32-bit units. The orchestrator
    // refuses payloads >2 MB, so we cap reads at 2 MB / 4 = 512K longs. If the
    // selection is larger than that, bytesAfter > 0 and we skip the raise rather
    // than truncate (which could split a UTF-8 multi-byte sequence and produce
    // a corrupt string).
    private const int MaxSelectionLongs = 512 * 1024; // 2 MiB at 32-bit format units

    private void ReadAndRaiseFromProperty()
    {
        var rc = X11Interop.XGetWindowProperty(
            _display, _ourWindow, _atomReadProp,
            offset: IntPtr.Zero, length: new IntPtr(MaxSelectionLongs), delete: true,
            reqType: X11Interop.AnyPropertyType,
            out var actualType, out var actualFormat,
            out var nItemsPtr, out var bytesAfter, out var prop);
        if (rc != 0 || prop == IntPtr.Zero) return;

        try
        {
            if (actualType != _atomUtf8String) return; // owner provided a different type -- not text we can handle
            if (actualFormat != 8) return;             // UTF8_STRING is 8-bit-format

            // bytesAfter > 0 means the property had more data than we asked
            // for. Truncating UTF-8 mid-codepoint would surface a Replacement
            // Character or garbage to the orchestrator, so we skip-and-log
            // instead of raising a corrupt string. Future work: implement INCR.
            var pending = bytesAfter.ToInt64();
            if (pending > 0)
            {
                _log.Warning($"clipboard: selection truncated, bytesAfter={pending}; skipping (>2MB or INCR not implemented)");
                return;
            }

            var nItems = nItemsPtr.ToInt64();
            if (nItems <= 0 || nItems > int.MaxValue) return;

            var bytes = new byte[(int)nItems];
            Marshal.Copy(prop, bytes, 0, (int)nItems);
            var text = Encoding.UTF8.GetString(bytes);

            try { ClipboardChanged?.Invoke(text); }
            catch (Exception ex) { _log.Warning($"clipboard: ClipboardChanged handler threw: {ex.Message}"); }
        }
        finally
        {
            X11Interop.XFree(prop);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started || _disposed) return;
            _started = false;
        }

        try { _cts?.Cancel(); } catch { /* ignored */ }

        // Wake XNextEvent: send a ClientMessage to our own window. The bg thread observes
        // ev.type == ClientMessage, sees IsCancellationRequested, exits.
        if (_display != IntPtr.Zero && _ourWindow != IntPtr.Zero)
        {
            var ev = new X11Interop.XEvent { type = X11Interop.ClientMessage };
            X11Interop.XSendEvent(_display, _ourWindow, propagate: false, eventMask: 0, ref ev);
            X11Interop.XFlush(_display);
        }

        try { _eventThread?.Join(2000); } catch (Exception ex) { _log.Warning($"clipboard: thread join threw: {ex.Message}"); }
        _eventThread = null;
        _cts?.Dispose();
        _cts = null;

        if (_display != IntPtr.Zero)
        {
            if (_ourWindow != IntPtr.Zero)
            {
                X11Interop.XDestroyWindow(_display, _ourWindow);
                _ourWindow = IntPtr.Zero;
            }
            X11Interop.XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { Stop(); }
        catch (Exception ex) { _log.Warning($"clipboard: Stop in Dispose threw: {ex.Message}"); }
        _log.Info("clipboard: LinuxClipboardWatcher disposed");
    }
}
