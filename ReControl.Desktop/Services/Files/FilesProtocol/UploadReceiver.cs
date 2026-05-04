using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Per-upload receiver: opens a write-only FileStream at the .partial path,
/// streams chunks delivered by <see cref="FilesDataChannel"/>, validates the
/// total byte count on complete, and atomically renames .partial -> final on
/// the same volume.
///
/// Pitfall mitigations (from 11-RESEARCH):
///   - <b>Pitfall 9</b> (cross-volume File.Move): the .partial path is ALWAYS
///     a sibling of the final destination. The caller (begin handler) builds
///     <c>partialPath = $"{finalPath}.partial.{transferId}"</c>; this guarantees
///     same-volume File.Move, which is atomic on every supported OS.
///   - <b>Pitfall 7</b> (disk full mid-write): WriteAsync IOException catches
///     push <c>files.transfer.error</c> with code DISK_FULL or IO_ERROR
///     (heuristic on the message text -- locale-dependent on Windows but
///     defensible because the canonical fallback is IO_ERROR).
///   - <b>Stream open race</b>: <see cref="FileMode.CreateNew"/> +
///     <see cref="FileShare.None"/> guarantees a fresh exclusive stream;
///     if the .partial already exists from a prior aborted attempt the
///     constructor throws IOException, which the begin handler maps to
///     IO_ERROR (Phase 11 has no resume).
/// </summary>
public sealed class UploadReceiver : ITransferEntry, IDisposable
{
    private readonly uint _transferId;
    private readonly string _partialPath;
    private readonly string _finalPath;
    private readonly long _expectedSize;
    private readonly bool _allowOverwrite;
    private readonly FilesCtlChannel? _ctlForErrors;
    private readonly LogService _log;

    private FileStream? _writer;
    private long _bytesWritten;
    private long _lastBytesWrittenAtTicks;
    private bool _cancelled;
    // FilesDataChannel.OnMessage is async void: SIPSorcery fires chunk events
    // back-to-back without awaiting, so multiple OnChunkAsync calls can be in
    // flight on the same receiver. FileStream is not thread-safe, so concurrent
    // WriteAsync corrupts _writer.Position and the offset sanity check fires
    // spuriously. Serialize chunk processing per-receiver.
    private readonly SemaphoreSlim _chunkGate = new(1, 1);

    public string PartialPath => _partialPath;
    public string FinalPath => _finalPath;
    public uint TransferId => _transferId;
    public long BytesWritten => _bytesWritten;

    /// <summary>
    /// <see cref="Environment.TickCount64"/> snapshot at the most recent
    /// successful WriteAsync. Read by Plan 11-06's StallMonitor with a 10_000 ms
    /// threshold (TickCount64 is in milliseconds). 0 until the first chunk lands.
    /// </summary>
    public long LastBytesWrittenAtTicks => _lastBytesWrittenAtTicks;

    /// <summary>
    /// True if the StallMonitor has already pushed a files.transfer.error
    /// STALLED for this receiver's CURRENT idle episode. Cleared on the
    /// next chunk arrival in OnChunkAsync. Prevents duplicate STALLED
    /// pushes during a multi-second stall. Plan 11-06.
    /// </summary>
    public bool StalledNotified { get; set; }

