using System;
using System.Runtime.InteropServices;

namespace ReControl.Desktop.Platform.Linux.X11;

/// <summary>
/// Static utilities for image processing and format conversion required by the capture service.
/// </summary>
public static class ImageProcessingUtils
{
    /// <summary>
    /// Round a dimension to the nearest even number (VP8 requirement).
    /// </summary>
    public static int RoundToEven(int value) => value % 2 == 0 ? value : value - 1;

    /// <summary>
    /// Converts a BGRA32 (32-bit) pixel array to BGR24 (24-bit).
    /// </summary>
    public static byte[] ConvertBgraToBgr24(IntPtr data, int width, int height, int srcStride, out int dstStride)
    {
        dstStride = width * 3;
        var bgr = new byte[dstStride * height];
        unsafe
        {
            var src = (byte*)data;
            for (var y = 0; y < height; y++)
            {
                var srcRow = src + y * srcStride;
                var dstOffset = y * dstStride;
                for (var x = 0; x < width; x++)
                {
                    bgr[dstOffset + x * 3] = srcRow[x * 4];         // B
                    bgr[dstOffset + x * 3 + 1] = srcRow[x * 4 + 1]; // G
                    bgr[dstOffset + x * 3 + 2] = srcRow[x * 4 + 2]; // R
                }
            }
        }
        return bgr;
    }

    /// <summary>
    /// Copies an existing BGR24 pixel array into a managed byte array.
    /// </summary>
    public static byte[] CopyBgr24(IntPtr data, int width, int height, int srcStride, out int dstStride)
    {
        dstStride = width * 3;
        var result = new byte[dstStride * height];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(data + y * srcStride, result, y * dstStride, dstStride);
        }
        return result;
    }

    /// <summary>
    /// Nearest-neighbor downscale from BGR24 source to target dimensions.
    /// Fast and suitable for screen content.
    /// </summary>
    public static byte[] DownscaleBgr24(
        byte[] source, int srcW, int srcH, int srcStride,
        int dstW, int dstH, out int dstStride)
    {
        dstStride = dstW * 3;
        var result = new byte[dstStride * dstH];

        for (var y = 0; y < dstH; y++)
        {
            var srcY = y * srcH / dstH;
            var srcRowOffset = srcY * srcStride;
            var dstRowOffset = y * dstStride;

            for (var x = 0; x < dstW; x++)
            {
                var srcX = x * srcW / dstW;
                var srcPixel = srcRowOffset + srcX * 3;
                var dstPixel = dstRowOffset + x * 3;

                result[dstPixel] = source[srcPixel];         // B
                result[dstPixel + 1] = source[srcPixel + 1]; // G
                result[dstPixel + 2] = source[srcPixel + 2]; // R
            }
        }

        return result;
    }
}
