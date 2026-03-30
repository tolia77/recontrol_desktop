using System;
using System.Numerics;
using System.Threading;

namespace ReControl.Desktop.Services;

internal sealed class DirtyDetector : IDisposable
{
    private const int TileSize = 64;
    private const int BytesPerPixel = 4;
    private const byte Threshold = 3;
    private const int RowSampleStride = 4;

    private byte[]? _previousFrame;
    private int _prevWidth;
    private int _prevHeight;
    private long _framesSkipped;

    public long FramesSkipped => Interlocked.Read(ref _framesSkipped);

    public bool IsFrameDirty(byte[] currentFrame, int width, int height, int stride)
    {
        if (_previousFrame == null || width != _prevWidth || height != _prevHeight)
        {
            StoreFrame(currentFrame, width, height);
            return true;
        }

        int tilesX = (width + TileSize - 1) / TileSize;
        int tilesY = (height + TileSize - 1) / TileSize;

        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (IsTileDirty(currentFrame, _previousFrame, tx, ty, width, height, stride))
                {
                    StoreFrame(currentFrame, width, height);
                    return true;
                }
            }
        }

        Interlocked.Increment(ref _framesSkipped);
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

    public void Dispose()
    {
        _previousFrame = null;
    }
}
