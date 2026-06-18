using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ReControl.Desktop.Services.Clipboard;
using ReControl.Desktop.Services.Files;
using ReControl.Desktop.Services.Files.FilesProtocol;
using ReControl.Desktop.Services.Interfaces;
using ReControl.Desktop.Services.Permissions;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace ReControl.Desktop.Services;

/// <summary>
/// WebRTC peer connection lifecycle, signaling, and screen streaming.
/// When an IScreenCaptureService is provided, captures the screen and encodes
/// H.264 (preferred) or VP8 (fallback) frames via FFmpegVideoEncoder from SIPSorceryMedia.FFmpeg.
/// </summary>
public sealed class WebRtcService : IDisposable
{
    // Converts Stopwatch ticks to microseconds. All per-frame stage timings use this.
    private static readonly double _tsToUs = 1_000_000.0 / Stopwatch.Frequency;

    private readonly LogService _log;
    private readonly Func<string, Task> _sendSignal;
    private readonly IScreenCaptureService? _screenCapture;
    private readonly Func<Task<List<RTCIceServer>>>? _fetchIceServers;

    private RTCPeerConnection? _pc;
    private FFmpegVideoSource? _videoSource;
    private volatile bool _disposed;
    private readonly List<RTCIceCandidateInit> _pendingCandidates = new();
    // Serializes HandleOfferAsync end-to-end. Inbound commands are dispatched
    // on detached thread-pool tasks (MainViewModel.OnMessageReceived) with no
    // ordering guarantee, so two interleaved offers would both run
    // CleanupPeerConnection and race assignments to _pc/_videoSource — the
    // first offer's continuation then operates on the second offer's peer.
    private readonly SemaphoreSlim _offerLock = new SemaphoreSlim(1, 1);
    // True once setRemoteDescription has been applied to the CURRENT _pc.
    // Guarded by lock(_pendingCandidates): HandleIceCandidate must buffer
    // candidates that arrive between `_pc = new RTCPeerConnection(...)` and
    // setRemoteDescription — adding them directly to a pc with no remote
    // description corrupts ICE state.
    private bool _remoteDescriptionSet;

    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    // Serializes Start/Stop/Restart so concurrent resolution-change and
    // connection-state transitions cannot corrupt _captureCts/_captureTask.
    private readonly SemaphoreSlim _captureLock = new SemaphoreSlim(1, 1);
    private byte[]? _captureBuffer;
    private int _bufferSize;

    private volatile int _targetFps = 24;
    private volatile int _targetResolution = 1080;
    // Null-frame streak recovery. _needsRecovery is set from the capture
    // loop thread and read at the top of the loop body on the next iteration.
    // _nullStreakStart is only ever read/written from the capture-loop thread
    // (DateTime? cannot be volatile), so no cross-thread races.
    private volatile bool _needsRecovery;
    private DateTime? _nullStreakStart;

    private DirtyDetector? _dirtyDetector;
    private RTCDataChannel? _statsChannel;
    private long _lastStatsSendMs;

    // files-ctl / files-data are created on the offerer side (frontend); the desktop
    // consumes them via pc.ondatachannel and NEVER calls createDataChannel for these
    // labels. This sidesteps a SIPSorcery RTCDataChannelInit reliability issue (#701)
    // that affects answerer-created channels.
    private FileOperationsService? _fileOps;
    private Func<IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>>> _filesCommandHandlersFactory;
    private FilesCtlChannel? _filesCtl;
    private FilesDataChannel? _filesData;
    // Raw RTCDataChannel handle for files-data, surfaced via the public
    // FilesDataChannel property below so the FilesCommandHandlers factory's
    // accessor closure can hand it to DownloadSender.
    private RTCDataChannel? _filesDataRtc;
    // Process-wide registry of in-flight transfers. Provided by
    // CommandDispatcher; constructed there and shared so the registry's
    // counter survives WebRtcService construction order.
    private readonly TransferRegistry? _transferRegistry;
    // 60s orphan-sweeper for .partial files. Created on first offer (when
    // we have a registry) and disposed in CleanupPeerConnection.
    private PartialFileSweeper? _partialSweeper;
    // 1s stall watchdog: pushes files.transfer.error STALLED for receivers
    // idle > 10s. Lifecycle co-located with the sweeper / registry-CancelAll
    // teardown so all transfer-side timers go away together.
    private StallMonitor? _stallMonitor;
    private ClipboardCtlChannel? _clipboardCtl;
    private ClipboardSyncService? _clipboardSync;
    private string? _clipboardOriginId;

