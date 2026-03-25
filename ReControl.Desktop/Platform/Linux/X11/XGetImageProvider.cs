using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ReControl.Desktop.Native;

namespace ReControl.Desktop.Platform.Linux.X11;

/// <summary>
/// Provides fallback screen capture capabilities using XGetImage.
/// </summary>
[SupportedOSPlatform("linux")]
public static class XGetImageProvider
{
    public static byte[] CaptureBgr24(IntPtr display, IntPtr rootWindow, int width, int height, out int stride)
    {
        var imagePtr = X11Interop.XGetImage(
            display, rootWindow,
            0, 0, (uint)width, (uint)height,
            X11Interop.AllPlanes, X11Interop.ZPixmap);

        if (imagePtr == IntPtr.Zero)
            throw new InvalidOperationException("XGetImage failed");

        try
        {
            var ximg = Marshal.PtrToStructure<X11Interop.XImage>(imagePtr);
            var srcStride = ximg.bytes_per_line;
            var bpp = ximg.bits_per_pixel;

            if (bpp == 24)
            {
                return ImageProcessingUtils.CopyBgr24(ximg.data, width, height, srcStride, out stride);
            }

            return ImageProcessingUtils.ConvertBgraToBgr24(ximg.data, width, height, srcStride, out stride);
        }
        finally
        {
            X11Interop.XDestroyImage(imagePtr);
        }
    }
}