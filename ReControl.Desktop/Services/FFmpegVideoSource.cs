using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SIPSorceryMedia.Abstractions;

namespace ReControl.Desktop.Services;

/// <summary>
/// IVideoSource implementation that encodes raw frames using an IVideoEncoder (FFmpeg).
/// Drop-in replacement for VideoEncoderEndPoint that works cross-platform.
/// </summary>
internal sealed class FFmpegVideoSource : IVideoSource, IDisposable
{
    private const uint VideoSamplingRate = 90000;
    private const uint DefaultFps = 30;

    private readonly IVideoEncoder _encoder;
    private readonly MediaFormatManager<VideoFormat> _formatManager;
    private bool _closed;
    private bool _paused;
    private int _encodeCount;
    private int _nullCount;

    public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
    public event RawVideoSampleDelegate? OnVideoSourceRawSample;
    public event RawVideoSampleFasterDelegate? OnVideoSourceRawSampleFaster;
    public event SourceErrorDelegate? OnVideoSourceError;

    public FFmpegVideoSource(IVideoEncoder encoder)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _formatManager = new MediaFormatManager<VideoFormat>(_encoder.SupportedFormats);
    }

    public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
    public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
    public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
    public void ForceKeyFrame() => _encoder.ForceKeyFrame();
    public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
    public bool IsVideoSourcePaused() => _paused;

    public Task PauseVideo() { _paused = true; return Task.CompletedTask; }
    public Task ResumeVideo() { _paused = false; return Task.CompletedTask; }
    public Task StartVideo() { _closed = false; return Task.CompletedTask; }
    public Task CloseVideo() { _closed = true; return Task.CompletedTask; }

    public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        if (_closed) { _skipReason = "closed"; return; }
        if (_paused) { _skipReason = "paused"; return; }
        if (OnVideoSourceEncodedSample == null) { _skipReason = "no_subscribers"; return; }

        var format = _formatManager.SelectedFormat;
        if (format.IsEmpty()) { _skipReason = "format_empty"; return; }

        _skipReason = null;
        var encoded = _encoder.EncodeVideo(width, height, sample, pixelFormat, format.Codec);

        if (encoded != null)
        {
            _encodeCount++;
            uint durationRtpUnits = durationMilliseconds > 0
                ? VideoSamplingRate * durationMilliseconds / 1000
                : VideoSamplingRate / DefaultFps;
            OnVideoSourceEncodedSample.Invoke(durationRtpUnits, encoded);
        }
        else
        {
            _nullCount++;
        }
    }

    // Diagnostic accessors
    internal string? LastSkipReason => _skipReason;
    internal int EncodedCount => _encodeCount;
    internal int NullCount => _nullCount;
    private string? _skipReason;

    public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage)
    {
        if (_closed || _paused) return;
        if (OnVideoSourceEncodedSample == null) return;

        var codec = _formatManager.SelectedFormat.Codec;
        var encoded = _encoder.EncodeVideoFaster(rawImage, codec);

        if (encoded != null)
        {
            uint fps = durationMilliseconds > 0 ? 1000 / durationMilliseconds : DefaultFps;
            uint durationRtpUnits = VideoSamplingRate / fps;
            OnVideoSourceEncodedSample.Invoke(durationRtpUnits, encoded);
        }
    }

    public void Dispose()
    {
        _closed = true;
        _encoder.Dispose();
    }
}
