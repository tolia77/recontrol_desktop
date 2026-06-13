using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReControl.Desktop.Services;

/// <summary>
/// A single timing sample enqueued on the hot path (no lock, no file write).
/// All integer durations are in microseconds unless the field name says otherwise.
/// </summary>
public readonly struct TimingEntry
{
    /// <summary>Monotonic frame sequence counter assigned by the capture loop.</summary>
    public long Seq { get; init; }

    /// <summary>Logical area tag, e.g. "webrtc", "command", "terminal".</summary>
    public string Area { get; init; }

    /// <summary>Event name within the area, e.g. "frame_capture", "frame_encode".</summary>
    public string Event { get; init; }

    /// <summary>Screen-capture stage duration in microseconds.</summary>
    public int CaptureUs { get; init; }

    /// <summary>FFmpeg encode stage duration in microseconds.</summary>
    public int EncodeUs { get; init; }

    /// <summary>Frame-scale stage duration in microseconds (0 if scaling skipped).</summary>
    public int ScaleUs { get; init; }

    /// <summary>Encoded payload size in bytes.</summary>
    public int SentBytes { get; init; }

    /// <summary>Total per-frame pipeline duration in microseconds (optional summary).</summary>
    public int DurationUs { get; init; }

    /// <summary>
    /// Optional key=value metric params (D-06 operation params).
    /// MUST NOT contain terminal I/O, clipboard contents, or file contents.
    /// Carry metric/identifier data only (e.g. "res=1920x1080 codec=H264").
    /// </summary>
    public string? ParamsText { get; init; }
}

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

    // Lock-free timing accumulator — safe to Enqueue from the capture thread at frame rate.
    // Drained by DrainTiming() once per second on the stats tick (never on the hot path).
    private readonly ConcurrentQueue<TimingEntry> _timingQueue = new();

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

    // -------------------------------------------------------------------------
    // Hot-path timing channel (lock-free accumulate, drain on stats tick)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enqueue a timing sample from the capture loop.
    /// This method is lock-free and MUST NOT be changed to acquire _fileLock,
    /// call File.AppendAllText, or call Write/Info/Debug — doing so would add
    /// 0.5–5 ms of file-write overhead to every captured frame (RESEARCH Pitfall 1).
    /// </summary>
    public void EnqueueTiming(in TimingEntry entry)
    {
        _timingQueue.Enqueue(entry);
    }

    /// <summary>
    /// Drain all pending timing entries, compute per-(area,event) aggregates, and write
    /// one structured JSONL line per group through the existing rotating file sink.
    /// Called by WebRtcService on its 1-second stats tick — NOT on the hot path.
    /// </summary>
    public void DrainTiming()
    {
        // Dequeue all pending entries into a local list.
        var entries = new List<TimingEntry>();
        while (_timingQueue.TryDequeue(out var entry))
            entries.Add(entry);

        if (entries.Count == 0)
            return;

        // Group by (area, event) and compute min/mean/max for each numeric metric.
        var groups = entries
            .GroupBy(e => (e.Area ?? string.Empty, e.Event ?? string.Empty));

        foreach (var group in groups)
        {
            var list = group.ToList();
            int count = list.Count;

            // Only aggregate fields that have at least one non-zero value, so JSONL lines
            // produced by non-streaming areas (where CaptureUs/EncodeUs are 0) stay compact.
            int minCapture = list.Min(e => e.CaptureUs);
            int maxCapture = list.Max(e => e.CaptureUs);
            int meanCapture = (int)list.Average(e => e.CaptureUs);

            int minEncode = list.Min(e => e.EncodeUs);
            int maxEncode = list.Max(e => e.EncodeUs);
            int meanEncode = (int)list.Average(e => e.EncodeUs);

            int minScale = list.Min(e => e.ScaleUs);
            int maxScale = list.Max(e => e.ScaleUs);
            int meanScale = (int)list.Average(e => e.ScaleUs);

            int minBytes = list.Min(e => e.SentBytes);
            int maxBytes = list.Max(e => e.SentBytes);
            int meanBytes = (int)list.Average(e => e.SentBytes);

            int minDuration = list.Min(e => e.DurationUs);
            int maxDuration = list.Max(e => e.DurationUs);
            int meanDuration = (int)list.Average(e => e.DurationUs);

            long minSeq = list.Min(e => e.Seq);
            long maxSeq = list.Max(e => e.Seq);

            var aggregate = new
            {
                t = DateTime.UtcNow.ToString("O"),
                area = group.Key.Item1,
                @event = group.Key.Item2,
                count,
                seqMin = minSeq,
                seqMax = maxSeq,
                captureUs = new { min = minCapture, mean = meanCapture, max = maxCapture },
                encodeUs = new { min = minEncode, mean = meanEncode, max = maxEncode },
                scaleUs = new { min = minScale, mean = meanScale, max = maxScale },
                sentBytes = new { min = minBytes, mean = meanBytes, max = maxBytes },
                durationUs = new { min = minDuration, mean = meanDuration, max = maxDuration },
            };

            var jsonLine = JsonSerializer.Serialize(aggregate);
            Write("TIMING", jsonLine);
        }
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
