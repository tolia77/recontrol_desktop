using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Services;

/// <summary>
/// WebRTC peer connection lifecycle, VP8 encoding, and screen capture feed loop.
/// Ported from WPF WebRtcService with cross-platform encoder selection
/// (VpxVideoEncoder on Windows, FFmpegVideoEncoder on Linux).
/// </summary>
public sealed class WebRtcService : IDisposable
{
    private readonly IScreenCaptureService _screenCapture;
    private readonly LogService _log;
    private readonly Func<string, Task> _sendSignal;

    private RTCPeerConnection? _pc;
    private CancellationTokenSource? _feedCts;
    private Task? _feedTask;
    private IVideoEncoder? _encoder;
    private readonly object _lock = new();
    private volatile bool _disposed;

    /// <summary>
    /// VP8 clock rate is 90kHz. For 30fps: 90000 / 30 = 3000.
    /// </summary>
    private const uint VideoTimestampSpacing = 3000;

    public WebRtcService(IScreenCaptureService screenCapture, LogService log, Func<string, Task> sendSignal)
    {
        _screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sendSignal = sendSignal ?? throw new ArgumentNullException(nameof(sendSignal));
    }

    /// <summary>
    /// Handle an incoming WebRTC offer from the browser.
    /// Creates a peer connection, adds VP8 video track, sets remote description,
    /// creates and sends an answer, and wires ICE and connection state handlers.
    /// </summary>
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

        // Add VP8 video track (SendOnly)
        var vp8Format = new SDPAudioVideoMediaFormat(new VideoFormat(VideoCodecsEnum.VP8, 96));
        var videoTrack = new MediaStreamTrack(
            SDPMediaTypesEnum.video,
            false,
            new List<SDPAudioVideoMediaFormat> { vp8Format },
            MediaStreamStatusEnum.SendOnly
        );
        _pc.addTrack(videoTrack);

        // ICE candidate handler -- send candidates to remote peer via ActionCable
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

        // Connection state handler
        _pc.onconnectionstatechange += (state) =>
        {
            _log.Info($"WebRtcService: connection state -> {state}");
            switch (state)
            {
                case RTCPeerConnectionState.connected:
                    StartScreenFeed();
                    break;
                case RTCPeerConnectionState.failed:
                case RTCPeerConnectionState.closed:
                case RTCPeerConnectionState.disconnected:
                    StopScreenFeed();
                    break;
            }
        };

        // Set remote description (the offer)
        var remoteDesc = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = sdp
        };
        var setResult = _pc.setRemoteDescription(remoteDesc);
        if (setResult != SetDescriptionResultEnum.OK)
        {
            _log.Error($"WebRtcService: failed to set remote description: {setResult}");
            throw new InvalidOperationException($"Failed to set remote description: {setResult}");
        }

        // Create answer
        var answer = _pc.createAnswer();
        await _pc.setLocalDescription(answer);

        // Send answer back via signaling
        var answerPayload = new
        {
            command = "webrtc.answer",
            payload = new
            {
                sdp = answer.sdp
            }
        };
        await SendSignalSafe(System.Text.Json.JsonSerializer.Serialize(answerPayload));

        _log.Info("WebRtcService: answer sent");
    }

    /// <summary>
    /// Add an ICE candidate received from the remote peer.
    /// </summary>
    public void HandleIceCandidate(string candidate, string? sdpMid, ushort? sdpMLineIndex)
    {
        if (_pc == null)
        {
            _log.Warning("WebRtcService: HandleIceCandidate called with no peer connection");
            return;
        }

        _log.Info("WebRtcService: adding remote ICE candidate");
        var iceCandidate = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid ?? "0",
            sdpMLineIndex = sdpMLineIndex ?? 0
        };
        _pc.addIceCandidate(iceCandidate);
    }

    /// <summary>
    /// Stop the WebRTC session and tear down the peer connection.
    /// </summary>
    public void Stop()
    {
        _log.Info("WebRtcService: stop requested");
        CleanupPeerConnection();
    }

    /// <summary>
    /// Start the screen capture feed loop, encoding frames as VP8 and sending via RTP.
    /// </summary>
    private void StartScreenFeed()
    {
        lock (_lock)
        {
            if (_feedCts != null) return; // Already running

            _log.Info("WebRtcService: starting screen feed (30fps target)");

            _encoder = CreateEncoder();
            _feedCts = new CancellationTokenSource();
            var token = _feedCts.Token;

            _feedTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        byte[] bgr = _screenCapture.CaptureFrame(out int w, out int h, out int stride);
                        var i420 = PixelConverter.ToI420(w, h, stride, bgr, VideoPixelFormatsEnum.Bgr);
                        var encoded = _encoder!.EncodeVideo(w, h, i420, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);

                        if (encoded != null)
                        {
                            _pc?.SendVideo(VideoTimestampSpacing, encoded);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error("WebRtcService: screen feed error", ex);
                    }

                    stopwatch.Stop();
                    int elapsed = (int)stopwatch.ElapsedMilliseconds;
                    int delay = Math.Max(1, 33 - elapsed); // 33ms target for 30fps
                    try
                    {
                        await Task.Delay(delay, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                _log.Info("WebRtcService: screen feed loop exited");
            }, token);
        }
    }

    /// <summary>
    /// Stop the screen capture feed loop and dispose the encoder.
    /// </summary>
    private void StopScreenFeed()
    {
        lock (_lock)
        {
            if (_feedCts == null) return;

            _log.Info("WebRtcService: stopping screen feed");

            _feedCts.Cancel();

            try
            {
                _feedTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Expected from cancellation
            }

            _feedCts.Dispose();
            _feedCts = null;
            _feedTask = null;

            if (_encoder is IDisposable disposableEncoder)
            {
                disposableEncoder.Dispose();
            }
            _encoder = null;
        }
    }

    /// <summary>
    /// Create a VP8 encoder appropriate for the current platform.
    /// Windows: VpxVideoEncoder (native vpxmd.dll)
    /// Linux: FFmpegVideoEncoder (system FFmpeg libraries)
    /// </summary>
    private static IVideoEncoder CreateEncoder()
    {
        if (OperatingSystem.IsWindows())
        {
            return new SIPSorceryMedia.Encoders.VpxVideoEncoder();
        }
        else
        {
            return new SIPSorceryMedia.FFmpeg.FFmpegVideoEncoder();
        }
    }

    /// <summary>
    /// Tear down the peer connection and stop the screen feed.
    /// </summary>
    private void CleanupPeerConnection()
    {
        StopScreenFeed();

        if (_pc != null)
        {
            _pc.close();
            _pc.Dispose();
            _pc = null;
        }
    }

    /// <summary>
    /// Send a signaling message through ActionCable, catching and logging errors.
    /// </summary>
    private async Task SendSignalSafe(string message)
    {
        try
        {
            await _sendSignal(message);
        }
        catch (Exception ex)
        {
            _log.Error("WebRtcService: failed to send signaling message", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _log.Info("WebRtcService: disposing");
        CleanupPeerConnection();
    }
}
