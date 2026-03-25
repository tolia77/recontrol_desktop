using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ReControl.Desktop.Native;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Linux;

/// <summary>
/// Linux screen capture using X11 with XShm shared memory (primary path)
/// and XGetImage fallback when XShm is unavailable.
/// Produces raw BGR24 pixel data from the BGRA X11 capture.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxScreenCaptureService : IScreenCaptureService
{
    private readonly IntPtr _display;
    private readonly int _screen;
    private readonly IntPtr _rootWindow;
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    // XShm resources (null/zero if XShm not available)
    private readonly bool _useShmPath;
    private X11Interop.XShmSegmentInfo _shmInfo;
    private IntPtr _shmImage;
    private bool _disposed;

    public LinuxScreenCaptureService()
    {
        _display = X11Interop.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("Failed to open X11 display. Ensure DISPLAY is set.");

        _screen = X11Interop.XDefaultScreen(_display);
        _rootWindow = X11Interop.XRootWindow(_display, _screen);
        _screenWidth = X11Interop.XDisplayWidth(_display, _screen);
        _screenHeight = X11Interop.XDisplayHeight(_display, _screen);

        _useShmPath = TryInitShm();
    }

    private bool TryInitShm()
    {
        try
        {
            if (X11Interop.XShmQueryExtension(_display) == 0)
                return false;

            var visual = X11Interop.XDefaultVisual(_display, _screen);
            var depth = (uint)X11Interop.XDefaultDepth(_display, _screen);

            _shmInfo = new X11Interop.XShmSegmentInfo();
            _shmImage = X11Interop.XShmCreateImage(
                _display, visual, depth,
                X11Interop.ZPixmap, IntPtr.Zero,
                ref _shmInfo,
                (uint)_screenWidth, (uint)_screenHeight);

            if (_shmImage == IntPtr.Zero)
                return false;

            // Read bytes_per_line from the XImage to calculate shared memory size
            var ximg = Marshal.PtrToStructure<X11Interop.XImage>(_shmImage);
            var shmSize = (nint)(ximg.bytes_per_line * ximg.height);

            _shmInfo.shmid = X11Interop.shmget(
                X11Interop.IPC_PRIVATE,
                shmSize,
                X11Interop.IPC_CREAT | X11Interop.SHM_R | X11Interop.SHM_W);

            if (_shmInfo.shmid < 0)
            {
                X11Interop.XDestroyImage(_shmImage);
                _shmImage = IntPtr.Zero;
                return false;
            }

            _shmInfo.shmaddr = X11Interop.shmat(_shmInfo.shmid, IntPtr.Zero, 0);
            if (_shmInfo.shmaddr == new IntPtr(-1))
            {
                X11Interop.shmctl(_shmInfo.shmid, X11Interop.IPC_RMID, IntPtr.Zero);
                X11Interop.XDestroyImage(_shmImage);
                _shmImage = IntPtr.Zero;
                return false;
            }

            _shmInfo.readOnly = 0;

            // Write shmaddr into the XImage.data field
            Marshal.WriteIntPtr(_shmImage, Marshal.OffsetOf<X11Interop.XImage>(nameof(X11Interop.XImage.data)).ToInt32(), _shmInfo.shmaddr);

            if (X11Interop.XShmAttach(_display, ref _shmInfo) == 0)
            {
                X11Interop.shmdt(_shmInfo.shmaddr);
                X11Interop.shmctl(_shmInfo.shmid, X11Interop.IPC_RMID, IntPtr.Zero);
                X11Interop.XDestroyImage(_shmImage);
                _shmImage = IntPtr.Zero;
                return false;
            }

            // Mark segment for removal after detach (cleanup even on crash)
            X11Interop.shmctl(_shmInfo.shmid, X11Interop.IPC_RMID, IntPtr.Zero);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Round a dimension to the nearest even number (VP8 requirement).
    /// </summary>
    private static int RoundToEven(int value) => value % 2 == 0 ? value : value - 1;

    public byte[] CaptureFrame(out int width, out int height, out int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        width = _screenWidth;
        height = _screenHeight;

        if (_useShmPath)
            return CaptureShmBgr24(width, height, out stride);

        return CaptureFallbackBgr24(width, height, out stride);
    }

    public byte[] CaptureFrame(int targetWidth, int targetHeight, out int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        targetWidth = Math.Max(2, RoundToEven(targetWidth));
        targetHeight = Math.Max(2, RoundToEven(targetHeight));

        // Capture at native resolution
        var nativeData = CaptureFrame(out var nativeW, out var nativeH, out var nativeStride);

        // If target matches native, return as-is
        if (targetWidth == nativeW && targetHeight == nativeH)
        {
            stride = nativeStride;
            return nativeData;
        }

        // Nearest-neighbor downscale
        return DownscaleBgr24(nativeData, nativeW, nativeH, nativeStride, targetWidth, targetHeight, out stride);
    }

    public (int Width, int Height) GetScreenSize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_screenWidth, _screenHeight);
    }

    /// <summary>
    /// Capture using XShm shared memory path (fast, zero-copy from X server).
    /// XShm captures BGRA (32-bit), which we convert to BGR24.
    /// </summary>
    private byte[] CaptureShmBgr24(int width, int height, out int stride)
    {
        X11Interop.XShmGetImage(_display, _rootWindow, _shmImage, 0, 0, X11Interop.AllPlanes);

        var ximg = Marshal.PtrToStructure<X11Interop.XImage>(_shmImage);
        var srcStride = ximg.bytes_per_line;
        var bpp = ximg.bits_per_pixel;

        if (bpp == 24)
        {
            // Already BGR24 -- direct copy
            stride = width * 3;
            var result = new byte[stride * height];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(ximg.data + y * srcStride, result, y * stride, stride);
            }
            return result;
        }

        // BGRA32 -> BGR24 conversion
        stride = width * 3;
        var bgr = new byte[stride * height];
        unsafe
        {
            var src = (byte*)ximg.data;
            for (var y = 0; y < height; y++)
            {
                var srcRow = src + y * srcStride;
                var dstOffset = y * stride;
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
    /// Capture using XGetImage fallback (slower, allocates per call).
    /// </summary>
    private byte[] CaptureFallbackBgr24(int width, int height, out int stride)
    {
        var imagePtr = X11Interop.XGetImage(
            _display, _rootWindow,
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
                stride = width * 3;
                var result = new byte[stride * height];
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(ximg.data + y * srcStride, result, y * stride, stride);
                }
                return result;
            }

            // BGRA32 -> BGR24
            stride = width * 3;
            var bgr = new byte[stride * height];
            unsafe
            {
                var src = (byte*)ximg.data;
                for (var y = 0; y < height; y++)
                {
                    var srcRow = src + y * srcStride;
                    var dstOffset = y * stride;
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
        finally
        {
            X11Interop.XDestroyImage(imagePtr);
        }
    }

    /// <summary>
    /// Nearest-neighbor downscale from BGR24 source to target dimensions.
    /// Fast and suitable for screen content.
    /// </summary>
    private static byte[] DownscaleBgr24(
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_useShmPath && _shmImage != IntPtr.Zero)
        {
            X11Interop.XShmDetach(_display, ref _shmInfo);
            X11Interop.shmdt(_shmInfo.shmaddr);
            // shmctl IPC_RMID already called in TryInitShm -- segment removed after last detach
            X11Interop.XDestroyImage(_shmImage);
            _shmImage = IntPtr.Zero;
        }

        if (_display != IntPtr.Zero)
        {
            X11Interop.XCloseDisplay(_display);
        }
    }
}
