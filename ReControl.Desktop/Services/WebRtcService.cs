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

    private volatile int _targetFps = 24;
    private int _consecutiveNullFrames;

    public WebRtcService(LogService log, Func<string, Task> sendSignal, IScreenCaptureService? screenCapture = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sendSignal = sendSignal ?? throw new ArgumentNullException(nameof(sendSignal));
        _screenCapture = screenCapture;

        if (_screenCapture != null)
        {
            _captureBuffer = new byte[_screenCapture.Width * _screenCapture.Height * 4];
            _log.Info($"WebRtcService: screen capture available ({_screenCapture.Width}x{_screenCapture.Height})");
        }
    }

    public void SetTargetFps(int fps)
    {
        fps = Math.Clamp(fps, 1, 30);
        _targetFps = fps;
        _log.Info($"WebRtcService: target FPS changed to {fps}");
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

        // Set up video encoding via FFmpeg with H.264 (preferred) + VP8 fallback
        var encoderOptions = new Dictionary<string, string>
        {
            { "preset", "ultrafast" }
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
            var frameCount = 0;
            var captureFailCount = 0;
            var previousNullCount = _videoSource?.NullCount ?? 0;
            _consecutiveNullFrames = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

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

                    if (captured)
                    {
                        _videoSource?.ExternalVideoSourceRawSample(
                            (uint)frameIntervalMs,
                            _screenCapture.Width,
                            _screenCapture.Height,
                            _captureBuffer,
                            VideoPixelFormatsEnum.Bgra);
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
                    else
                    {
                        captureFailCount++;
                    }

                    if (frameCount + captureFailCount <= 3 || (frameCount + captureFailCount) % 60 == 0)
                        _log.Info($"WebRtcService: frame={frameCount} captureFail={captureFailCount} captured={captured} enc={_videoSource?.EncodedCount} null={_videoSource?.NullCount} skip={_videoSource?.LastSkipReason}");

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
