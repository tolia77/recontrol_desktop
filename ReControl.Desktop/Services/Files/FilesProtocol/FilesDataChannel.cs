using System;
using System.Threading;
using SIPSorcery.Net;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Inbound side of the <c>files-data</c> binary WebRTC data channel.
///
/// Phase 11: parses the 16-byte <see cref="ChunkHeader"/> prefix on every
/// inbound message, looks up the matching <see cref="UploadReceiver"/> in
/// the <see cref="TransferRegistry"/>, and forwards the payload bytes.
///
/// Routing rules:
///   - data.Length &lt; 16 -> WARN + drop (corrupt frame).
///   - header.TransferId not in registry -> DEBUG + drop (post-cancel
///     race; the browser still had pending sends in-flight when the
///     cancel ack landed).
///   - registry entry is a DownloadSender, not an UploadReceiver -> WARN
///     + drop (impossible in normal flow; would indicate a producer bug).
///   - UploadReceiver.OnChunkAsync threw -> log + drop (the receiver
///     itself already pushed a transfer.error event and called Cancel,
///     so no further work here).
///
/// Lifecycle note (per 09-SPIKE-FINDINGS Spike C): do NOT call dc.close()
/// on this side. Resource cleanup is driven by pc.close() on the
/// RTCPeerConnection; the registry's CancelAll cleans up every
/// UploadReceiver / DownloadSender in one pass.
/// </summary>
public sealed class FilesDataChannel
{
    private readonly RTCDataChannel _dc;
    private readonly LogService _log;
    private readonly TransferRegistry _registry;

    public FilesDataChannel(RTCDataChannel dc, LogService log, TransferRegistry registry)
    {
        _dc = dc ?? throw new ArgumentNullException(nameof(dc));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dc.onmessage += OnMessage;
    }

    private async void OnMessage(RTCDataChannel ch, DataChannelPayloadProtocols type, byte[] data)
    {
        if (data.Length < ChunkHeader.Size)
        {
            _log.Warning($"files-data: chunk too short ({data.Length} bytes)");
            return;
        }

        ChunkHeader header;
        try
        {
            header = ChunkHeader.Read(data);
        }
        catch (Exception ex)
        {
            _log.Warning($"files-data: header parse failed: {ex.Message}");
            return;
        }

        if (!_registry.TryGet(header.TransferId, out var entry))
        {
            _log.Debug(
                $"files-data: chunk for unknown transferId={header.TransferId} (post-cancel race; dropped)");
            return;
        }

        if (entry is not UploadReceiver up)
        {
            _log.Warning(
                $"files-data: registry entry for transferId={header.TransferId} is not an UploadReceiver (got {entry.GetType().Name}); dropped");
            return;
        }

        try
        {
            var payload = data.AsMemory(ChunkHeader.Size, data.Length - ChunkHeader.Size);
            await up.OnChunkAsync(header, payload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // UploadReceiver.OnChunkAsync already pushed transfer.error and
            // called Cancel on its IOException path. The async-void callback
            // contract requires us to swallow here; rethrowing would crash
            // the SIPSorcery dispatch thread.
            _log.Warning(
                $"files-data: chunk handler threw for transferId={header.TransferId}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
