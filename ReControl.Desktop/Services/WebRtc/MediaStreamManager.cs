using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Services.WebRtc;

/// <summary>
/// Start the screen capture feed loop, encoding frames as VP8 and sending via RTP.
/// Uses resolution-aware capture and tile-based dirty region detection to skip
/// encoding unchanged frames.
/// </summary>
public class MediaStreamManager
{
    private readonly IScreenCaptureService _screenCapture;
    private readonly LogService _log;
    private readonly object _lock = new();

    private CancellationTokenSource? _feedCts;
    private Task? _feedTask;
    private IVideoEncoder? _encoder;
    
    public ResolutionPreset CurrentPreset { get; private set; } = ResolutionPreset.Native;
    public bool ManualPresetOverride { get; set; }
    
    private byte[]? _previousFrame;
    private int _previousWidth;
    private int _previousHeight;
    private const int TileSize = 64;
    private volatile bool _forceKeyframe;

    /// <summary>
    /// VP8 clock rate is 90kHz. For 30fps: 90000 / 30 = 3000.
    /// </summary>
    private const uint VideoTimestampSpacing = 3000;

    public MediaStreamManager(IScreenCaptureService screenCapture, LogService log)
    {
        _screenCapture = screenCapture;
        _log = log;
    }

    public void StartScreenFeed(RTCPeerConnection? pc)
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
                            CurrentPreset, nativeW, nativeH);

                        byte[] bgr;
                        int w, h, stride;
                        if (CurrentPreset == ResolutionPreset.Native)
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
                                pc?.SendVideo(VideoTimestampSpacing, encoded);
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

    public void StopScreenFeed()
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

    public void SetResolution(ResolutionPreset preset)
    {
        _log.Info($"WebRtcService: resolution preset changed to {preset}");
        CurrentPreset = preset;
        _forceKeyframe = true;
        _previousFrame = null;
        ManualPresetOverride = true;
    }

    public void SetResolutionInternal(ResolutionPreset preset)
    {
        CurrentPreset = preset;
        _forceKeyframe = true;
        _previousFrame = null;
    }

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

                for (int row = 0; row < tileH; row += 4)
                {
                    int offset = (tileY + row) * stride + tileX * bytesPerPixel;
                    int length = tileW * bytesPerPixel;

                    if (offset + length > current.Length) continue;

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
}
