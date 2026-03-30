using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReControl.Desktop.Services;

/// <summary>
/// Combined file and in-memory logging service.
/// Ported from WPF InternalLogger + InMemoryLog.
/// </summary>
public sealed class LogService
{
    private const int MaxMemoryEntries = 1000;
    private const long MaxLogFileSize = 5 * 1024 * 1024; // 5MB

    // Prefixes for high-frequency logs that should be collapsed (show last only + count)
    private static readonly string[] CollapsiblePrefixes =
    {
        "WebRtcService: frame=",
    };

    private readonly string _logPath;
    private readonly object _fileLock = new();
    private readonly object _memoryLock = new();
    private readonly List<string> _memoryLog = new(MaxMemoryEntries);
    private readonly Dictionary<string, int> _collapseCounts = new();

    /// <summary>
    /// Raised when a new log entry is added to the in-memory buffer.
    /// The string parameter is the formatted log line.
    /// The bool parameter is true if the entry replaces the previous one (collapsed).
    /// </summary>
    public event Action<string, bool>? LogAdded;

    public LogService()
    {
        string logDir;

        if (OperatingSystem.IsWindows())
        {
            logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "recontrol");
        }
        else
        {
            logDir = Path.Combine(
                Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
                "recontrol");
        }

        try
        {
            Directory.CreateDirectory(logDir);
        }
        catch
        {
            // Fall back to temp directory if we cannot create log dir
            logDir = Path.GetTempPath();
        }

        _logPath = Path.Combine(logDir, "recontrol.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(string context, Exception ex) =>
        Write("ERROR", $"{context}: {ex}");

    /// <summary>
    /// Returns a snapshot of the in-memory log buffer.
    /// </summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_memoryLock)
        {
            return _memoryLog.ToArray();
        }
    }

    /// <summary>
    /// Clears the in-memory log buffer.
    /// </summary>
    public void ClearMemory()
    {
        lock (_memoryLock)
        {
            _memoryLog.Clear();
            _collapseCounts.Clear();
        }
    }

    private string? GetCollapsiblePrefix(string message)
    {
        foreach (var prefix in CollapsiblePrefixes)
        {
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return prefix;
        }
        return null;
    }

    private void Write(string level, string message)
    {
        var collapseKey = GetCollapsiblePrefix(message);
        bool replacing = false;

        // Add to in-memory buffer
        lock (_memoryLock)
        {
            if (collapseKey != null)
            {
                _collapseCounts.TryGetValue(collapseKey, out var count);
                count++;
                _collapseCounts[collapseKey] = count;

                var line = $"[{DateTime.UtcNow:O}] [{level}] {message} (x{count})";

                // Replace previous collapsed entry if it exists
                if (count > 1)
                {
                    for (int i = _memoryLog.Count - 1; i >= 0; i--)
                    {
                        if (_memoryLog[i].Contains(collapseKey))
                        {
                            _memoryLog[i] = line;
                            replacing = true;
                            break;
                        }
                    }
                }

                if (!replacing)
                {
                    if (_memoryLog.Count >= MaxMemoryEntries)
                        _memoryLog.RemoveAt(0);
                    _memoryLog.Add(line);
                }

                try { LogAdded?.Invoke(line, replacing); } catch { }
            }
            else
            {
                var line = $"[{DateTime.UtcNow:O}] [{level}] {message}";

                if (_memoryLog.Count >= MaxMemoryEntries)
                    _memoryLog.RemoveAt(0);
                _memoryLog.Add(line);

                try { LogAdded?.Invoke(line, false); } catch { }
            }
        }

        // Write to file (always, uncollapsed)
        var fileLine = $"[{DateTime.UtcNow:O}] [{level}] {message}";
        try
        {
            lock (_fileLock)
            {
                RotateIfNeeded();
                File.AppendAllText(_logPath, fileLine + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Swallow file logging errors to avoid impacting application behavior
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath)) return;

            var info = new FileInfo(_logPath);
            if (info.Length < MaxLogFileSize) return;

            var backupPath = _logPath + ".1";
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(_logPath, backupPath);
        }
        catch
        {
            // Ignore rotation errors
        }
    }
}
