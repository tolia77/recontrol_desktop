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

    /// <summary>
    /// D-09: cached browser-side capabilities envelope. Stored on receipt for diagnostics
    /// only. Outbound is NOT gated on this field (asymmetric enforcement).
    /// Reset to null on DetachChannel; refreshed on every ReceiveCapabilities call.
    /// </summary>
    private volatile ClipboardCapabilitiesEnvelope? _cachedBrowserCaps;

    /// <summary>
    /// CR-02 (Phase 15): cached <see cref="ClipboardSettings"/> snapshot consumed by
    /// <see cref="SendCapabilities"/>. The cache exists because <c>SendCapabilities</c> is
    /// reachable from <see cref="AttachChannel"/>, which runs on the SIPSorcery SCTP worker
    /// thread (WR-06). Calling <see cref="ClipboardSettingsStore.Load"/> there would synchronously
    /// disk-read the JSON file and risk stalling the SCTP thread on slow storage. The cache is
    /// refreshed off-thread:
    /// <list type="bullet">
    ///   <item>at construction (the host's startup thread, not SCTP)</item>
    ///   <item>on <see cref="OnSettingsChanged"/> (the watcher's debounce-timer thread)</item>
    /// </list>
    /// Marked <c>volatile</c> for cross-thread visibility; assignments are reference swaps
    /// of an immutable settings instance so torn reads are not possible.
    /// </summary>
    private volatile ClipboardSettings _cachedSettings = ClipboardSettings.Defaults;

    // TEST SEAMS (public; see file header comment)

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

        // CR-02: prime the cache off the SCTP thread. DI construction runs on the host's
        // startup thread, so the synchronous Load() here is safe -- and it ensures the
        // first SendCapabilities() (called from AttachChannel on the SCTP worker thread)
        // never has to disk-read. If Load() throws unexpectedly, fall back to defaults
        // rather than failing service construction; the watcher will refresh on the next
        // settings change anyway.
        try
        {
            _cachedSettings = _settings.Load();
        }
        catch (Exception ex)
        {
            _log.Warning($"clipboard: initial settings load failed; using defaults: {ex.Message}");
            _cachedSettings = ClipboardSettings.Defaults;
        }
    }

    private static Avalonia.Input.Platform.IClipboard? DefaultClipboardAccessor()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow)
                : null;
        return topLevel?.Clipboard;
    }

    // Channel lifecycle

    /// <summary>
    /// Wires a freshly negotiated clipboard data channel onto this sync service.
    ///
    /// THREADING INVARIANT (WR-06): expected to run on the SCTP/SIPSorcery worker thread
    /// (called from <c>WebRtcService.pc.ondatachannel</c>). The body must NOT block --
    /// no <c>Wait()</c> on a UI-thread dispatch, no synchronous I/O. The session-start
    /// push is fire-and-forget for that reason. If a future caller invokes this from the
    /// UI thread, fire-and-forget is still safe (the dispatcher just inlines), but DO NOT
    /// add blocking code here without first auditing every call site.
    /// </summary>
    public void AttachChannel(ClipboardCtlChannel channel, string originId)
    {
        if (channel is null) throw new ArgumentNullException(nameof(channel));
        if (string.IsNullOrEmpty(originId)) throw new ArgumentException("originId required", nameof(originId));
        // WR-06: defense in depth -- in DEBUG builds, flag a UI-thread caller. The current
        // SIPSorcery wiring should never invoke us from the UI thread; if a future change
        // does, blocking work added here would deadlock the UI.
        System.Diagnostics.Debug.Assert(
            !Dispatcher.UIThread.CheckAccess(),
            "ClipboardSyncService.AttachChannel must run off the UI thread (SCTP worker)");
        _channel = channel;
        _channelOriginId = originId;
        _clipboardLoopGate.Reset();    // D-17 fresh state per attach (LOOP-04 inheritance)
        Interlocked.Exchange(ref _seqCounter, 0);
        _log.Info($"clipboard: AttachChannel originId={originId}");
        // D-05: session-start push, fire-and-forget (read happens on UI thread; channel.Send is non-blocking).
        _sessionStartPushTask = TryPushCurrentClipboardAsync();
        // D-17 (Phase 15): synchronous, WR-06-safe — _channel.Send is non-blocking.
        SendCapabilities();
    }

    /// <summary>
    /// TEST SEAM (WR-09): the Task returned by the most recent fire-and-forget session-start push.
    /// Tests can <c>await svc.SessionStartPushTask</c> instead of <c>Task.Delay(50)</c> to
    /// synchronously gate on the push completing -- removes timing flakes on slow CI.
    /// Production code never observes this property.
    /// </summary>
    public Task SessionStartPushTask => _sessionStartPushTask;
    private Task _sessionStartPushTask = Task.CompletedTask;

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
        // WR-09: capture the task so tests can await SessionStartPushTask instead of Task.Delay(50).
        _sessionStartPushTask = TryPushCurrentClipboardAsync();
        // Phase 15: mirror AttachChannel's capabilities advertisement so unit tests
        // exercising the mock attach path observe the same outbound envelope.
        SendCapabilities();
    }

    public void DetachChannel()
    {
        _channel = null;
        _channelOriginId = null;
        _clipboardLoopGate.Reset();
        // Phase 15 D-06 / D-17 reset philosophy: clear cached peer caps on every detach
        // so a fresh attach starts from a clean state.
        _cachedBrowserCaps = null;
        _log.Info("clipboard: DetachChannel");
    }

    public void SetPaused(bool paused)
    {
        // Phase 14: desktop-side pause is unused (browser-local D-09). Field exists for Phase 15.
        _isPaused = paused;
    }

    // Inbound (from remote browser)

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

        // WR-02: check the byte count BEFORE materializing the byte array. A peer (or
        // attacker reaching the application layer) sending a 100MB string would otherwise
        // force a 100MB allocation just to refuse it. GetByteCount walks the string
        // without allocating; the eager GetBytes only runs on plausibly-sized payloads.
        var content = envelope.Content ?? string.Empty;
        var byteCount = Encoding.UTF8.GetByteCount(content);
        if (byteCount > MaxContentBytes)
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
            _log.Warning($"clipboard: refused set envelope reason=TOO_LARGE bytes={byteCount}");
            return;
        }

        var utf8Bytes = Encoding.UTF8.GetBytes(content);
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

        // WR-08: POLICY-06 receive-side gate must run BEFORE RecordApplied. If we recorded
        // first and then bailed out due to policy, the loop gate would treat that hash as
        // "just applied" -- causing the next matching local clipboard change to be silently
        // suppressed from outbound. Check policy first so the gate state truthfully reflects
        // an apply that is about to happen.
        // Phase 15 (D-02, CAP-03): each of the four policy paths now emits a categorized
        // ClipboardRefusedEnvelope rather than silently dropping. Refusal happens BEFORE
        // RecordApplied so a refused-but-not-applied envelope does not poison the loop gate.
        if (_isPaused)
        {
            if (sendRefused is not null)
                await sendRefused(new ClipboardRefusedEnvelope
                {
                    Kind = RefusedEnvelopeKind.Refused,
                    OriginId = envelope.OriginId,
                    Reason = ClipboardRefusalReason.Paused,
                    Seq = envelope.Seq,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            _log.Warning("clipboard: refused set envelope reason=PAUSED");
            return;
        }

        var settings = _settings.Load();
        if (!settings.Master)
        {
            if (sendRefused is not null)
                await sendRefused(new ClipboardRefusedEnvelope
                {
                    Kind = RefusedEnvelopeKind.Refused,
                    OriginId = envelope.OriginId,
                    Reason = ClipboardRefusalReason.MasterDisabled,
                    Seq = envelope.Seq,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            _log.Warning("clipboard: refused set envelope reason=MASTER_DISABLED");
            return;
        }
        if (!settings.AllowInbound)
        {
            if (sendRefused is not null)
                await sendRefused(new ClipboardRefusedEnvelope
                {
                    Kind = RefusedEnvelopeKind.Refused,
                    OriginId = envelope.OriginId,
                    Reason = ClipboardRefusalReason.InboundDisabled,
                    Seq = envelope.Seq,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            _log.Warning("clipboard: refused set envelope reason=INBOUND_DISABLED");
            return;
        }

        // Phase 15 (D-01): NON_TEXT defensive receiver-side check. Mirrors the sender-side
        // normalization in OnLocalClipboardChanged. Runs AFTER hash + policy checks and
        // BEFORE RecordApplied so a refused-non-text envelope never poisons the loop gate.
        // WR-06: the normalized output is now actually applied -- previously the receiver
        // hashed/applied the original and discarded the normalize result. Using the
        // normalized text on apply matches the sender-side path in OnLocalClipboardChanged
        // (CRLF -> LF, control-char-heavy refused) and removes the brittle "hash original /
        // probe normalized / apply original" middle-ground.
        var (normalizedInbound, refusedNonText) = ClipboardNormalization.Normalize(content);
        if (refusedNonText)
        {
            if (sendRefused is not null)
                await sendRefused(new ClipboardRefusedEnvelope
                {
                    Kind = RefusedEnvelopeKind.Refused,
                    OriginId = envelope.OriginId,
                    Reason = ClipboardRefusalReason.NonText,
                    Seq = envelope.Seq,
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            _log.Warning("clipboard: refused set envelope reason=NON_TEXT");
            return;
        }

        // WR-06: if normalization actually changed the bytes, the OS clipboard event will
        // fire over `normalizedInbound`, whose SHA-256 differs from `envelope.ContentHash`.
        // Record THAT hash in the loop gate so the echo is correctly suppressed. In the
        // common case (browser already CRLF-normalized before send) the two are identical
        // and this branch reduces to the original behavior.
        byte[] applyHash8;
        if (string.Equals(normalizedInbound, content, StringComparison.Ordinal))
        {
            applyHash8 = hash8;
        }
        else
        {
            var normUtf8 = Encoding.UTF8.GetBytes(normalizedInbound);
            applyHash8 = Convert.FromHexString(ComputeHash16(normUtf8));
            _log.Debug($"clipboard: receiver-side normalization changed content; loop gate hash adjusted");
        }

        // Pitfall 1 (apply-then-suppress): RecordApplied BEFORE SetTextAsync so any OS clipboard
        // change event fired by the write is suppressed by the outbound gate check.
        _clipboardLoopGate.RecordApplied(applyHash8);
        _log.Info($"received clipboard envelope hash={envelope.ContentHash} originId={envelope.OriginId}");

        // TEST SEAM: bypass Dispatcher.UIThread in unit tests.
        if (TestApplyOverride is not null)
        {
            try
            {
                await TestApplyOverride(normalizedInbound);
            }
            catch (Exception ex)
            {
                // WR-04: if the apply fails, reset the gate so a subsequent local OS clipboard
                // event matching this hash is NOT suppressed (the OS still has the old contents).
                _clipboardLoopGate.Reset();
                _log.Warning($"clipboard: TestApplyOverride threw: {ex.Message}; loop gate reset");
            }
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
                    // WR-04: gate already recorded the hash but no apply happened. Reset so a
                    // future local OS event for this content is allowed through to outbound.
                    _clipboardLoopGate.Reset();
                    return;
                }
                await clipboard.SetTextAsync(normalizedInbound);
            }
            catch (Exception ex)
            {
                _log.Warning($"clipboard: SetTextAsync threw: {ex.Message}; loop gate reset");
                // WR-04: see above -- if SetTextAsync fails, reset the gate.
                _clipboardLoopGate.Reset();
            }
        });
    }

    public void ReceiveRefused(ClipboardRefusedEnvelope envelope)
    {
        _log.Info($"clipboard refused reason={envelope.Reason} originId={envelope.OriginId}");
    }

    public void ReceiveCapabilities(ClipboardCapabilitiesEnvelope envelope)
    {
        // D-09: cache for diagnostics only; outbound is NOT gated on this field
        // (asymmetric enforcement -- desktop trusts its own settings, not peer-supplied caps).
        _cachedBrowserCaps = envelope;
        _log.Info(
            $"clipboard browser caps outbound={envelope.OutboundEnabled} inbound={envelope.InboundEnabled} originId={envelope.OriginId}");

        // Handshake completion: re-advertise our capabilities in response to the
        // browser's advertisement. The AttachChannel send fires the instant the
        // inbound channel arrives (already open), which is BEFORE the browser has
        // attached its ClipboardChannelClient message listener -- so that first
        // envelope is dropped (data channels don't buffer for late listeners).
        // The browser sends its caps once its listener is live; replying here
        // guarantees it receives ours, clearing its CAP-07 timeout instead of
        // falsely flipping the pill to "requires-v1.3" (Update desktop).
        // WR-06-safe: SendCapabilities is synchronous and reads only the settings
        // cache, same as the AttachChannel call site on the SCTP worker thread.
        SendCapabilities();
    }

    public void OnSettingsChanged()
    {
        // D-16: ClipboardSettingsWatcher already debounces at 300 ms, well within CAP-02's
        // 1 s budget; no second debounce inside SendCapabilities.
        // CR-02: refresh the cache HERE -- this callback runs on the watcher's
        // debounce-timer thread, not the SCTP worker thread, so the synchronous
        // Load() is allowed. SendCapabilities then reads the cache only.
        try
        {
            _cachedSettings = _settings.Load();
        }
        catch (Exception ex)
        {
            _log.Warning($"clipboard: settings reload failed; cache unchanged: {ex.Message}");
        }
        _log.Info("clipboard settings changed; re-advertising");
        SendCapabilities();
    }

    /// <summary>
    /// D-17 / Pattern B (Phase 15): synchronous capabilities advertise. No-op if no channel
    /// is attached (no _channelOriginId, no _channel and no TestSendOverride).
    /// WR-06 invariant: stays synchronous; <c>_channel.Send</c> is non-blocking AND the
    /// settings read is from the in-memory cache (CR-02), not <see cref="ClipboardSettingsStore.Load"/>.
    /// The cache is refreshed off-thread (constructor + <see cref="OnSettingsChanged"/>);
    /// callers reachable from the SCTP worker thread (notably <see cref="AttachChannel"/>)
    /// must not perform synchronous disk I/O here.
    /// </summary>
    private void SendCapabilities()
    {
        if (_channelOriginId is null) return;
        if (_channel is null && TestSendOverride is null) return;

        // CR-02: read the cache, not _settings.Load(). _settings.Load() does synchronous
        // file I/O which violates WR-06 when this method is reached from AttachChannel.
        var settings = _cachedSettings;
        var envelope = new ClipboardCapabilitiesEnvelope
        {
            Kind = CapabilitiesEnvelopeKind.Capabilities,
            OriginId = _channelOriginId,
            OutboundEnabled = settings.Master && settings.AllowOutbound,
            InboundEnabled = settings.Master && settings.AllowInbound,
            MaxBytes = MaxContentBytes,                        // single source of truth
            ProtocolVersion = "1.0",                            // D-19 literal lock
            Seq = Interlocked.Increment(ref _seqCounter),       // shares per-channel counter
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        if (TestSendOverride is not null)
        {
            try { TestSendOverride(envelope); }
            catch (Exception ex) { _log.Warning($"clipboard: TestSendOverride threw: {ex.Message}"); }
        }
        else
        {
            try { _channel!.Send(envelope); }
            catch (Exception ex) { _log.Warning($"clipboard: SendCapabilities threw: {ex.Message}"); }
        }
    }

    // Outbound (local clipboard -> remote browser)

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