    public UploadReceiver(
        uint transferId,
        string partialPath,
        string finalPath,
        long expectedSize,
        FilesCtlChannel? ctlForErrors,
        LogService log,
        bool allowOverwrite = false)
    {
        _transferId = transferId;
        _partialPath = partialPath ?? throw new ArgumentNullException(nameof(partialPath));
        _finalPath = finalPath ?? throw new ArgumentNullException(nameof(finalPath));
        _expectedSize = expectedSize;
        _allowOverwrite = allowOverwrite;
        _ctlForErrors = ctlForErrors;
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // Exclusive create: throws IOException if .partial already exists.
        // FileShare.None blocks any other process from opening the file
        // while we hold it. useAsync:true enables true async I/O on Windows
        // (overlapped I/O); on Linux it's effectively the same as sync but
        // doesn't hurt.
        _writer = new FileStream(
            _partialPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
    }

    /// <summary>
    /// Append a chunk's payload bytes to the .partial file. Sanity-checks
    /// header.Offset against the writer's current position (defence-in-depth:
    /// the data channel is reliable+ordered so this should never fire).
    /// On any IOException the receiver pushes a transfer.error event with
    /// either DISK_FULL (heuristic) or IO_ERROR, then cancels itself and
    /// rethrows so <see cref="FilesDataChannel.OnMessage"/> stops feeding chunks.
    /// </summary>
    public async Task OnChunkAsync(ChunkHeader header, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        await _chunkGate.WaitAsync(ct);
        try
        {
            if (_writer is null) throw new ObjectDisposedException(nameof(UploadReceiver));

            // Defence-in-depth: a reliable+ordered channel guarantees in-order
            // delivery, but a header.Offset mismatch indicates a producer bug.
            var position = _writer.Position;
            if ((long)header.Offset != position)
            {
                _log.Warning(
                    $"upload {_transferId}: offset mismatch -- header.Offset={header.Offset} stream.Position={position}");
                throw new IOException(
                    $"offset mismatch: header.Offset={header.Offset} writer.Position={position}");
            }

            try
            {
                await _writer.WriteAsync(payload, ct);
                _bytesWritten += payload.Length;
                _lastBytesWrittenAtTicks = Environment.TickCount64;
                // Plan 11-06: a successful chunk arrival ENDS the current stall
                // episode, so the StallMonitor is free to push another STALLED
                // event the next time this receiver goes idle for 10s+.
                if (StalledNotified) StalledNotified = false;
            }
            catch (IOException ex)
            {
                // Pitfall 7: disk filled up mid-write. Heuristic message check;
                // locale-dependent on Windows but the IO_ERROR fallback is safe.
                var msg = ex.Message ?? "";
                var isDiskFull = msg.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0
                              || msg.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0
                              || msg.IndexOf("ENOSPC", StringComparison.OrdinalIgnoreCase) >= 0;
                var code = isDiskFull ? "DISK_FULL" : "IO_ERROR";
                _log.Warning($"upload {_transferId}: write failed ({code}): {msg}");
                try
                {
                    if (_ctlForErrors is not null)
                    {
                        await _ctlForErrors.PushEventAsync("files.transfer.error", new
                        {
                            transferId = _transferId,
                            error = new { code, message = msg }
                        });
                    }
                }
                catch { /* swallow: error-push best effort */ }
                Cancel();
                throw;
            }
        }
        finally
        {
            _chunkGate.Release();
        }
    }

    /// <summary>
    /// Close the writer, assert byte count, and atomically rename
    /// .partial -> final via <see cref="File.Move(string, string, bool)"/>
    /// (overwrite:false, throws if the final already exists -- Phase 11 has
    /// no name-conflict resolution). Returns the final path on success.
    /// On byte-count mismatch the .partial is deleted and an IOException is
    /// thrown so the caller maps to IO_ERROR.
    /// </summary>
    public async Task<string> CompleteAsync(long expectedBytes)
    {
        if (_writer is null) throw new ObjectDisposedException(nameof(UploadReceiver));

        await _writer.FlushAsync();
        _writer.Close();
        _writer = null;

        if (_bytesWritten != expectedBytes)
        {
            try { File.Delete(_partialPath); } catch { /* best effort */ }
            throw new IOException(
                $"size mismatch: wrote {_bytesWritten} bytes, expected {expectedBytes}");
        }

        // Pitfall 9: same-volume rename = atomic. overwrite:false keeps us
        // honest if a sibling appeared while the upload was running. When the
        // caller opted into NameConflictMode.Replace at upload.begin time,
        // _allowOverwrite is true so File.Move clobbers any existing entry
        // at the final path (Plan 12-02).
        File.Move(_partialPath, _finalPath, overwrite: _allowOverwrite);
        return _finalPath;
    }

    /// <summary>
    /// Idempotent: closes the writer (if open) and deletes the .partial file.
    /// Safe to call multiple times. Invoked from
    /// <c>files.transfer.cancel</c>, from <see cref="TransferRegistry.CancelAll"/>
    /// during pc.close, and from <see cref="OnChunkAsync"/> on write failure.
    /// </summary>
    public void Cancel()
    {
        if (_cancelled) return;
        _cancelled = true;

        try { _writer?.Close(); }
        catch (Exception ex) { _log.Warning($"upload {_transferId}: close threw {ex.GetType().Name}: {ex.Message}"); }
        _writer = null;

        try
        {
            if (File.Exists(_partialPath))
                File.Delete(_partialPath);
        }
        catch (Exception ex)
        {
            _log.Warning($"upload {_transferId}: delete .partial threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Cancel();
        _chunkGate.Dispose();
    }
}
