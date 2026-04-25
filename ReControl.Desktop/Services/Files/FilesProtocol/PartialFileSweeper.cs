using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Background sweeper that reaps orphan <c>*.partial.*</c> files left behind
/// by browser page-refresh, process kill, or any path that bypassed the
/// normal cancel / complete flow.
///
/// Tick cadence: 60s. Orphan threshold: 5 minutes since last write. Both
/// values are pinned by 11-RESEARCH and the success criteria of Plan 11-02
/// (Pitfall 10).
///
/// Scope: only enumerates directories in
/// <see cref="TransferRegistry.KnownParentDirs"/>. This means we only
/// sweep directories the desktop has already touched in this process
/// lifetime; an arbitrary allowlisted directory the user never wrote into
/// is NEVER walked. Cheap and safe.
///
/// Deletion guard: any path present in
/// <see cref="TransferRegistry.ActivePartialPaths"/> is skipped, so a
/// stalled-but-still-tracked upload is never reaped while it is still
/// owned by an UploadReceiver. The 5-minute mtime threshold then governs
/// the orphan case (the upload's owner is gone).
/// </summary>
public sealed class PartialFileSweeper : IDisposable
{
    private const int TickIntervalMs = 60_000;
    private static readonly TimeSpan OrphanThreshold = TimeSpan.FromMinutes(5);

    private readonly TransferRegistry _registry;
    private readonly LogService _log;
    private readonly Timer _timer;
    private bool _disposed;

    public PartialFileSweeper(TransferRegistry registry, LogService log)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _timer = new Timer(OnTick, null, TickIntervalMs, TickIntervalMs);
    }

    private void OnTick(object? _)
    {
        if (_disposed) return;
        try
        {
            // Snapshot once per tick so concurrent registrations don't
            // distort the active-paths check.
            var active = new HashSet<string>(_registry.ActivePartialPaths, StringComparer.Ordinal);
            var cutoff = DateTime.UtcNow - OrphanThreshold;
            var enumOpts = new EnumerationOptions
            {
                MatchType = MatchType.Win32,
                MatchCasing = MatchCasing.PlatformDefault,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Device,
                RecurseSubdirectories = false
            };

            foreach (var dir in _registry.KnownParentDirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*.partial.*", enumOpts))
                    {
                        if (active.Contains(file)) continue;
                        try
                        {
                            var mtime = File.GetLastWriteTimeUtc(file);
                            if (mtime < cutoff)
                            {
                                File.Delete(file);
                                _log.Info($"partial-sweeper: reaped orphan {file} (mtime={mtime:O})");
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.Warning(
                                $"partial-sweeper: delete failed for {file}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(
                        $"partial-sweeper: enumerate failed for {dir}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("partial-sweeper: tick threw", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _timer.Dispose(); } catch { /* ignore */ }
    }
}
