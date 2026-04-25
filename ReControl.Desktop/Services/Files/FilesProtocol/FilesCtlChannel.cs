using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Desktop-side dispatcher for the <c>files-ctl</c> WebRTC data channel.
///
/// Consumes JSON request envelopes of the shape <c>{id, command, payload}</c>
/// delivered over the offerer-created channel (the frontend is the offerer;
/// see useWebRtc.ts), invokes the registered handler for <c>command</c>, and
/// emits a matching response envelope:
///   success: <c>{id, status:"success", result}</c>
///   error:   <c>{id, status:"error", error:{code, message, data?}}</c>
///
/// Domain exceptions are caught individually and mapped to stable error codes
/// from the 11-code Phase-9 taxonomy. Unknown commands return
/// <c>UNKNOWN_COMMAND</c>; malformed JSON returns <c>MALFORMED_RESPONSE</c>;
/// anything else becomes <c>INTERNAL_ERROR</c>.
///
/// IMPORTANT: the <see cref="InvalidFileNameException"/> catch MUST copy
/// <see cref="InvalidFileNameException.Reason"/> into <c>error.data.reason</c>
/// so the frontend sees the structured sub-reason (e.g. RESERVED, ILLEGAL_CHAR)
/// -- Plan 09-05's ALLOW-04 end-to-end demo relies on this wire-level detail.
///
/// Lifecycle note (per 09-SPIKE-FINDINGS Spike C): do NOT call dc.close()
/// on this side to tear down. Rely on pc.close() instead. The channel also
/// arrives already in readyState=open on the answerer, so there is no need
/// to wait on an onopen event here.
/// </summary>
public sealed class FilesCtlChannel
{
    private readonly RTCDataChannel _dc;
    private readonly LogService _log;
    private readonly IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>> _handlers;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public FilesCtlChannel(
        RTCDataChannel dc,
        LogService log,
        IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>> handlers)
    {
        _dc = dc ?? throw new ArgumentNullException(nameof(dc));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _dc.onmessage += OnMessage;
    }

    private void OnMessage(RTCDataChannel ch, DataChannelPayloadProtocols type, byte[] data)
    {
        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(data);
        }
        catch
        {
            _log.Warning("files-ctl: non-UTF8 message ignored");
            return;
        }
        // Fire-and-forget: exceptions inside HandleAsync are caught and mapped
        // to structured-error responses; there is no outer await site to propagate to.
        _ = HandleAsync(raw);
    }

    private async Task HandleAsync(string raw)
    {
        string? id = null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var cmd = root.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() ?? "" : "";
            var payload = root.TryGetProperty("payload", out var p) ? p : default;

            if (string.IsNullOrEmpty(id))
            {
                _log.Warning("files-ctl: request missing id; dropping");
                return;
            }

            if (!_handlers.TryGetValue(cmd, out var handler))
            {
                SendError(id, "UNKNOWN_COMMAND", $"Unknown command: {cmd}", null);
                return;
            }

            var result = await handler(payload);
            SendSuccess(id, result);
        }
        catch (AllowlistViolationException ex)
        {
            SendError(id ?? "", "ALLOWLIST_VIOLATION", "Path outside allowlisted roots",
                new { path = ex.AttemptedPath, cause = ex.Cause });
        }
        catch (InvalidFileNameException ex)
        {
            // ALLOW-04 end-to-end wire contract: ex.Reason MUST reach the
            // browser as error.data.reason so the Phase-12 i18n layer can key
            // off { code: INVALID_NAME, data: { reason: RESERVED|... } }.
            SendError(id ?? "", "INVALID_NAME", "Invalid file name",
                new { name = ex.Name, reason = ex.Reason });
        }
        catch (FileNotFoundException ex)
        {
            SendError(id ?? "", "NOT_FOUND", "Path not found",
                new { path = ex.FileName });
        }
        catch (DirectoryNotFoundException ex)
        {
            SendError(id ?? "", "NOT_FOUND", ex.Message, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            SendError(id ?? "", "PERMISSION_DENIED", "OS denied access",
                new { detail = ex.Message });
        }
        catch (TransferNotFoundException ex)
        {
            // Phase 11: files.upload.complete / files.download.begin / etc.
            // raise this when the transferId is not in the registry. Note
            // that files.transfer.cancel does NOT throw -- it returns an
            // empty success envelope on cancel-after-complete races.
            SendError(id ?? "", "TRANSFER_NOT_FOUND",
                $"transferId {ex.TransferId} not found",
                new { transferId = ex.TransferId });
        }
        catch (IOException ex) when ((ex.Message ?? "").StartsWith("DISK_FULL", StringComparison.Ordinal))
        {
            // Phase 11 upload.begin pre-flight: synthetic IOException with
            // "DISK_FULL: ..." prefix. The free-space safety margin
            // (default 64 MiB above payload size) keeps mid-write disk-full
            // from this catch path -- runtime ENOSPC is handled inside
            // UploadReceiver.OnChunkAsync and surfaces as a transfer.error
            // event, not a sync error envelope.
            SendError(id ?? "", "DISK_FULL", ex.Message ?? "DISK_FULL", null);
        }
        catch (IOException ex)
        {
            SendError(id ?? "", "IO_ERROR", ex.Message, null);
        }
        catch (JsonException ex)
        {
            _log.Warning($"files-ctl: malformed envelope: {ex.Message}");
            SendError(id ?? "", "MALFORMED_RESPONSE", "Malformed request envelope", null);
        }
        catch (Exception ex)
        {
            _log.Error("files-ctl: unhandled", ex);
            SendError(id ?? Guid.NewGuid().ToString(), "INTERNAL_ERROR", "Unexpected error",
                new { type = ex.GetType().Name });
        }
    }

    private void SendSuccess(string id, object? result)
    {
        var json = JsonSerializer.Serialize(new { id, status = "success", result }, JsonOpts);
        TrySend(json);
    }

    private void SendError(string id, string code, string message, object? data)
    {
        var json = JsonSerializer.Serialize(
            new { id, status = "error", error = new { code, message, data } }, JsonOpts);
        TrySend(json);
    }

    /// <summary>
    /// Push a server-initiated event to the browser peer. Used by Phase-11
    /// transfer-control to announce files.download.complete and
    /// files.transfer.error without correlating to a prior request.
    ///
    /// The frontend's FilesChannelClient.onEvent(command, listener)
    /// surface receives these by command name. A failed send is logged
    /// and swallowed -- the channel is shared by every transfer, and a
    /// throwing send must not crash the dispatcher.
    ///
    /// Returns Task.CompletedTask so call-sites can await uniformly even
    /// though the underlying SIPSorcery send is synchronous; future
    /// implementations may replace this with a real async send without
    /// changing consumers.
    /// </summary>
    public Task PushEventAsync(string command, object payload)
    {
        if (string.IsNullOrEmpty(command))
            throw new ArgumentException("command must be non-empty", nameof(command));
        var json = JsonSerializer.Serialize(
            new { status = "event", command, payload }, JsonOpts);
        TrySend(json);
        return Task.CompletedTask;
    }

    private void TrySend(string json)
    {
        try
        {
            _dc.send(json);
        }
        catch (Exception ex)
        {
            _log.Error("files-ctl: send failed", ex);
        }
    }
}
