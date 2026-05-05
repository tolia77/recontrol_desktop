using System;
using System.IO;
using System.Threading;

namespace ReControl.Desktop.Services.Clipboard;

public sealed class ClipboardSettingsWatcher : IDisposable
{
    private readonly FileSystemWatcher _fsw;
    private readonly Action _onChange;
    private Timer? _debounce;
    private readonly object _gate = new();
    private bool _disposed;

    private const int DebounceMs = 300;

    public ClipboardSettingsWatcher(string jsonPath, Action onChange)
    {
        _onChange = onChange;
        var dir = Path.GetDirectoryName(jsonPath)!;
        Directory.CreateDirectory(dir);
        _fsw = new FileSystemWatcher(dir, Path.GetFileName(jsonPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
            InternalBufferSize = 64 * 1024
        };
        _fsw.Changed += (_, _) => Poke();
        _fsw.Created += (_, _) => Poke();
        _fsw.Renamed += (_, _) => Poke();
    }

    private void Poke()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ =>
            {
                try { _onChange(); }
                catch (IOException) { }
            }, null, DebounceMs, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _debounce?.Dispose();
            _debounce = null;
        }
        _fsw.Dispose();
    }
}
