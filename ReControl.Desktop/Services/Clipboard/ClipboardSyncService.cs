using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReControl.Desktop.Protocol.Generated;

// TEST SEAM overview (plan 14-04):
//   TestSendOverride          -- bypasses _channel.Send; set in tests to capture outbound envelopes.
//   TestReadCurrentClipboardOverride -- bypasses Dispatcher.UIThread.InvokeAsync in TryPushCurrentClipboardAsync.
//   TestApplyOverride         -- bypasses Dispatcher.UIThread.InvokeAsync in ReceiveSetAsync apply step.
//   TestAttachChannelMock(originId) -- sets only _channelOriginId (no real channel) so outbound gate passes in tests.
// All seams are public with XML "TEST SEAM" docs. None affect production behavior when left null.

namespace ReControl.Desktop.Services.Clipboard;

public sealed class ClipboardSyncService
{
    private static readonly Regex Hex16 = new("^[0-9a-f]{16}$", RegexOptions.Compiled);
    private const int MaxContentBytes = 2_000_000;

    private readonly ClipboardLoopGate _clipboardLoopGate;
    private readonly LogService _log;

    // Phase 14 additions
    private readonly IClipboardWatcher _watcher;
    private readonly ClipboardSettingsStore _settings;

    /// <summary>
    /// TEST SEAM: when non-null, TryPushCurrentClipboardAsync and ReceiveSetAsync use
    /// this accessor instead of DefaultClipboardAccessor (bypasses Avalonia runtime).
    /// Production code passes null, which resolves to DefaultClipboardAccessor.
    /// </summary>
    private readonly Func<Avalonia.Input.Platform.IClipboard?> _clipboardAccessor;

    private ClipboardCtlChannel? _channel;
    private string? _channelOriginId;

    /// <summary>Phase 15 hook; Phase 14 keeps it false (D-09 browser-local pause).</summary>
    private volatile bool _isPaused;

    private long _seqCounter;

    // ---- TEST SEAMS (public; see file header comment) ----

    /// <summary>
    /// TEST SEAM: when non-null, OnLocalClipboardChanged calls this instead of _channel.Send.
    /// Signature is <c>Func&lt;object, bool&gt;</c> so tests can capture the typed envelope
    /// without depending on a real RTCDataChannel.
    /// </summary>
    public Func<object, bool>? TestSendOverride { get; set; }

    /// <summary>
    /// TEST SEAM: when non-null, TryPushCurrentClipboardAsync calls this instead of
    /// Dispatcher.UIThread.InvokeAsync to read the OS clipboard text.
    /// </summary>
    public Func<Task<string?>>? TestReadCurrentClipboardOverride { get; set; }

    /// <summary>
    /// TEST SEAM: when non-null, ReceiveSetAsync calls this instead of
    /// Dispatcher.UIThread.InvokeAsync to apply text to the OS clipboard.
    /// </summary>
    public Func<string, Task>? TestApplyOverride { get; set; }

