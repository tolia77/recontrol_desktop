using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ReControl.Desktop.Services;

/// <summary>
/// Combined file and in-memory logging service.
/// Consecutive log entries with the same shape are collapsed into one entry with a count.
/// </summary>
public sealed class LogService
{
    private const int MaxMemoryEntries = 1000;
    private const long MaxLogFileSize = 5 * 1024 * 1024; // 5MB

    private readonly string _logPath;
    private readonly object _fileLock = new();
    private readonly object _memoryLock = new();
    private readonly List<string> _memoryLog = new(MaxMemoryEntries);

    // Collapse tracking: consecutive identical-shape messages replace the last entry
    private string? _lastCollapseKey;
    private int _lastCollapseCount;

    // Strips UUIDs, hex strings, and numbers to produce a stable "shape" for comparison
    private static readonly Regex CollapseRegex = new(
        @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|\b\d+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Raised when a log entry is added or updated in the in-memory buffer.
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
            logDir = Path.GetTempPath();
        }

        _logPath = Path.Combine(logDir, "recontrol.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Debug(string message) => Write("DEBUG", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(string context, Exception ex) =>
        Write("ERROR", $"{context}: {ex}");

    public IReadOnlyList<string> Snapshot()
    {
        lock (_memoryLock)
        {
            return _memoryLog.ToArray();
        }
    }

    public void ClearMemory()
    {
        lock (_memoryLock)
        {
            _memoryLog.Clear();
            _lastCollapseKey = null;
            _lastCollapseCount = 0;
        }
    }

    private static string GetCollapseKey(string message)
    {
        return CollapseRegex.Replace(message, "#");
    }

    private void Write(string level, string message)
    {
        var key = GetCollapseKey(message);

        lock (_memoryLock)
        {
            if (key == _lastCollapseKey && _memoryLog.Count > 0)
            {
                // Same shape as previous — replace last entry, bump count
                _lastCollapseCount++;
                var line = $"[{DateTime.UtcNow:O}] [{level}] {message} (x{_lastCollapseCount})";
                _memoryLog[_memoryLog.Count - 1] = line;
                try { LogAdded?.Invoke(line, true); } catch { }
            }
            else
            {
                // Different shape — new entry
                _lastCollapseKey = key;
                _lastCollapseCount = 1;
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
            // Swallow file logging errors
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
