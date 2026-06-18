using System;
using ReControl.Desktop.Services.Clipboard.Platforms;

namespace ReControl.Desktop.Services.Clipboard;

/// <summary>
/// OS-dispatched factory for IClipboardWatcher. Mirrors Platform/PlatformServices.cs.
/// </summary>
public static class ClipboardWatcherFactory
{
    public static IClipboardWatcher Create(LogService log)
    {
        if (log is null) throw new ArgumentNullException(nameof(log));
        if (OperatingSystem.IsWindows()) return new WindowsClipboardWatcher(log);
        if (OperatingSystem.IsLinux()) return new LinuxClipboardWatcher(log);
        // Do NOT throw on unsupported OS. The DI container eagerly resolves the watcher
        // singleton at startup (ServiceCollectionExtensions.BuildApplicationServices), so a throw
        // here crashes the entire app (e.g. on macOS) before MainWindow.OnOpened runs. Fall back to a
        // no-op watcher: the rest of the clipboard graph builds, sync is silently disabled.
        log.Warning($"clipboard: unsupported OS '{Environment.OSVersion.Platform}'; using no-op watcher");
        return new NoOpClipboardWatcher();
    }

    /// <summary>
    /// Inert IClipboardWatcher used on platforms with no native impl (e.g. macOS).
    /// Start/Stop/Dispose are all no-ops; ClipboardChanged never fires.
    /// </summary>
    private sealed class NoOpClipboardWatcher : IClipboardWatcher
    {
#pragma warning disable CS0067 // event never used
        public event Action<string>? ClipboardChanged;
#pragma warning restore CS0067
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
