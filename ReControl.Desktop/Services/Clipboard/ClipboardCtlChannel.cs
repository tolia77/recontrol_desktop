using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ReControl.Desktop.Protocol.Generated;
using SIPSorcery.Net;

namespace ReControl.Desktop.Services.Clipboard;

/// <summary>
/// Desktop-side dispatcher for the <c>clipboard</c> WebRTC data channel.
/// Created from WebRtcService <c>ondatachannel</c> handling.
///
/// Do NOT call <c>dc.close()</c> from here; channel teardown is driven by pc.close().
/// </summary>
public sealed class ClipboardCtlChannel
{
    private readonly RTCDataChannel _dc;
    private readonly LogService _log;
    private readonly ClipboardSyncService _syncService;
    private readonly Func<bool>? _accessClipboard;

    public ClipboardCtlChannel(RTCDataChannel dc, LogService log, ClipboardSyncService syncService, Func<bool>? accessClipboard = null)
    {
        _dc = dc ?? throw new ArgumentNullException(nameof(dc));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _accessClipboard = accessClipboard;
        _dc.onmessage += OnMessage;
    }

    private void OnMessage(RTCDataChannel channel, DataChannelPayloadProtocols payloadType, byte[] data)
    {
        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(data);
        }
        catch
        {
            _log.Warning("clipboard: non-UTF8 message ignored");
            return;
        }

        _ = DispatchEnvelopeAsync(raw, _syncService, SendRefusedAsync, _log, _accessClipboard);
    }

    public void Send<TEnvelope>(TEnvelope envelope)
    {
        var json = ClipboardEnvelope.Serialize(envelope);
        try
        {
            _dc.send(json);
        }
        catch (Exception ex)
        {
            _log.Error("clipboard: send failed", ex);
        }
    }

    public static async Task DispatchEnvelopeAsync(
        string raw,
        ClipboardSyncService syncService,
        Func<ClipboardRefusedEnvelope, Task> sendRefused,
        LogService log,
        Func<bool>? accessClipboard = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("kind", out var kindProp))
            {
                log.Warning("clipboard: malformed message missing kind");
                return;
            }

            var kind = kindProp.GetString() ?? string.Empty;
            switch (kind)
            {
                case "set":
                    if (!ClipboardEnvelope.TryParseSet(root, out var set) || set is null)
                    {
                        log.Warning("clipboard: failed to parse set envelope");
                        return;
                    }
                    // Permission gate. accessClipboard == null means "no holder
                    // wired" (legacy callers and existing unit tests) -- treat
                    // as allowed so we don't regress those paths.
                    if (accessClipboard is not null && !accessClipboard())
                    {
                        log.Info("clipboard: set refused (permission)");
                        await sendRefused(new ClipboardRefusedEnvelope
                        {
                            Kind = RefusedEnvelopeKind.Refused,
                            Reason = ClipboardRefusalReason.PermissionDenied,
                            OriginId = set.OriginId,
                            Seq = set.Seq,
                            Ts = set.Ts
                        });
                        return;
                    }
                    await syncService.ReceiveSetAsync(set, sendRefused);
                    break;
                case "refused":
                    if (!ClipboardEnvelope.TryParseRefused(root, out var refused) || refused is null)
                    {
                        log.Warning("clipboard: failed to parse refused envelope");
                        return;
                    }
                    syncService.ReceiveRefused(refused);
                    break;
                case "capabilities":
                    if (!ClipboardEnvelope.TryParseCapabilities(root, out var capabilities) || capabilities is null)
                    {
                        log.Warning("clipboard: failed to parse capabilities envelope");
                        return;
                    }
                    syncService.ReceiveCapabilities(capabilities);
                    break;
                default:
                    log.Warning($"clipboard: unknown kind '{kind}'");
                    break;
            }
        }
        catch (JsonException ex)
        {
            log.Warning($"clipboard: malformed json dropped: {ex.Message}");
        }
    }

    private Task SendRefusedAsync(ClipboardRefusedEnvelope envelope)
    {
        Send(envelope);
        return Task.CompletedTask;
    }
}
