using System;

namespace ReControl.Desktop.Services;

/// <summary>
/// Bilinear interpolation scaler for raw BGRA pixel buffers.
/// Used to downscale captured frames before H.264 encoding when
/// the target resolution is below native.
/// </summary>
internal static class FrameScaler
{
    private const int BytesPerPixel = 4;

    /// <summary>
    /// Scale a BGRA frame from source dimensions to destination dimensions
    /// using bilinear interpolation.
    /// </summary>
    /// <param name="source">Source BGRA pixel data</param>
    /// <param name="srcWidth">Source width in pixels</param>
    /// <param name="srcHeight">Source height in pixels</param>
    /// <param name="srcStride">Source row stride in bytes (may include X11 padding)</param>
    /// <param name="destination">Destination buffer (must be dstWidth * dstHeight * 4 bytes)</param>
    /// <param name="dstWidth">Destination width in pixels</param>
    /// <param name="dstHeight">Destination height in pixels</param>
    public static void Scale(
        byte[] source, int srcWidth, int srcHeight, int srcStride,
        byte[] destination, int dstWidth, int dstHeight)
    {
        int dstStride = dstWidth * BytesPerPixel;
        float xRatio = (float)(srcWidth - 1) / (dstWidth - 1);
        float yRatio = (float)(srcHeight - 1) / (dstHeight - 1);

        for (int y = 0; y < dstHeight; y++)
        {
            float srcY = y * yRatio;
            int y0 = (int)srcY;
            int y1 = Math.Min(y0 + 1, srcHeight - 1);
            float yFrac = srcY - y0;
            float yInv = 1f - yFrac;

            for (int x = 0; x < dstWidth; x++)
            {
                float srcX = x * xRatio;
                int x0 = (int)srcX;
                int x1 = Math.Min(x0 + 1, srcWidth - 1);
                float xFrac = srcX - x0;
                float xInv = 1f - xFrac;

                // Four source pixel offsets using srcStride for correct row addressing
                int i00 = y0 * srcStride + x0 * BytesPerPixel;
                int i10 = y0 * srcStride + x1 * BytesPerPixel;
                int i01 = y1 * srcStride + x0 * BytesPerPixel;
                int i11 = y1 * srcStride + x1 * BytesPerPixel;

                int dstIdx = y * dstStride + x * BytesPerPixel;

                // Bilinear interpolation for each BGRA channel
                for (int c = 0; c < BytesPerPixel; c++)
                {
                    float val = source[i00 + c] * xInv * yInv
                              + source[i10 + c] * xFrac * yInv
                              + source[i01 + c] * xInv * yFrac
                              + source[i11 + c] * xFrac * yFrac;
                    destination[dstIdx + c] = (byte)Math.Clamp(val + 0.5f, 0f, 255f);
                }
            }
        }
    }
}
