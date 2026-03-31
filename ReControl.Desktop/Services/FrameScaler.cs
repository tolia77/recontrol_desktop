using System;

namespace ReControl.Desktop.Services;

/// <summary>
/// Bilinear interpolation scaler for raw BGRA pixel buffers.
/// Used to downscale captured frames before H.264 encoding when
/// the target resolution is below native.
/// Uses unsafe pointers to avoid bounds checking on the hot path.
/// </summary>
internal static class FrameScaler
{
    private const int Bpp = 4; // BGRA

    /// <summary>
    /// Scale a BGRA frame from source dimensions to destination dimensions
    /// using bilinear interpolation with unsafe pointer access.
    /// </summary>
    public static unsafe void Scale(
        byte[] source, int srcWidth, int srcHeight, int srcStride,
        byte[] destination, int dstWidth, int dstHeight)
    {
        int dstStride = dstWidth * Bpp;
        float xRatio = (float)(srcWidth - 1) / (dstWidth - 1);
        float yRatio = (float)(srcHeight - 1) / (dstHeight - 1);

        fixed (byte* srcPtr = source, dstPtr = destination)
        {
            for (int y = 0; y < dstHeight; y++)
            {
                float srcY = y * yRatio;
                int y0 = (int)srcY;
                int y1 = y0 + 1;
                if (y1 >= srcHeight) y1 = srcHeight - 1;
                float yf = srcY - y0;
                float yi = 1f - yf;

                int rowOff0 = y0 * srcStride;
                int rowOff1 = y1 * srcStride;
                byte* dst = dstPtr + y * dstStride;

                for (int x = 0; x < dstWidth; x++)
                {
                    float srcX = x * xRatio;
                    int x0 = (int)srcX;
                    int x1 = x0 + 1;
                    if (x1 >= srcWidth) x1 = srcWidth - 1;
                    float xf = srcX - x0;
                    float xi = 1f - xf;

                    byte* p00 = srcPtr + rowOff0 + x0 * Bpp;
                    byte* p10 = srcPtr + rowOff0 + x1 * Bpp;
                    byte* p01 = srcPtr + rowOff1 + x0 * Bpp;
                    byte* p11 = srcPtr + rowOff1 + x1 * Bpp;

                    float w00 = xi * yi;
                    float w10 = xf * yi;
                    float w01 = xi * yf;
                    float w11 = xf * yf;

                    // Unrolled BGRA channels
                    dst[0] = (byte)(p00[0] * w00 + p10[0] * w10 + p01[0] * w01 + p11[0] * w11 + 0.5f);
                    dst[1] = (byte)(p00[1] * w00 + p10[1] * w10 + p01[1] * w01 + p11[1] * w11 + 0.5f);
                    dst[2] = (byte)(p00[2] * w00 + p10[2] * w10 + p01[2] * w01 + p11[2] * w11 + 0.5f);
                    dst[3] = (byte)(p00[3] * w00 + p10[3] * w10 + p01[3] * w01 + p11[3] * w11 + 0.5f);
                    dst += Bpp;
                }
            }
        }
    }
}
