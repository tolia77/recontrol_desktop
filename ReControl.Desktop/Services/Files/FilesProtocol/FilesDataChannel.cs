using SIPSorcery.Net;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Phase-9 stub for the <c>files-data</c> binary WebRTC data channel.
///
/// Parses the 16-byte <see cref="ChunkHeader"/> prefix on every inbound
/// message and logs the triple + payload length. Actual routing to an
/// in-flight transfer is Phase 11's work.
///
/// Lifecycle note (per 09-SPIKE-FINDINGS Spike C): do NOT call dc.close()
/// on this side. Resource cleanup is driven by pc.close() on the
/// RTCPeerConnection.
///
/// TODO(Phase 11): when the sender lives on this side, gate the send loop
/// with Recommendation A from 09-SPIKE-FINDINGS: poll
/// <c>RTCDataChannel.bufferedAmount</c> after every
/// <c>channel.send(chunk)</c> and pause while it exceeds HIGH_WATER=4 MiB;
/// resume once it drains below LOW_WATER=1 MiB.
/// </summary>
public sealed class FilesDataChannel
{
    private readonly RTCDataChannel _dc;
    private readonly LogService _log;

    public FilesDataChannel(RTCDataChannel dc, LogService log)
    {
        _dc = dc;
        _log = log;
        _dc.onmessage += OnMessage;
    }

    private void OnMessage(RTCDataChannel ch, DataChannelPayloadProtocols type, byte[] data)
    {
        if (data.Length < ChunkHeader.Size)
        {
            _log.Warning($"files-data: chunk too short ({data.Length} bytes)");
            return;
        }
        var header = ChunkHeader.Read(data);
        _log.Info(
            $"files-data: chunk transferId={header.TransferId} seq={header.Seq} " +
            $"offset={header.Offset} payloadBytes={data.Length - ChunkHeader.Size} " +
            $"(Phase 9 stub, dropped)");
    }
}
