using System;
using System.Threading;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Background timer that watches every active <see cref="UploadReceiver"/>
/// in the <see cref="TransferRegistry"/> and pushes a single
/// <c>files.transfer.error</c> event with code <c>STALLED</c> for any receiver
/// that has gone idle for more than <see cref="STALL_THRESHOLD_TICKS"/>
/// milliseconds. Plan 11-06.
///
/// Threshold + cadence (RESEARCH-locked):
///   - Tick every 1 s (<see cref="TICK_PERIOD_MS"/>): inexpensive; matches the
///     browser-side download stall interval.
///   - Stall threshold 10 s (<see cref="STALL_THRESHOLD_TICKS"/>): the receiver
///     stamps <see cref="UploadReceiver.LastBytesWrittenAtTicks"/> from
///     <see cref="Environment.TickCount64"/> on every successful write, so the
///     comparison is always milliseconds-on-milliseconds.
///
/// One push per stall episode: <see cref="UploadReceiver.StalledNotified"/> is
/// set to true the first time the monitor pushes for a receiver; the receiver
/// itself clears the flag inside its next successful <c>OnChunkAsync</c>. This
/// guarantees STALLED fires exactly once until bytes flow again, then is armed
/// for re-firing if the transfer stalls a second time.
///
/// FilesCtlChannel is supplied via a <see cref="Func{FilesCtlChannel}"/> getter
/// because the channel can be null between connections; the monitor's lifetime
/// is owned by <see cref="WebRtcService"/> and survives the channel reference.
/// When the getter returns null (e.g. during pc.close), the tick is a no-op.
///
/// Lifecycle: constructed once per peer connection in
/// <c>WebRtcService.HandleOfferAsync</c> alongside the registry+sweeper; disposed
/// in <c>WebRtcService.CleanupPeerConnection</c> AFTER
/// <c>TransferRegistry.CancelAll</c> so a final tick cannot race a half-cancelled
/// receiver.
/// </summary>
public sealed class StallMonitor : IDisposable
{
    /// <summary>
    /// Idle threshold in milliseconds. A receiver whose
    /// <see cref="UploadReceiver.LastBytesWrittenAtTicks"/> is older than this
    /// (relative to <see cref="Environment.TickCount64"/>) is considered stalled.
    /// </summary>
    private const long STALL_THRESHOLD_TICKS = 10_000;

    /// <summary>
    /// Background timer cadence in milliseconds.
    /// </summary>
    private const int TICK_PERIOD_MS = 1_000;

    private readonly TransferRegistry _registry;
    private readonly Func<FilesCtlChannel?> _getCtl;
    private readonly LogService _log;
    private readonly Timer _timer;
    private bool _disposed;

    public StallMonitor(TransferRegistry registry, Func<FilesCtlChannel?> getCtl, LogService log)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _getCtl = getCtl ?? throw new ArgumentNullException(nameof(getCtl));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _timer = new Timer(_ => Tick(), null, TICK_PERIOD_MS, TICK_PERIOD_MS);
    }

    /// <summary>
    /// One tick: enumerate active uploads, push STALLED for any receiver idle
    /// past the threshold and not already notified. Wrapped in a try/catch so
    /// a throwing receiver cannot kill the timer thread.
    /// </summary>
    private void Tick()
    {
        if (_disposed) return;
        var ctl = _getCtl();
        if (ctl is null) return;

        var nowTicks = Environment.TickCount64;
        try
        {
            foreach (var up in _registry.ActiveUploads)
            {
                if (up.StalledNotified) continue;
                // First chunk has not landed yet -- skip rather than fire on
                // the LastBytesWrittenAtTicks=0 sentinel.
                if (up.LastBytesWrittenAtTicks == 0) continue;
                if (nowTicks - up.LastBytesWrittenAtTicks <= STALL_THRESHOLD_TICKS) continue;

                up.StalledNotified = true;
                try
                {
                    _ = ctl.PushEventAsync("files.transfer.error", new
                    {
                        transferId = up.TransferId,
                        error = new
                        {
                            code = "STALLED",
                            message = "No bytes received for 10 seconds"
                        }
                    });
                    _log.Info($"StallMonitor: pushed STALLED for upload {up.TransferId}");
                }
                catch (Exception ex)
                {
                    _log.Warning($"StallMonitor: PushEventAsync failed for {up.TransferId}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"StallMonitor: tick failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
    }
}
