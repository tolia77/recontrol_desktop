using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;
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
    private readonly LogService _log;
    private readonly Func<string, Task> _sendSignal;
    private readonly IScreenCaptureService? _screenCapture;

    private RTCPeerConnection? _pc;
    private FFmpegVideoSource? _videoSource;
    private volatile bool _disposed;
    private readonly List<RTCIceCandidateInit> _pendingCandidates = new();

    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private byte[]? _captureBuffer;
    private int _bufferSize;

    private volatile int _targetFps = 24;
    private int _consecutiveNullFrames;
    private volatile int _targetResolution = 1080;

    private DirtyDetector? _dirtyDetector;
    private RTCDataChannel? _statsChannel;
    private long _lastStatsSendMs;

    public WebRtcService(LogService log, Func<string, Task> sendSignal, IScreenCaptureService? screenCapture = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sendSignal = sendSignal ?? throw new ArgumentNullException(nameof(sendSignal));
        _screenCapture = screenCapture;

        if (_screenCapture != null)
        {
            _bufferSize = _screenCapture.Width * _screenCapture.Height * 4;
            _captureBuffer = new byte[_bufferSize];
            _log.Info($"WebRtcService: screen capture available ({_screenCapture.Width}x{_screenCapture.Height})");
        }
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

    public async Task HandleOfferAsync(string sdp)
    {
        _log.Info("WebRtcService: handling offer");
        CleanupPeerConnection();

        var config = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer>
            {
                new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
            }
        };

        _pc = new RTCPeerConnection(config);

        _statsChannel = await _pc.createDataChannel("stats");
        _dirtyDetector = new DirtyDetector();

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
    }

    public void HandleIceCandidate(string candidate, string? sdpMid, ushort? sdpMLineIndex)
    {
        var iceCandidate = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid ?? "0",
            sdpMLineIndex = sdpMLineIndex ?? 0
        };

        if (_pc == null)
        {
            _log.Info("WebRtcService: buffering ICE candidate (peer connection not ready)");
            lock (_pendingCandidates) { _pendingCandidates.Add(iceCandidate); }
            return;
        }

        _log.Info("WebRtcService: adding remote ICE candidate");
        _pc.addIceCandidate(iceCandidate);
    }

    public void Stop()
    {
        _log.Info("WebRtcService: stop requested");
        StopCaptureLoop(wait: true);
        CleanupPeerConnection();
    }

    private void RestartCaptureWithNewResolution()
    {
        StopCaptureLoop(wait: true);

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

        StartCaptureLoop();
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

    private void StartCaptureLoop()
    {
        if (_screenCapture == null || _captureBuffer == null)
        {
            _log.Warning("WebRtcService: no screen capture service, skipping capture loop");
            return;
        }

        StopCaptureLoop();

        _captureCts = new CancellationTokenSource();
        var ct = _captureCts.Token;

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
            _consecutiveNullFrames = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lastEncodeMs = 0L;

            // log format state
            if (_videoSource != null)
            {
                var fmts = _videoSource.GetVideoSourceFormats();
                _log.Info($"WebRtcService: formats={fmts.Count}, hasSubs={_videoSource.HasEncodedVideoSubscribers()}, paused={_videoSource.IsVideoSourcePaused()}");
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var frameIntervalMs = 1000 / _targetFps;
                    var frameStart = sw.ElapsedMilliseconds;
                    var captured = _screenCapture.CaptureFrame(_captureBuffer);

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

                    if (needsScaling)
                    {
                        FrameScaler.Scale(_captureBuffer, _screenCapture.Width, _screenCapture.Height, stride,
                                          scaledBuffer!, targetW, targetH);
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

                        // Track consecutive null frames for H.264 encoding health
                        var currentNullCount = _videoSource?.NullCount ?? 0;
                        if (currentNullCount > previousNullCount)
                        {
                            _consecutiveNullFrames++;
                            if (_consecutiveNullFrames == 30)
                                _log.Warning($"WebRtcService: H.264 encoding may have failed, consecutive null frames: {_consecutiveNullFrames}");
                        }
                        else
                        {
                            _consecutiveNullFrames = 0;
                        }
                        previousNullCount = currentNullCount;
                    }

                    // Stats delivery via data channel every ~1 second
                    if (sw.ElapsedMilliseconds - _lastStatsSendMs >= 1000 && _statsChannel?.IsOpened == true)
                    {
                        try
                        {
                            var json = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                skipped = _dirtyDetector.FramesSkipped,
                                resolution = _targetResolution
                            });
                            _statsChannel.send(json);
                        }
                        catch { /* channel may close between check and send */ }
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
        }, ct);
    }

    private void StopCaptureLoop(bool wait = false)
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
        lock (_pendingCandidates) { _pendingCandidates.Clear(); }
        _dirtyDetector?.Dispose();
        _dirtyDetector = null;
        _statsChannel = null;
        _lastStatsSendMs = 0;
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
