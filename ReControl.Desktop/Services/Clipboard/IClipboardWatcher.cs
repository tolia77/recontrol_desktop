using System;

namespace ReControl.Desktop.Services.Clipboard;

/// <summary>
/// Platform-neutral OS-clipboard change-detection contract.
/// Implementations:
///   Windows: Services.Clipboard.Platforms.WindowsClipboardWatcher (HWND_MESSAGE + WM_CLIPBOARDUPDATE)
///   Linux:   Services.Clipboard.Platforms.LinuxClipboardWatcher (X11 XFixes on CLIPBOARD selection)
///
/// Watchers are READ-ONLY: they raise ClipboardChanged with the current text and never write back.
/// Writing the OS clipboard on receive happens through Avalonia's IClipboard.SetTextAsync from
/// the orchestrator on the UI thread (Pitfall C: single write path).
///
/// Lifecycle: process-wide singleton (CONTEXT.md D-03). Start() at app boot via MainWindow.OnOpened;
/// Stop()/Dispose() on app shutdown.
/// </summary>
public interface IClipboardWatcher : IDisposable
{
    /// <summary>
    /// Raised when the OS clipboard text content changes. Payload is the raw UTF-16 string
    /// from the OS (no normalization applied -- normalization is the orchestrator's job per D-13).
    /// Raised on the worker thread that performed the read (Windows: ThreadPool; Linux: dedicated
    /// X11 event thread). Consumers MUST marshal to UI thread before invoking Avalonia APIs.
    /// </summary>
    event Action<string>? ClipboardChanged;

    /// <summary>
    /// Begin observing the OS clipboard. Idempotent: calling Start twice is a no-op.
    /// On Windows: dispatches HWND_MESSAGE creation to UI thread, registers WM_CLIPBOARDUPDATE listener.
    /// On Linux: opens dedicated X11 display, spawns background event thread, subscribes to XFixes CLIPBOARD selection.
    /// </summary>
    void Start();

    /// <summary>
    /// Stop observing. Idempotent. Releases native handles (HWND, X Display) but leaves the instance disposable.
    /// </summary>
    void Stop();
}