    // Active peer's permission snapshot. Seeded from the `permissions` field
    // of the inbound webrtc.offer envelope, refreshed by permissions.update.
    // ClipboardCtlChannel and FilesCtlChannel read this holder before
    // processing each inbound message.
    private readonly PeerPermissionsHolder _peerPermissions = new();

    /// <summary>
    /// The raw <see cref="RTCDataChannel"/> for files-data, exposed for the
    /// file transfer engine (DownloadSender pushes chunks here directly).
    /// Null until the frontend's SDP offer creates the channel and our
    /// ondatachannel handler runs.
    /// </summary>
    public RTCDataChannel? FilesDataChannel => _filesDataRtc;

    /// <summary>
    /// The files-ctl wrapper, exposed so the file transfer engine can
    /// push <c>files.download.complete</c> / <c>files.transfer.error</c>
    /// events from outside the request/response flow. Null until the
    /// frontend's SDP offer creates the channel.
    /// </summary>
    public FilesCtlChannel? FilesCtlChannel => _filesCtl;

    /// <summary>
    /// Live read-only view of the active peer's permissions. Returns
    /// PeerPermissions.OwnerEquivalent before the first offer arrives.
    /// </summary>
    public PeerPermissions Permissions => _peerPermissions.Current;

    /// <summary>
    /// Replace the active snapshot. Called by CommandDispatcher when a
    /// `permissions.update` command arrives. Idempotent and thread-safe.
    /// </summary>
    public void UpdatePermissions(PeerPermissions snapshot) => _peerPermissions.Set(snapshot);

    public WebRtcService(
        LogService log,
        Func<string, Task> sendSignal,
        IScreenCaptureService? screenCapture = null,
        FileOperationsService? fileOps = null,
        Func<IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>>>? filesCommandHandlersFactory = null,
        TransferRegistry? transferRegistry = null,
        ClipboardSyncService? clipboardSync = null,
        Func<Task<List<RTCIceServer>>>? fetchIceServers = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sendSignal = sendSignal ?? throw new ArgumentNullException(nameof(sendSignal));
        _screenCapture = screenCapture;
        _fileOps = fileOps;
        _fetchIceServers = fetchIceServers;
        // Default factory returns an empty dictionary (no command handlers); a real
        // factory returning { "files.list": ..., ... } is injected via DI.
        _filesCommandHandlersFactory = filesCommandHandlersFactory
            ?? (() => new Dictionary<string, Func<JsonElement, Task<object?>>>());
        _transferRegistry = transferRegistry;
        _clipboardSync = clipboardSync;

        if (_screenCapture != null)
        {
            _bufferSize = _screenCapture.Width * _screenCapture.Height * 4;
            _captureBuffer = new byte[_bufferSize];
            _log.Info($"WebRtcService: screen capture available ({_screenCapture.Width}x{_screenCapture.Height})");
        }
    }

    /// <summary>
    /// Parse the `permissions` field of an inbound webrtc.offer envelope.
    /// Returns PeerPermissions.OwnerEquivalent if the element is a default
    /// (uninitialized) JsonElement -- supports backends that have not yet
    /// shipped the snapshot in their envelope.
    /// </summary>
    public static PeerPermissions ParsePermissionsSnapshot(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            return PeerPermissions.OwnerEquivalent;
        }

        bool Get(string key) =>
            element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