    public ClipboardSyncService(
        IClipboardWatcher watcher,
        ClipboardLoopGate clipboardLoopGate,
        ClipboardSettingsStore settings,
        LogService log,
        Func<Avalonia.Input.Platform.IClipboard?>? clipboardAccessor = null)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _clipboardLoopGate = clipboardLoopGate ?? throw new ArgumentNullException(nameof(clipboardLoopGate));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clipboardAccessor = clipboardAccessor ?? DefaultClipboardAccessor;
        _watcher.ClipboardChanged += OnLocalClipboardChanged;
    }

    private static Avalonia.Input.Platform.IClipboard? DefaultClipboardAccessor()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow)
                : null;
        return topLevel?.Clipboard;
    }

    // ---- Channel lifecycle ----

    public void AttachChannel(ClipboardCtlChannel channel, string originId)
    {
        if (channel is null) throw new ArgumentNullException(nameof(channel));
        if (string.IsNullOrEmpty(originId)) throw new ArgumentException("originId required", nameof(originId));
        _channel = channel;
        _channelOriginId = originId;
        _clipboardLoopGate.Reset();    // D-17 fresh state per attach (LOOP-04 inheritance)
        Interlocked.Exchange(ref _seqCounter, 0);
        _log.Info($"clipboard: AttachChannel originId={originId}");
        // D-05: session-start push, fire-and-forget (read happens on UI thread; channel.Send is non-blocking).
        _ = TryPushCurrentClipboardAsync();
    }

    /// <summary>
    /// TEST SEAM: sets _channelOriginId without requiring a real ClipboardCtlChannel.
    /// Allows tests to enable the outbound path without constructing an RTCDataChannel.
    /// Also fires TryPushCurrentClipboardAsync so session-start tests work when
    /// TestReadCurrentClipboardOverride is set (mirrors what AttachChannel does).
    /// </summary>
    public void TestAttachChannelMock(string originId)
    {
        _channel = null;
        _channelOriginId = originId;
        _clipboardLoopGate.Reset();
        Interlocked.Exchange(ref _seqCounter, 0);
        // Mirror AttachChannel: trigger session-start push (uses TestReadCurrentClipboardOverride if set).
        _ = TryPushCurrentClipboardAsync();
    }

    public void DetachChannel()
    {
        _channel = null;
        _channelOriginId = null;
        _clipboardLoopGate.Reset();
        _log.Info("clipboard: DetachChannel");
    }

    public void SetPaused(bool paused)
    {
        // Phase 14: desktop-side pause is unused (browser-local D-09). Field exists for Phase 15.
        _isPaused = paused;
    }

    // ---- Inbound (from remote browser) ----

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

        // Pitfall 1 (apply-then-suppress): RecordApplied BEFORE SetTextAsync so any OS clipboard
        // change event fired by the write is suppressed by the outbound gate check.
        _clipboardLoopGate.RecordApplied(hash8);
        _log.Info($"received clipboard envelope hash={envelope.ContentHash} originId={envelope.OriginId}");

        // POLICY-06 receive-side gate: master + inbound + (Phase-15 _isPaused).
        if (_isPaused) return;
        var settings = _settings.Load();
        if (!settings.Master || !settings.AllowInbound) return;

        // TEST SEAM: bypass Dispatcher.UIThread in unit tests.
        if (TestApplyOverride is not null)
        {
            try { await TestApplyOverride(envelope.Content ?? string.Empty); }
            catch (Exception ex) { _log.Warning($"clipboard: TestApplyOverride threw: {ex.Message}"); }
            return;
        }

        // Pitfall B: fire-and-forget on UI thread -- blocking the SCTP thread is forbidden (Pitfall B).
        // _ = fire-and-forget is intentional: the inbound WebRTC callback must not block.
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var clipboard = _clipboardAccessor();
                if (clipboard is null)
                {
                    _log.Warning("clipboard: no IClipboard available; apply skipped");
                    return;
                }
                await clipboard.SetTextAsync(envelope.Content);
            }
            catch (Exception ex)
            {
                _log.Warning($"clipboard: SetTextAsync threw: {ex.Message}");
            }
        });
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

    // ---- Outbound (local clipboard -> remote browser) ----

    private async Task TryPushCurrentClipboardAsync()
    {
        try
        {
            string? text;

            // TEST SEAM: bypass Dispatcher.UIThread in unit tests.
            if (TestReadCurrentClipboardOverride is not null)
            {
                text = await TestReadCurrentClipboardOverride();
            }
            else
            {
                text = await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var clipboard = _clipboardAccessor();
                    return clipboard is null ? null : await clipboard.GetTextAsync();
                });
            }

            if (string.IsNullOrEmpty(text)) return; // D-06 silent skip
            OnLocalClipboardChanged(text);
        }
        catch (Exception ex)
        {
            _log.Warning($"clipboard: session-start push failed: {ex.Message}");
        }
    }

    private void OnLocalClipboardChanged(string text)
    {
        if (text is null) return;

        // D-03: no-op without a channel (either real or test mock).
        if (_channelOriginId is null) return;
        if (_channel is null && TestSendOverride is null) return;

        var settings = _settings.Load();
        if (!settings.Master || !settings.AllowOutbound) return;

        var (normalized, refused) = ClipboardNormalization.Normalize(text);
        if (refused)
        {
            _log.Info("clipboard: outbound refused (non-text >20% control chars)");
            return;
        }
        var utf8 = Encoding.UTF8.GetBytes(normalized);
        if (utf8.Length > MaxContentBytes)
        {
            _log.Info($"clipboard: outbound refused size={utf8.Length} (>2MB)");
            return;
        }

        var hashHex = ComputeHash16(utf8);
        var hash8 = Convert.FromHexString(hashHex);
        if (_clipboardLoopGate.ShouldSuppressOutbound(hash8))
        {
            _log.Debug($"clipboard: outbound suppressed by lastSent gate hash={hashHex}");
            return;
        }
        if (_clipboardLoopGate.ShouldSuppressInbound(hash8))
        {
            // Just-applied remote payload triggered the OS event; do not echo.
            _log.Debug($"clipboard: outbound suppressed by recentApplied gate hash={hashHex}");
            return;
        }
        _clipboardLoopGate.RecordSent(hash8);

        var seq = Interlocked.Increment(ref _seqCounter);
        var envelope = new ClipboardSetEnvelope
        {
            Kind = SetEnvelopeKind.Set,
            Content = normalized,
            OriginId = _channelOriginId,
            ContentHash = hashHex,
            Seq = seq,
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // TEST SEAM: bypass _channel.Send when TestSendOverride is set.
        if (TestSendOverride is not null)
        {
            try { TestSendOverride(envelope); }
            catch (Exception ex) { _log.Warning($"clipboard: TestSendOverride threw: {ex.Message}"); }
        }
        else
        {
            try { _channel!.Send(envelope); }
            catch (Exception ex) { _log.Warning($"clipboard: outbound send threw: {ex.Message}"); }
        }
    }

    private static string ComputeHash16(byte[] utf8Bytes)
    {
        var digest = SHA256.HashData(utf8Bytes);
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }
}
