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

    private readonly string _logPath;
    private readonly object _fileLock = new();
    private readonly object _memoryLock = new();
    private readonly List<string> _memoryLog = new(MaxMemoryEntries);

    /// <summary>
    /// Raised when a new log entry is added to the in-memory buffer.
    /// </summary>
    public event Action<string>? LogAdded;

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
        }
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.UtcNow:O}] [{level}] {message}";

        // Add to in-memory buffer
        lock (_memoryLock)
        {
            if (_memoryLog.Count >= MaxMemoryEntries)
            {
                _memoryLog.RemoveAt(0);
            }
            _memoryLog.Add(line);
        }

        try { LogAdded?.Invoke(line); } catch { }

        // Write to file
        try
        {
            lock (_fileLock)
            {
                RotateIfNeeded();
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
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
