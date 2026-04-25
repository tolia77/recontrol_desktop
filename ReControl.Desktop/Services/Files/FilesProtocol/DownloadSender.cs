using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Per-download sender: opens a read-only FileStream, frames 16 KiB chunks
/// with a 16-byte <see cref="ChunkHeader"/>, and pushes them over the
/// <c>files-data</c> RTCDataChannel. Uses Recommendation A
/// (desktop-side polled bufferedAmount) backpressure from
/// <c>09-SPIKE-FINDINGS.md</c>: pause when bufferedAmount &gt; 4 MiB; resume
/// when it drains below 1 MiB. Polls every 5 ms.
///
/// Pitfall mitigations (from 11-RESEARCH):
///   - <b>Pitfall 1</b> (trailing garbage on final short chunk): the final
///     send slices the buffer to exactly <c>16 + read</c> bytes so the
///     receiver does not see padding from the previous full chunk. Full
///     chunks reuse the original buffer for free.
///   - <b>Pitfall 2</b> (dc.send on closing channel): every send is
///     guarded by a readyState != open check; SIPSorcery may also throw
///     from <c>send()</c>, which is caught and treated as a
///     transfer.error path.
///
/// On EOF the sender pushes a <c>files.download.complete</c> event over
/// files-ctl with the transferId and total bytes. On any failure
/// (cancellation, IO error, channel close) the entry self-removes from
/// the registry so a subsequent files.transfer.cancel call does not
/// double-cancel; if the failure was NOT a user-initiated cancel a
/// <c>files.transfer.error</c> event is also pushed.
/// </summary>
public sealed class DownloadSender : ITransferEntry
{
    // 09-SPIKE-FINDINGS Recommendation A constants. Phase 11 RESEARCH
    // mandates these exact values; do NOT tune without re-running Spike B.
    private const int CHUNK_PAYLOAD = 16 * 1024;          // 16 KiB
    private const int HIGH_WATER = 4 * 1024 * 1024;       // 4 MiB
    private const int LOW_WATER = 1 * 1024 * 1024;        // 1 MiB
    private const int POLL_DELAY_MS = 5;

    private readonly uint _transferId;
    private readonly string _path;
    private readonly long _size;
    private readonly RTCDataChannel _filesData;
    private readonly FilesCtlChannel _ctl;
    private readonly LogService _log;
    private readonly TransferRegistry _registry;
    private readonly CancellationTokenSource _cts = new();

    private long _bytesSent;
    private long _lastBytesSentAtTicks;

    public uint TransferId => _transferId;
    public long Size => _size;
    public long BytesSent => _bytesSent;

    /// <summary>
    /// UTC ticks of the most recent successful send. Read by Plan 11-06's
    /// stall detector; 0 until the first chunk is dispatched.
    /// </summary>
    public long LastBytesSentAtTicks => _lastBytesSentAtTicks;

