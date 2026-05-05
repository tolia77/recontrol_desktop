using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ReControl.Desktop.Protocol.Generated;

namespace ReControl.Desktop.Services.Clipboard;

public sealed class ClipboardSyncService
{
    private static readonly Regex Hex16 = new("^[0-9a-f]{16}$", RegexOptions.Compiled);
    private const int MaxContentBytes = 2_000_000;

    private readonly ClipboardLoopGate _clipboardLoopGate;
    private readonly LogService _log;

    public ClipboardSyncService(ClipboardLoopGate clipboardLoopGate, LogService log)
    {
        _clipboardLoopGate = clipboardLoopGate ?? throw new ArgumentNullException(nameof(clipboardLoopGate));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task ReceiveSetAsync(
        ClipboardSetEnvelope envelope,
        Func<ClipboardRefusedEnvelope, Task>? sendRefused = null)
    {
        if (envelope is null) throw new ArgumentNullException(nameof(envelope));
        if (!Hex16.IsMatch(envelope.ContentHash))
        {
            _log.Warning("clipboard: dropped set envelope due to invalid contentHash hex16");
            return;
        }

        var utf8Bytes = Encoding.UTF8.GetBytes(envelope.Content ?? string.Empty);
        if (utf8Bytes.Length > MaxContentBytes)
        {
            var refused = new ClipboardRefusedEnvelope
            {
                Kind = RefusedEnvelopeKind.Refused,
                OriginId = envelope.OriginId,
                Reason = ClipboardRefusalReason.TooLarge,
                Seq = envelope.Seq,
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            if (sendRefused is not null)
                await sendRefused(refused);
            _log.Warning($"clipboard: refused set envelope reason=TOO_LARGE bytes={utf8Bytes.Length}");
            return;
        }

        var expectedHash16 = ComputeHash16(utf8Bytes);
        if (!string.Equals(expectedHash16, envelope.ContentHash, StringComparison.Ordinal))
        {
            _log.Warning("clipboard: dropped set envelope due to contentHash mismatch");
            return;
        }

        var hash8 = Convert.FromHexString(envelope.ContentHash);
        if (_clipboardLoopGate.ShouldSuppressInbound(hash8))
        {
            _log.Debug($"clipboard: suppressed inbound duplicate hash={envelope.ContentHash}");
            return;
        }

        _clipboardLoopGate.RecordApplied(hash8);
        _log.Info($"received clipboard envelope hash={envelope.ContentHash} originId={envelope.OriginId}");
    }

    public void ReceiveRefused(ClipboardRefusedEnvelope envelope)
    {
        _log.Info($"clipboard refused reason={envelope.Reason} originId={envelope.OriginId}");
    }

    public void ReceiveCapabilities(ClipboardCapabilitiesEnvelope envelope)
    {
        _log.Info(
            $"clipboard capabilities outbound={envelope.OutboundEnabled} inbound={envelope.InboundEnabled} maxBytes={envelope.MaxBytes}");
    }

    public void OnSettingsChanged()
    {
        _log.Info("clipboard settings changed");
    }

    private static string ComputeHash16(byte[] utf8Bytes)
    {
        var digest = SHA256.HashData(utf8Bytes);
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }
}
