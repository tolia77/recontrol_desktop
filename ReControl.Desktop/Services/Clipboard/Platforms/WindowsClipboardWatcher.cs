using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia.Threading;
using ReControl.Desktop.Native;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Services.Clipboard.Platforms;

/// <summary>
/// Win32 clipboard change watcher built on a dedicated HWND_MESSAGE window.
/// CONTEXT.md D-01 mandates this approach (NOT Win32Properties.AddWndProcHookCallback)
/// because Avalonia's main HWND can be destroyed/recreated on theme/DPI changes,
/// silently breaking AddClipboardFormatListener (Pitfall 7).
///
/// Threading model:
///   - Start() dispatches HWND creation to Avalonia's UI thread (CONTEXT D-01 + RESEARCH Pattern 1).
///     The UI thread's existing message pump dispatches WM_CLIPBOARDUPDATE.
///   - WndProc immediately offloads the clipboard read to ThreadPool.QueueUserWorkItem
///     so the message pump is never blocked by the 5x10ms OpenClipboard retry loop.
///
/// Pitfall A: WndProc delegate is rooted via GCHandle.Alloc to prevent the GC from
/// collecting/moving the marshalled function pointer.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboardWatcher : IClipboardWatcher
{
    private const string ClassName = "ReControlClipboardListener";

    private readonly LogService _log;
    private readonly object _gate = new();

    private Win32ClipboardInterop.WndProcDelegate? _wndProc;
    private GCHandle _wndProcHandle;
    private IntPtr _hwnd;
    private ushort _classAtom;
    private IntPtr _hInstance;
    private bool _started;
    private bool _disposed;

    public event Action<string>? ClipboardChanged;

    public WindowsClipboardWatcher(LogService log)
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

        // HWND creation must happen on a thread with a message pump. UI thread is the simplest source.
        // Wait synchronously so callers (DI eager-resolve, MainWindow.OnOpened) can rely on _hwnd being valid on return.
        Dispatcher.UIThread.InvokeAsync(CreateMessageWindow).Wait();
    }

    private void CreateMessageWindow()
    {
        _hInstance = Win32ClipboardInterop.GetModuleHandle(null);

        _wndProc = OnWndProc;
        _wndProcHandle = GCHandle.Alloc(_wndProc); // Pitfall A: prevent GC of the marshalled delegate

        var wc = new Win32ClipboardInterop.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32ClipboardInterop.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _wndProc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = _hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = ClassName,
            hIconSm = IntPtr.Zero
        };
        _classAtom = Win32ClipboardInterop.RegisterClassEx(ref wc);
        if (_classAtom == 0)
        {
            _log.Warning($"clipboard: RegisterClassEx failed errno={Marshal.GetLastWin32Error()}; clipboard sync disabled");
            return;
        }

        _hwnd = Win32ClipboardInterop.CreateWindowEx(
            dwExStyle: 0,
            lpClassName: ClassName,
            lpWindowName: null,
            dwStyle: 0,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent: Win32ClipboardInterop.HWND_MESSAGE,
            hMenu: IntPtr.Zero,
            hInstance: _hInstance,
            lpParam: IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            _log.Warning($"clipboard: CreateWindowEx failed errno={Marshal.GetLastWin32Error()}; clipboard sync disabled");
            return;
        }

        if (!Win32ClipboardInterop.AddClipboardFormatListener(_hwnd))
        {
            _log.Warning($"clipboard: AddClipboardFormatListener failed errno={Marshal.GetLastWin32Error()}; clipboard sync disabled");
            Win32ClipboardInterop.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            return;
        }

        _log.Info("clipboard: WindowsClipboardWatcher started (HWND_MESSAGE)");
    }

    private IntPtr OnWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32ClipboardInterop.WM_CLIPBOARDUPDATE)
        {
            // Offload the read so the pump is never blocked by the 5x10ms retry loop.
            ThreadPool.QueueUserWorkItem(_ => TryReadAndRaise());
            return IntPtr.Zero;
        }
        return Win32ClipboardInterop.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void TryReadAndRaise()
    {
        // CONTEXT D-02: 5 attempts with 10ms delay; final failure logs at warning and returns.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (Win32ClipboardInterop.OpenClipboard(_hwnd))
            {
                try
                {
                    var hData = Win32ClipboardInterop.GetClipboardData((uint)Win32ClipboardInterop.CF_UNICODETEXT);
                    if (hData == IntPtr.Zero) return; // empty / non-text -- Pitfall: do NOT raise

                    var locked = Win32ClipboardInterop.GlobalLock(hData);
                    if (locked == IntPtr.Zero) return;
                    try
                    {
                        var text = Marshal.PtrToStringUni(locked);
                        if (text != null)
                        {
                            try { ClipboardChanged?.Invoke(text); }
                            catch (Exception ex) { _log.Warning($"clipboard: ClipboardChanged handler threw: {ex.Message}"); }
                        }
                    }
                    finally { Win32ClipboardInterop.GlobalUnlock(hData); }
                    return;
                }
                finally { Win32ClipboardInterop.CloseClipboard(); }
            }

            _log.Debug($"clipboard: OpenClipboard attempt {attempt + 1} failed errno={Marshal.GetLastWin32Error()}");
            Thread.Sleep(10);
        }
        _log.Warning("clipboard: OpenClipboard failed after 5 retries; skipping silently");
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started || _disposed) return;
            _started = false;
        }

        // Dispatch teardown back to UI thread (the thread that owns the HWND).
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_hwnd != IntPtr.Zero)
            {
                Win32ClipboardInterop.RemoveClipboardFormatListener(_hwnd);
                Win32ClipboardInterop.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_classAtom != 0)
            {
                Win32ClipboardInterop.UnregisterClass(ClassName, _hInstance);
                _classAtom = 0;
            }
        }).Wait();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { Stop(); } catch (Exception ex) { _log.Warning($"clipboard: Stop in Dispose threw: {ex.Message}"); }

        // Free GC handle AFTER native window is destroyed (no more callbacks possible).
        if (_wndProcHandle.IsAllocated) _wndProcHandle.Free();
        _wndProc = null;
        _log.Info("clipboard: WindowsClipboardWatcher disposed");
    }
}
