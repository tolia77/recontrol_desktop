using System;
using System.Numerics;
using System.Threading;

namespace ReControl.Desktop.Services;

/// <summary>
/// Tile-based SIMD frame comparison with threshold tolerance and idle state tracking.
/// Compares each captured frame against the previous frame to determine if encoding is needed.
/// </summary>
internal sealed class DirtyDetector : IDisposable
{
    private const int TileSize = 64;
    private const int BytesPerPixel = 4;       // BGRA
    private const byte Threshold = 3;          // Per-channel tolerance for compression artifacts
    private const int RowSampleStride = 4;     // Check every 4th row per tile
    private const long KeyframeIntervalMs = 5000; // ~5 second idle keyframes

    private byte[]? _previousFrame;
    private int _prevWidth;
    private int _prevHeight;
    private bool _isIdle;
    private long _framesSkipped;
    private long _lastEncodeTimestampMs;
    private bool _wasIdle;

    public bool IsIdle => _isIdle;
    public long FramesSkipped => Interlocked.Read(ref _framesSkipped);

    /// <summary>
    /// Compare current frame against previous. Returns true if encoding is needed.
    /// Handles first-frame, dimension-change, and threshold-based tile comparison.
    /// </summary>
    public bool IsFrameDirty(byte[] currentFrame, int width, int height, int stride)
    {
        // First frame or dimension change: always dirty
        if (_previousFrame == null || width != _prevWidth || height != _prevHeight)
        {
            StoreFrame(currentFrame, width, height);
            SetActive();
            return true;
        }

        // Tile-based comparison with early exit
        int tilesX = (width + TileSize - 1) / TileSize;
        int tilesY = (height + TileSize - 1) / TileSize;

        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (IsTileDirty(currentFrame, _previousFrame, tx, ty, width, height, stride))
                {
                    StoreFrame(currentFrame, width, height);
                    SetActive();
                    return true;
                }
            }
        }

        // Frame is clean
        Interlocked.Increment(ref _framesSkipped);
        if (!_isIdle) SetIdle();
        return false;
    }

    /// <summary>
    /// Check if periodic idle keyframe is due (every ~5 seconds).
    /// </summary>
    public bool ShouldSendIdleKeyframe(long currentElapsedMs)
    {
        if (!_isIdle) return false;
        return (currentElapsedMs - _lastEncodeTimestampMs) >= KeyframeIntervalMs;
    }

    /// <summary>
    /// Call after encoding a frame to reset idle timer.
    /// </summary>
    public void OnFrameEncoded(long currentElapsedMs)
    {
        _lastEncodeTimestampMs = currentElapsedMs;
    }

    /// <summary>
    /// Returns and clears the was-idle flag (for forcing keyframe on first dirty after idle).
    /// </summary>
    public bool ConsumeWasIdle()
    {
        if (_wasIdle)
        {
            _wasIdle = false;
            return true;
        }
        return false;
    }

    private static bool IsTileDirty(
        ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous,
        int tileX, int tileY,
        int frameWidth, int frameHeight, int stride)
    {
        int startX = tileX * TileSize;
        int startY = tileY * TileSize;
        int endX = Math.Min(startX + TileSize, frameWidth);
        int endY = Math.Min(startY + TileSize, frameHeight);
        int tileWidthBytes = (endX - startX) * BytesPerPixel;

        var threshVec = new Vector<byte>(Threshold);
        int vecSize = Vector<byte>.Count;

        for (int y = startY; y < endY; y += RowSampleStride)
        {
            int offset = y * stride + startX * BytesPerPixel;
            var rowCurrent = current.Slice(offset, tileWidthBytes);
            var rowPrevious = previous.Slice(offset, tileWidthBytes);

            int i = 0;
            for (; i + vecSize <= tileWidthBytes; i += vecSize)
            {
                var a = new Vector<byte>(rowCurrent.Slice(i, vecSize));
                var b = new Vector<byte>(rowPrevious.Slice(i, vecSize));
                var diff = Vector.Max(a, b) - Vector.Min(a, b);
                if (Vector.GreaterThanAny(diff, threshVec))
                    return true;
            }

            // Scalar tail for remaining bytes
            for (; i < tileWidthBytes; i++)
            {
                if (Math.Abs((int)rowCurrent[i] - (int)rowPrevious[i]) > Threshold)
                    return true;
            }
        }

        return false;
    }

    private void StoreFrame(byte[] frame, int width, int height)
    {
        if (_previousFrame == null || _previousFrame.Length != frame.Length)
            _previousFrame = new byte[frame.Length];
        Buffer.BlockCopy(frame, 0, _previousFrame, 0, frame.Length);
        _prevWidth = width;
        _prevHeight = height;
    }

    private void SetActive()
    {
        if (_isIdle)
        {
            _wasIdle = true;
            _isIdle = false;
        }
    }

    private void SetIdle()
    {
        _isIdle = true;
    }

    public void Dispose()
    {
        _previousFrame = null;
    }
}