        return new PeerPermissions(
            SeeScreen: Get("see_screen"),
            AccessMouse: Get("access_mouse"),
            AccessKeyboard: Get("access_keyboard"),
            AccessTerminal: Get("access_terminal"),
            ManagePower: Get("manage_power"),
            AccessClipboard: Get("access_clipboard"),
            FilesRead: Get("files_read"),
            FilesWrite: Get("files_write"));
    }

    public void SetTargetFps(int fps)
    {
        fps = Math.Clamp(fps, 1, 30);
        _targetFps = fps;
        _log.Info($"WebRtcService: target FPS changed to {fps}");
    }

    public void SetTargetResolution(int resolution)
    {
        resolution = resolution switch
        {
            480 or 720 or 1080 => resolution,
            _ => 1080
        };

        if (resolution == _targetResolution) return;
        _targetResolution = resolution;
        _log.Info($"WebRtcService: target resolution changed to {resolution}p");

        // If stream is running, restart capture loop with new resolution
        // This recreates the encoder to avoid FFmpeg crash on dimension change
        if (_captureCts != null)
        {
            RestartCaptureWithNewResolution();
        }
    }

    /// <summary>
    /// Forces the FFmpeg encoder to emit an IDR keyframe on the next encoded
    /// frame. Called by <see cref="ReControl.Desktop.Commands.WebRtc.WebRtcRequestKeyframeCommand"/>
    /// when the viewer signals an orientation change, resolving the black-frame
    /// stall the decoder shows while waiting for the next keyframe to arrive.
    /// </summary>
    public void RequestKeyframe()
    {
        _videoSource?.ForceKeyFrame();
    }

    public async Task HandleOfferAsync(string sdp, JsonElement permissions = default)
    {
        // Serialize the whole offer pipeline (see _offerLock). A second offer
        // arriving mid-handshake waits here until the first completes or fails.
        await _offerLock.WaitAsync();
        try
        {
            await HandleOfferLockedAsync(sdp, permissions);
        }
        finally
        {
            _offerLock.Release();
        }
    }

    private async Task HandleOfferLockedAsync(string sdp, JsonElement permissions)
    {
        _log.Info("WebRtcService: handling offer");
        CleanupPeerConnection();
        _peerPermissions.Set(ParsePermissionsSnapshot(permissions));

        // Fetch ephemeral ICE servers (Cloudflare TURN + STUN) from the backend.
        // Falls back to STUN-only inside TurnCredentialsService if the backend is
        // unreachable, so same-LAN peers still connect even when TURN is down.
        var iceServers = _fetchIceServers is not null
            ? await _fetchIceServers()
            : new List<RTCIceServer> { new RTCIceServer { urls = "stun:stun.l.google.com:19302" } };

        var config = new RTCConfiguration { iceServers = iceServers };

        _pc = new RTCPeerConnection(config);

        // Wrap everything after _pc allocation so any failure path
        // (setRemoteDescription, data-channel setup, setLocalDescription, SendSignalSafe)
        // disposes the partially-created peer before propagating the exception.
        try
        {

        _statsChannel = await _pc.createDataChannel("stats");
        _dirtyDetector = new DirtyDetector();

        // Lazily start the .partial orphan sweeper the first time we have a
        // registry to attach it to. Disposed in CleanupPeerConnection so the
        // timer doesn't survive across reconnects (a fresh sweeper picks up
        // the registry's KnownParentDirs again from the next allocation).
        if (_transferRegistry is not null && _partialSweeper is null)
        {
            _partialSweeper = new PartialFileSweeper(_transferRegistry, _log);
        }

        // 1 s stall watchdog over active upload receivers. The
        // FilesCtlChannel handle is captured via a deferred getter because
        // _filesCtl is null until the frontend's ondatachannel for files-ctl
        // fires (which can happen AFTER this constructor in the offer flow).
        if (_transferRegistry is not null && _stallMonitor is null)
        {
            _stallMonitor = new StallMonitor(_transferRegistry, () => _filesCtl, _log);
        }

        // Route inbound data channels created by the frontend (files-ctl, files-data)
        // to their respective handlers. Registered BEFORE setRemoteDescription so the
        // handler is in place when SIPSorcery processes the offerer's SCTP m-section.
        //
        // Invariants:
        //  - Inbound channels arrive already in readyState=open -- do NOT wait on
        //    onopen here; attach onmessage immediately (FilesCtlChannel / FilesDataChannel
        //    subscribe to dc.onmessage in their constructors).
        //  - Never call dc.close() on these channels from the desktop side; tear-down
        //    is driven by pc.close() inside CleanupPeerConnection().
        //  - The existing "stats" channel is answerer-created (line above) and its
        //    on-the-wire id differs from files-ctl/files-data; all three coexist on
        //    the same SCTP association without renegotiation.
        _pc.ondatachannel += rdc =>
        {
            _log.Info($"WebRtcService: ondatachannel label={rdc.label} readyState={rdc.readyState}");
            switch (rdc.label)
            {
                case "files-ctl":
                    _filesCtl = new FilesCtlChannel(
                        rdc, _log, _filesCommandHandlersFactory(),
                        filesRead: () => _peerPermissions.Current.FilesRead,
                        filesWrite: () => _peerPermissions.Current.FilesWrite);
                    rdc.onopen += () => _log.Info("files-ctl: open");
                    rdc.onclose += () => _log.Info("files-ctl: closed");
                    break;
                case "files-data":
                    _filesDataRtc = rdc;
                    if (_transferRegistry is not null)
                    {
                        _filesData = new FilesDataChannel(rdc, _log, _transferRegistry);
                    }
                    else
                    {
                        // Fallback: registry not provided -> chunks get logged but
                        // not routed. Production wiring always supplies a registry
                        // through CommandDispatcher.
                        _log.Warning("files-data: no TransferRegistry; chunks will be dropped");
                    }
                    rdc.onopen += () => _log.Info("files-data: open");
                    rdc.onclose += () => _log.Info("files-data: closed");
                    break;
                case "clipboard":
                    if (_clipboardSync is null)
                    {
                        _log.Warning("clipboard: no ClipboardSyncService injected; clipboard channel ignored");
                        break;
                    }
                    _clipboardOriginId = Guid.NewGuid().ToString();
                    _clipboardCtl = new ClipboardCtlChannel(
                        rdc, _log, _clipboardSync,
                        accessClipboard: () => _peerPermissions.Current.AccessClipboard);
                    _clipboardSync.AttachChannel(_clipboardCtl, _clipboardOriginId);
                    rdc.onopen += () => _log.Info("clipboard: open");
                    rdc.onclose += () => _log.Info("clipboard: closed");
                    break;
                default:
                    _log.Warning($"WebRtcService: unknown data channel label '{rdc.label}' -- ignoring");
                    break;
            }
        };

        // Set up video encoding via FFmpeg with H.264 (preferred) + VP8 fallback
        var encoderOptions = new Dictionary<string, string>
        {
            { "preset", "ultrafast" },
            { "tune", "zerolatency" },
            { "crf", GetCrfForResolution(_targetResolution) }
        };
        var encoder = new FFmpegVideoEncoder(encoderOptions);
        _videoSource = new FFmpegVideoSource(encoder);
        _videoSource.RestrictFormats(f => f.Codec == VideoCodecsEnum.H264);

        var videoTrack = new MediaStreamTrack(
            _videoSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        _pc.addTrack(videoTrack);

        _videoSource.OnVideoSourceEncodedSample += _pc.SendVideo;
        _pc.OnVideoFormatsNegotiated += (negotiatedFormats) =>
        {
            var negotiated = negotiatedFormats.First();
            _log.Info($"WebRtcService: negotiated video codec: {negotiated.Codec} " +
                      $"(formatID={negotiated.FormatID}, params={negotiated.Parameters})");
            _videoSource.SetVideoSourceFormat(negotiated);
        };

        _pc.onicecandidate += (candidate) =>
        {
            _log.Info("WebRtcService: local ICE candidate generated");
            var payload = new
            {
                command = "webrtc.ice_candidate",
                payload = new
                {
                    candidate = candidate.candidate,
                    sdpMid = candidate.sdpMid,
                    sdpMLineIndex = candidate.sdpMLineIndex
                }
            };
            _ = SendSignalSafe(System.Text.Json.JsonSerializer.Serialize(payload));
        };

        _pc.onconnectionstatechange += (state) =>
        {
            _log.Info($"WebRtcService: connection state -> {state}");
            if (state == RTCPeerConnectionState.connected)
            {
                StartCaptureLoop();
            }
            else if (state == RTCPeerConnectionState.disconnected ||
                     state == RTCPeerConnectionState.failed ||
                     state == RTCPeerConnectionState.closed)
            {
                StopCaptureLoop();
            }
        };

        var remoteDesc = new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdp };
        var setResult = _pc.setRemoteDescription(remoteDesc);
        if (setResult != SetDescriptionResultEnum.OK)
        {
            _log.Error($"WebRtcService: failed to set remote description: {setResult}");
            throw new InvalidOperationException($"Failed to set remote description: {setResult}");
        }

        lock (_pendingCandidates)
        {
            // Flip the flag inside the same lock that guards the buffer drain:
            // a concurrent HandleIceCandidate either sees the flag false and
            // buffers (drained below) or sees it true and adds directly —
            // no candidate can be stranded in the buffer after this drain.
            _remoteDescriptionSet = true;
            foreach (var pending in _pendingCandidates)
            {
                _log.Info("WebRtcService: adding buffered ICE candidate");
                _pc.addIceCandidate(pending);
            }
            _pendingCandidates.Clear();
        }

        var answer = _pc.createAnswer();
        await _pc.setLocalDescription(answer);

        var answerPayload = new
        {
            command = "webrtc.answer",
            payload = new { sdp = answer.sdp }
        };
        await SendSignalSafe(System.Text.Json.JsonSerializer.Serialize(answerPayload));
        _log.Info("WebRtcService: answer sent");

        } // end try
        catch (Exception ex)
        {
            _log.Error("WebRtcService: HandleOfferAsync failed, cleaning up", ex);
            CleanupPeerConnection();
            throw;
        }
    }

    public void HandleIceCandidate(string candidate, string? sdpMid, ushort? sdpMLineIndex)
    {
        var iceCandidate = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid ?? "0",
            sdpMLineIndex = sdpMLineIndex ?? 0
        };

        RTCPeerConnection? pc;
        lock (_pendingCandidates)
        {
            // Buffer until the remote description is actually applied, not
            // merely until _pc is allocated — candidates added between pc
            // construction and setRemoteDescription corrupt ICE state.
            pc = _pc;
            if (pc == null || !_remoteDescriptionSet)
            {
                _log.Info("WebRtcService: buffering ICE candidate (peer connection not ready)");
                _pendingCandidates.Add(iceCandidate);
                return;
            }
        }

        _log.Info("WebRtcService: adding remote ICE candidate");
        pc.addIceCandidate(iceCandidate);
    }

    public void Stop()
    {
        _log.Info("WebRtcService: stop requested");
        StopCaptureLoop(wait: true);
        CleanupPeerConnection();
    }

    private void RestartCaptureWithNewResolution()
    {
        // Acquire _captureLock ONCE and call the lockless Core helpers.
        // Must NOT call StartCaptureLoop/StopCaptureLoop (the locked wrappers) here —
        // SemaphoreSlim(1,1) is not reentrant and re-acquiring from the same thread deadlocks.
        _captureLock.Wait();
        try
        {
            // Stop/CleanupPeerConnection/Dispose may have won the race between
            // the scheduling check (recovery fires from a detached Task.Run) and
            // this lock acquisition. Restarting here would resurrect a capture
            // loop against a torn-down peer (no subscriber, runs until process
            // exit after Dispose). Bail out instead of resurrecting.
            if (_disposed || _pc == null) return;

            StopCaptureLoopCore(wait: true);

            // Unsubscribe old video source from peer connection
            if (_videoSource != null && _pc != null)
            {
                _videoSource.OnVideoSourceEncodedSample -= _pc.SendVideo;
                _videoSource.Dispose();
                _videoSource = null;
            }

            // Create new encoder + video source at the new resolution
            var encoderOptions = new Dictionary<string, string>
            {
                { "preset", "ultrafast" },
                { "tune", "zerolatency" },
                { "crf", GetCrfForResolution(_targetResolution) }
            };
            var encoder = new FFmpegVideoEncoder(encoderOptions);
            _videoSource = new FFmpegVideoSource(encoder);
            _videoSource.RestrictFormats(f => f.Codec == VideoCodecsEnum.H264);

            // Re-negotiate format (use the same negotiated format from the track)
            if (_pc != null)
            {
                _videoSource.OnVideoSourceEncodedSample += _pc.SendVideo;
                // Re-set the video source format from the existing negotiated format
                var formats = _videoSource.GetVideoSourceFormats();
                if (formats.Count > 0)
                    _videoSource.SetVideoSourceFormat(formats.First());
            }

            // Reset dirty detector so first frame at new resolution is always encoded
            _dirtyDetector?.Dispose();
            _dirtyDetector = new DirtyDetector();

            StartCaptureLoopCore();
        }
        finally { _captureLock.Release(); }
    }

    private static string GetCrfForResolution(int resolution) => resolution switch
    {
        480 => "28",
        720 => "25",
        _ => "23"  // 1080p default
    };

    /// <summary>
    /// Returns the scale factors to convert from stream coordinates to native screen coordinates.
    /// When not scaling (target >= native), returns (1, 1).
    /// </summary>
    public (float scaleX, float scaleY) GetCoordinateScale()
    {
        if (_screenCapture == null) return (1f, 1f);
        var (targetW, targetH) = GetTargetDimensions();
        if (targetW <= 0 || targetH <= 0) return (1f, 1f);
        return ((float)_screenCapture.Width / targetW, (float)_screenCapture.Height / targetH);
    }

    private (int width, int height) GetTargetDimensions()
    {
        if (_screenCapture == null) return (0, 0);
        int nativeW = _screenCapture.Width;
        int nativeH = _screenCapture.Height;

        if (_targetResolution >= nativeH)
            return (nativeW, nativeH); // no scaling needed, never upscale

        float scale = (float)_targetResolution / nativeH;
        int targetW = (int)(nativeW * scale) & ~1; // ensure even
        int targetH = _targetResolution & ~1;       // ensure even
        return (targetW, targetH);
    }

    // Thin locked wrapper — serializes callers via _captureLock.
    private void StartCaptureLoop()
    {
        _captureLock.Wait();
        try { StartCaptureLoopCore(); }
        finally { _captureLock.Release(); }
    }

    // Lockless core; must only be called while _captureLock is held.
    private void StartCaptureLoopCore()
    {
        if (_screenCapture == null || _captureBuffer == null)
        {
            _log.Warning("WebRtcService: no screen capture service, skipping capture loop");
            return;
        }

        StopCaptureLoopCore();

        // Clear any stale recovery flag left by a loop that was cancelled after
        // setting _needsRecovery but before consuming it — otherwise the fresh
        // loop's first iteration immediately exits and schedules one spurious
        // restart. Safe to write _nullStreakStart here: no loop is running
        // (StopCaptureLoopCore above) and we hold _captureLock.
        _needsRecovery = false;
        _nullStreakStart = null;

        // Reset the stats clock for the new loop: _lastStatsSendMs is compared
        // against a per-loop Stopwatch that restarts at 0, so a value carried
        // over from a previous (longer-lived) loop keeps the difference negative
        // and silences the stats channel for as long as the old loop had run.
        _lastStatsSendMs = 0;

        _captureCts = new CancellationTokenSource();
        var ct = _captureCts.Token;

        // INVARIANT: the Task.Run body below must NOT acquire _captureLock.
        // StopCaptureLoopCore's _captureTask?.Wait(2000) is safe because this task
        // never contends for the lock — re-acquiring would deadlock.
        _captureTask = Task.Run(async () =>
        {
            _log.Info($"WebRtcService: capture loop started at {_targetFps} FPS, videoSource={_videoSource != null}, bufLen={_captureBuffer?.Length}, screen={_screenCapture?.Width}x{_screenCapture?.Height}");

            var (targetW, targetH) = GetTargetDimensions();
            bool needsScaling = targetW != _screenCapture.Width || targetH != _screenCapture.Height;
            byte[]? scaledBuffer = needsScaling ? new byte[targetW * targetH * 4] : null;
            _log.Info($"WebRtcService: target resolution={_targetResolution}p ({targetW}x{targetH}), scaling={needsScaling}");

            var frameCount = 0;
            var captureFailCount = 0;
            var previousNullCount = _videoSource?.NullCount ?? 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lastEncodeMs = 0L;

            // Per-frame sequence counter and running RTP timestamp accumulator.
            // Declared as locals (not instance fields) because
            // RestartCaptureWithNewResolution holds _captureLock and spawns a new
            // Task.Run; racing instance fields would corrupt the measurements.
            long frameSeq = 0;
            long lastRtpTs = 0;

            // Per-interval timing accumulators for the 1-second stats drain.
            long captureUsSum = 0;
            long encodeUsSum = 0;
            int framesThisInterval = 0;
            long bytesThisInterval = 0;

            // log format state
            if (_videoSource != null)
            {
                var fmts = _videoSource.GetVideoSourceFormats();
                _log.Info($"WebRtcService: formats={fmts.Count}, hasSubs={_videoSource.HasEncodedVideoSubscribers()}, paused={_videoSource.IsVideoSourcePaused()}");
            }

            // Local flag set after the loop to fire a lockless recovery restart
            // from a separate Task.Run (avoids self-deadlock with StopCaptureLoopCore.Wait).
            var recover = false;

            while (!ct.IsCancellationRequested)
            {
                // Check at the top of each iteration so the loop exits cleanly
                // after the streak threshold fires and _needsRecovery is set.
                if (_needsRecovery)
                {
                    _needsRecovery = false;
                    _nullStreakStart = null;
                    recover = true;
                    _log.Info("WebRtcService: exiting capture loop for null-frame recovery restart");
                    break;
                }

                try
                {
                    var frameIntervalMs = 1000 / _targetFps;
                    var frameStart = sw.ElapsedMilliseconds;

                    // T0: capture stage start
                    long tCaptureStart = Stopwatch.GetTimestamp();
                    var captured = _screenCapture.CaptureFrame(_captureBuffer);
                    long tCaptureEnd = Stopwatch.GetTimestamp();
                    int captureUs = (int)((tCaptureEnd - tCaptureStart) * _tsToUs);

                    if (!captured)
                    {
                        captureFailCount++;
                        var failElapsed = sw.ElapsedMilliseconds - frameStart;
                        var failSleep = frameIntervalMs - (int)failElapsed;
                        if (failSleep > 0) await Task.Delay(failSleep, ct);
                        continue;
                    }

                    var stride = _bufferSize / _screenCapture.Height;

                    int encodeW, encodeH;
                    byte[] encodeBuffer;
                    int scaleUs = 0;

                    if (needsScaling)
                    {
                        long tScaleStart = Stopwatch.GetTimestamp();
                        FrameScaler.Scale(_captureBuffer, _screenCapture.Width, _screenCapture.Height, stride,
                                          scaledBuffer!, targetW, targetH);
                        scaleUs = (int)((Stopwatch.GetTimestamp() - tScaleStart) * _tsToUs);
                        encodeW = targetW;
                        encodeH = targetH;
                        encodeBuffer = scaledBuffer!;
                    }
                    else
                    {
                        encodeW = _screenCapture.Width;
                        encodeH = _screenCapture.Height;
                        encodeBuffer = _captureBuffer;
                    }

                    // Dirty detection on the buffer that will be encoded (scaled or native)
                    int encodeStride = needsScaling ? (encodeW * 4) : stride;
                    bool dirty = _dirtyDetector!.IsFrameDirty(encodeBuffer, encodeW, encodeH, encodeStride);

                    if (dirty)
                    {
                        var now = sw.ElapsedMilliseconds;
                        var actualDurationMs = (uint)Math.Max(now - lastEncodeMs, 1);
                        lastEncodeMs = now;

                        _videoSource?.ExternalVideoSourceRawSample(
                            actualDurationMs, encodeW, encodeH, encodeBuffer, VideoPixelFormatsEnum.Bgra);
                        frameCount++;
                        frameSeq++;

                        // Accumulate RTP timestamp: durationRtpUnits mirrors what FFmpegVideoSource
                        // passes to OnVideoSourceEncodedSample.Invoke so the receiver can correlate.
                        uint durationRtpUnits = actualDurationMs > 0
                            ? 90000u * actualDurationMs / 1000u
                            : 90000u / (uint)Math.Max(_targetFps, 1);
                        lastRtpTs += durationRtpUnits;

                        // Read encode timing from FFmpegVideoSource volatile fields.
                        // Written by the capture thread (same thread — volatile fence ensures
                        // the assignment in ExternalVideoSourceRawSample is visible here).
                        int encUs = _videoSource?.LastEncodeUs ?? 0;
                        int encBytes = _videoSource?.LastEncodedBytes ?? 0;

                        // Accumulate per-interval stats for the 1-second drain.
                        captureUsSum += captureUs;
                        encodeUsSum += encUs;
                        framesThisInterval++;
                        bytesThisInterval += encBytes;

                        // Enqueue the per-frame timing entry into the lock-free queue.
                        // This is the ONLY logging call per frame — no file write, no lock.
                        _log.EnqueueTiming(new TimingEntry
                        {
                            Seq = frameSeq,
                            Area = "webrtc",
                            Event = "frame",
                            CaptureUs = captureUs,
                            ScaleUs = scaleUs,
                            EncodeUs = encUs,
                            SentBytes = encBytes,
                            DurationUs = (int)((Stopwatch.GetTimestamp() - tCaptureStart) * _tsToUs)
                        });

                        // Time-window null-frame streak detection. When NullCount keeps
                        // increasing the encoder is stalling; after ~2.5s of continuous
                        // stall, schedule a capture+encoder restart while keeping the
                        // peer connection.
                        var currentNullCount = _videoSource?.NullCount ?? 0;
                        if (currentNullCount > previousNullCount)
                        {
                            _nullStreakStart ??= DateTime.UtcNow;
                            if ((DateTime.UtcNow - _nullStreakStart.Value).TotalSeconds >= 2.5 && !_needsRecovery)
                            {
                                _log.Warning("WebRtcService: null-frame streak > 2.5s, scheduling capture recovery");
                                _needsRecovery = true;
                            }
                        }
                        else
                        {
                            _nullStreakStart = null;
                        }
                        previousNullCount = currentNullCount;
                    }

                    // Stats delivery via data channel every ~1 second
                    if (sw.ElapsedMilliseconds - _lastStatsSendMs >= 1000 && _statsChannel?.IsOpened == true)
                    {
                        // Drain the timing queue (JSONL aggregation) — called ONCE per tick, not per frame.
                        _log.DrainTiming();

                        // Compute per-interval averages from the running sums.
                        int avgCaptureUs = framesThisInterval > 0 ? (int)(captureUsSum / framesThisInterval) : 0;
                        int avgEncodeUs  = framesThisInterval > 0 ? (int)(encodeUsSum  / framesThisInterval) : 0;

                        try
                        {
                            // Extend the existing payload in-place — single send, no second call.
                            var json = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                skipped = _dirtyDetector.FramesSkipped,
                                resolution = _targetResolution,
                                seq = frameSeq,
                                rtpTs = lastRtpTs,
                                captureUs = avgCaptureUs,
                                encodeUs = avgEncodeUs,
                                fps = framesThisInterval,
                                nulls = _videoSource?.NullCount ?? 0,
                                sentBytes = bytesThisInterval
                            });
                            _statsChannel.send(json);
                        }
                        catch { /* channel may close between check and send */ }

                        // Reset per-interval accumulators.
                        captureUsSum = 0;
                        encodeUsSum = 0;
                        framesThisInterval = 0;
                        bytesThisInterval = 0;

                        _lastStatsSendMs = sw.ElapsedMilliseconds;
                    }

                    if (frameCount + captureFailCount <= 3 || (frameCount + captureFailCount) % 60 == 0)
                        _log.Info($"WebRtcService: frame={frameCount} captureFail={captureFailCount} dirty={dirty} skip={_dirtyDetector.FramesSkipped} enc={_videoSource?.EncodedCount} null={_videoSource?.NullCount} res={_targetResolution}p");

                    var elapsed = sw.ElapsedMilliseconds - frameStart;
                    var sleepMs = frameIntervalMs - (int)elapsed;
                    if (sleepMs > 0)
                        await Task.Delay(sleepMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.Error("WebRtcService: capture loop error", ex);
                    await Task.Delay(100, ct);
                }
            }

            _log.Info("WebRtcService: capture loop stopped");

            // If the loop exited due to a null-frame recovery request and the
            // token is not cancelled (i.e. not a deliberate stop), fire the restart from
            // a separate Task.Run. Cannot call RestartCaptureWithNewResolution() directly
            // here — StopCaptureLoopCore would Wait() on THIS task, causing a deadlock.
            if (recover && !ct.IsCancellationRequested)
            {
                _log.Info("WebRtcService: restarting capture for null-frame recovery");
                _ = Task.Run(() => RestartCaptureWithNewResolution());
            }
        }, ct);
    }

    // Thin locked wrapper — serializes callers via _captureLock.
    private void StopCaptureLoop(bool wait = false)
    {
        _captureLock.Wait();
        try { StopCaptureLoopCore(wait); }
        finally { _captureLock.Release(); }
    }

    // Lockless core; must only be called while _captureLock is held.
    private void StopCaptureLoopCore(bool wait = false)
    {
        if (_captureCts != null)
        {
            _captureCts.Cancel();
            if (wait)
            {
                try { _captureTask?.Wait(2000); } catch { /* ignored */ }
            }
            _captureCts.Dispose();
            _captureCts = null;
            _captureTask = null;
        }
    }

    private void CleanupPeerConnection()
    {
        StopCaptureLoop(wait: true);
        lock (_pendingCandidates)
        {
            _pendingCandidates.Clear();
            _remoteDescriptionSet = false;
        }
        _dirtyDetector?.Dispose();
        _dirtyDetector = null;
        _statsChannel = null;
        _lastStatsSendMs = 0;
        // The registry's CancelAll covers Stop / Disconnect / page-refresh
        // uniformly -- closes every open FileStream and deletes every .partial
        // the registry knows about. Runs BEFORE we drop our channel references
        // so any push attempt by a still-running DownloadSender can return cleanly.
        try { _transferRegistry?.CancelAll(); }
        catch (Exception ex) { _log.Error("WebRtcService: CancelAll threw", ex); }
        try { _partialSweeper?.Dispose(); }
        catch (Exception ex) { _log.Error("WebRtcService: sweeper dispose threw", ex); }
        _partialSweeper = null;
        // Dispose the stall watchdog AFTER CancelAll so a final tick cannot
        // race a half-cancelled receiver and push a stale STALLED.
        try { _stallMonitor?.Dispose(); }
        catch (Exception ex) { _log.Error("WebRtcService: stall monitor dispose threw", ex); }
        _stallMonitor = null;
        // Do NOT call dc.close() on _filesCtl / _filesData. The channels are torn
        // down transitively when _pc.close() runs below.
        _filesCtl = null;
        _filesData = null;
        _filesDataRtc = null;
        try { _clipboardSync?.DetachChannel(); }
        catch (Exception ex) { _log.Warning($"WebRtcService: DetachChannel threw: {ex.Message}"); }
        _clipboardCtl = null;
        _clipboardOriginId = null;
        // Note: do NOT null out _clipboardSync -- it's a DI-injected singleton with process lifetime.
        // The watcher subscription on _clipboardSync stays live across reconnects (watcher always-on).
        if (_videoSource != null)
        {
            _videoSource.Dispose();
            _videoSource = null;
        }
        if (_pc != null)
        {
            _pc.close();
            _pc.Dispose();
            _pc = null;
        }
    }

    private async Task SendSignalSafe(string message)
    {
        try { await _sendSignal(message); }
        catch (Exception ex) { _log.Error("WebRtcService: failed to send signaling message", ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _log.Info("WebRtcService: disposing");
        CleanupPeerConnection();
    }
}
