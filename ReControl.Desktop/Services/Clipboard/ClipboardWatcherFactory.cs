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
        throw new PlatformNotSupportedException(
            $"ClipboardWatcherFactory: unsupported OS '{Environment.OSVersion.Platform}'. v1.3 supports Windows + Linux X11 only.");
    }
}
