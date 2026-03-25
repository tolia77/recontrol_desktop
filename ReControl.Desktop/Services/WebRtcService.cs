using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Services;

/// <summary>
/// WebRTC peer connection lifecycle, VP8 encoding, and screen capture feed loop.
/// Includes tile-based dirty region detection to skip encoding unchanged frames,
/// resolution preset support for configurable downscaling, and adaptive quality
/// adjustment based on bandwidth feedback.
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

    // Resolution preset and dirty region detection
    private ResolutionPreset _currentPreset = ResolutionPreset.Native;
    private byte[]? _previousFrame;
    private int _previousWidth;
    private int _previousHeight;
    private const int TileSize = 64;
    private volatile bool _forceKeyframe;

    // Adaptive quality: when true, AdjustQuality won't auto-change the preset
    private bool _manualPresetOverride;

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
    /// Change the resolution preset. Forces a keyframe on the next frame and clears
    /// the previous frame buffer so the full frame is encoded at the new dimensions.
    /// Sets the manual override flag to prevent adaptive quality from changing it.
    /// </summary>
    public void SetResolution(ResolutionPreset preset)
    {
        _log.Info($"WebRtcService: resolution preset changed to {preset}");
        _currentPreset = preset;
        _forceKeyframe = true;
        _previousFrame = null;
        _manualPresetOverride = true;
    }

    /// <summary>
    /// Adjust resolution preset based on available bandwidth. Auto-downgrades when
    /// bandwidth is insufficient, auto-upgrades when bandwidth improves.
    /// Skips adjustment if the user manually set a preset.
    /// </summary>
    public void AdjustQuality(int availableBitrateKbps)
    {
        if (_manualPresetOverride)
        {
            _log.Info($"WebRtcService: adaptive quality skipped (manual override active), bitrate={availableBitrateKbps}kbps");
            return;
        }

        var previous = _currentPreset;

        if (availableBitrateKbps < 500 && _currentPreset != ResolutionPreset.P480)
        {
            SetResolutionInternal(ResolutionPreset.P480);
        }
        else if (availableBitrateKbps < 1500 && _currentPreset < ResolutionPreset.P720)
        {
            // Current preset is Native or P1080, downgrade to 720p
            SetResolutionInternal(ResolutionPreset.P720);
        }
        else if (availableBitrateKbps > 3000 && _currentPreset > ResolutionPreset.Native)
        {
            // Upgrade one step: P480 -> P720 -> P1080 -> Native
            var upgraded = _currentPreset switch
            {
                ResolutionPreset.P480 => ResolutionPreset.P720,
                ResolutionPreset.P720 => ResolutionPreset.P1080,
                ResolutionPreset.P1080 => ResolutionPreset.Native,
                _ => _currentPreset
            };
            SetResolutionInternal(upgraded);
        }

        if (_currentPreset != previous)
        {
            _log.Info($"WebRtcService: adaptive quality {previous} -> {_currentPreset} (bitrate={availableBitrateKbps}kbps)");
        }
    }

    /// <summary>
    /// Internal resolution change for adaptive quality (does not set manual override).
    /// </summary>
    private void SetResolutionInternal(ResolutionPreset preset)
    {
        _currentPreset = preset;
        _forceKeyframe = true;
        _previousFrame = null;
    }

    /// <summary>
    /// Start the screen capture feed loop, encoding frames as VP8 and sending via RTP.
    /// Uses resolution-aware capture and tile-based dirty region detection to skip
    /// encoding unchanged frames.
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
                        // Resolution-aware capture
                        var (nativeW, nativeH) = _screenCapture.GetScreenSize();
                        var (targetW, targetH) = ResolutionPresets.ComputeTargetSize(
                            _currentPreset, nativeW, nativeH);

                        byte[] bgr;
                        int w, h, stride;
                        if (_currentPreset == ResolutionPreset.Native)
                        {
                            bgr = _screenCapture.CaptureFrame(out w, out h, out stride);
                        }
                        else
                        {
                            bgr = _screenCapture.CaptureFrame(targetW, targetH, out stride);
                            w = targetW;
                            h = targetH;
                        }

                        // Dirty region detection: skip encoding if frame is unchanged
                        bool shouldEncode = _previousFrame == null
                            || _forceKeyframe
                            || w != _previousWidth || h != _previousHeight
                            || HasSignificantChange(bgr, _previousFrame, w, h, stride);

                        if (shouldEncode)
                        {
                            var i420 = PixelConverter.ToI420(w, h, stride, bgr, VideoPixelFormatsEnum.Bgr);
                            var encoded = _encoder!.EncodeVideo(
                                w, h, i420, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);

                            if (encoded != null)
                            {
                                _pc?.SendVideo(VideoTimestampSpacing, encoded);
                            }

                            _forceKeyframe = false;
                        }

                        // Store for next comparison
                        _previousFrame = bgr;
                        _previousWidth = w;
                        _previousHeight = h;
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
    /// Tile-based dirty region detection. Compares current frame to previous frame
    /// by sampling rows within each 64x64 tile (every 4th row for speed).
    /// Returns true if any tile has changed, indicating the frame should be encoded.
    /// </summary>
    private static bool HasSignificantChange(byte[] current, byte[] previous, int width, int height, int stride)
    {
        if (current.Length != previous.Length) return true;

        int cols = (width + TileSize - 1) / TileSize;
        int rows = (height + TileSize - 1) / TileSize;
        int bytesPerPixel = 3; // BGR24

        for (int ry = 0; ry < rows; ry++)
        {
            for (int rx = 0; rx < cols; rx++)
            {
                int tileX = rx * TileSize;
                int tileY = ry * TileSize;
                int tileW = Math.Min(TileSize, width - tileX);
                int tileH = Math.Min(TileSize, height - tileY);

                // Sample every 4th row within the tile for speed
                for (int row = 0; row < tileH; row += 4)
                {
                    int offset = (tileY + row) * stride + tileX * bytesPerPixel;
                    int length = tileW * bytesPerPixel;

                    if (offset + length > current.Length) continue;

                    // Compare the sampled row bytes
                    for (int i = 0; i < length; i += 8)
                    {
                        int remaining = Math.Min(8, length - i);
                        for (int j = 0; j < remaining; j++)
                        {
                            if (current[offset + i + j] != previous[offset + i + j])
                                return true;
                        }
                    }
                }
            }
        }

        return false;
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

            // Clean up frame comparison state
            _previousFrame = null;
            _previousWidth = 0;
            _previousHeight = 0;
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