    public DownloadSender(
        uint transferId,
        string canonicalPath,
        long size,
        RTCDataChannel filesData,
        FilesCtlChannel ctl,
        LogService log,
        TransferRegistry registry)
    {
        _transferId = transferId;
        _path = canonicalPath ?? throw new ArgumentNullException(nameof(canonicalPath));
        _size = size;
        _filesData = filesData ?? throw new ArgumentNullException(nameof(filesData));
        _ctl = ctl ?? throw new ArgumentNullException(nameof(ctl));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Main send loop. Allocates one 16 + 16 KiB scratch buffer; writes the
    /// header in place over each chunk. On the FINAL short chunk, the send
    /// is sliced to <c>16 + read</c> bytes so trailing garbage from the
    /// previous full chunk is not transmitted (Pitfall 1).
    ///
    /// Catches all exceptions internally and emits a single
    /// <c>files.transfer.error</c> event (unless the cancel was
    /// user-initiated, in which case the cancel side is responsible for any
    /// UI feedback). Always removes itself from the registry in the finally
    /// block.
    /// </summary>
    public async Task RunAsync()
    {
        FileStream? reader = null;
        var ct = _cts.Token;
        try
        {
            reader = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            var buf = new byte[ChunkHeader.Size + CHUNK_PAYLOAD];
            uint seq = 0;
            ulong offset = 0;

            while (!ct.IsCancellationRequested)
            {
                int read = await reader.ReadAsync(buf.AsMemory(ChunkHeader.Size, CHUNK_PAYLOAD), ct);
                if (read == 0) break;  // EOF

                // Frame the chunk header in place (transferId / seq / offset).
                new ChunkHeader(_transferId, seq, offset)
                    .WriteTo(buf.AsSpan(0, ChunkHeader.Size));

                // Backpressure: Recommendation A poll. If readyState falls
                // out of open while we're parked in this loop, break out and
                // let the catch path emit an error.
                while (_filesData.bufferedAmount > HIGH_WATER && !ct.IsCancellationRequested)
                {
                    if (_filesData.readyState != RTCDataChannelState.open)
                        throw new InvalidOperationException("files-data channel is no longer open");
                    await Task.Delay(POLL_DELAY_MS, ct);
                    // Drain target is LOW_WATER; we resume sending as soon
                    // as bufferedAmount falls below HIGH_WATER again. The
                    // LOW_WATER constant exists so the next chunk does not
                    // immediately re-trigger the pause; the difference
                    // between HIGH_WATER and LOW_WATER is the hysteresis.
                    if (_filesData.bufferedAmount <= LOW_WATER) break;
                }

                if (ct.IsCancellationRequested) break;

                // Pitfall 2: never call send on a closing channel.
                if (_filesData.readyState != RTCDataChannelState.open)
                    throw new InvalidOperationException("files-data channel is no longer open");

                // Pitfall 1: send the EXACT framed length on the final short
                // chunk. Full chunks reuse buf as-is.
                if (read == CHUNK_PAYLOAD)
                {
                    _filesData.send(buf);
                }
                else
                {
                    var exact = new byte[ChunkHeader.Size + read];
                    Buffer.BlockCopy(buf, 0, exact, 0, ChunkHeader.Size + read);
                    _filesData.send(exact);
                }

                seq++;
                offset += (ulong)read;
                _bytesSent += read;
                _lastBytesSentAtTicks = DateTime.UtcNow.Ticks;
            }

            if (ct.IsCancellationRequested)
            {
                // User-initiated cancel: do NOT push transfer.error; the
                // cancel handler already returned a success envelope and
                // the browser owns the UI feedback.
                _log.Info($"download {_transferId}: cancelled after {_bytesSent}/{_size} bytes");
                return;
            }

            // EOF reached cleanly. Announce completion via files-ctl event.
            await _ctl.PushEventAsync("files.download.complete", new
            {
                transferId = _transferId,
                totalBytes = (long)offset
            });
            _log.Info($"download {_transferId}: complete ({offset} bytes)");
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            _log.Info($"download {_transferId}: cancelled (token)");
        }
        catch (Exception ex)
        {
            _log.Warning($"download {_transferId}: error {ex.GetType().Name}: {ex.Message}");
            try
            {
                await _ctl.PushEventAsync("files.transfer.error", new
                {
                    transferId = _transferId,
                    error = new { code = "IO_ERROR", message = ex.Message }
                });
            }
            catch { /* swallow: error-push best effort */ }
        }
        finally
        {
            try { reader?.Dispose(); } catch { /* ignore */ }
            _registry.Remove(_transferId);
        }
    }

    /// <summary>
    /// Cancel the active send loop. Idempotent. The actual FileStream
    /// disposal happens in the RunAsync finally block; this method only
    /// signals the CTS.
    /// </summary>
    public void Cancel()
    {
        try { _cts.Cancel(); } catch { /* already disposed */ }
    }
}
