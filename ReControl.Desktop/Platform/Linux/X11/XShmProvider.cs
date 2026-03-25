using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ReControl.Desktop.Native;

namespace ReControl.Desktop.Platform.Linux.X11;

/// <summary>
/// Provides shared memory capture capabilities using XShm.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class XShmProvider : IDisposable
{
    private readonly IntPtr _display;
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private X11Interop.XShmSegmentInfo _shmInfo;
    private IntPtr _shmImage;
    private bool _initialized;
    private bool _disposed;

    public bool IsInitialized => _initialized;

    public XShmProvider(IntPtr display, int screen, int width, int height)
    {
        _display = display;
        _screenWidth = width;
        _screenHeight = height;

        _initialized = TryInitShm(screen);
    }

    private bool TryInitShm(int screen)
    {
        try
        {
            if (X11Interop.XShmQueryExtension(_display) == 0)
                return false;

            var visual = X11Interop.XDefaultVisual(_display, screen);
            var depth = (uint)X11Interop.XDefaultDepth(_display, screen);

            _shmInfo = new X11Interop.XShmSegmentInfo();
            _shmImage = X11Interop.XShmCreateImage(
                _display, visual, depth,
                X11Interop.ZPixmap, IntPtr.Zero,
                ref _shmInfo,
                (uint)_screenWidth, (uint)_screenHeight);

            if (_shmImage == IntPtr.Zero)
                return false;

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

            Marshal.WriteIntPtr(_shmImage, Marshal.OffsetOf<X11Interop.XImage>(nameof(X11Interop.XImage.data)).ToInt32(), _shmInfo.shmaddr);

            if (X11Interop.XShmAttach(_display, ref _shmInfo) == 0)
            {
                X11Interop.shmdt(_shmInfo.shmaddr);
                X11Interop.shmctl(_shmInfo.shmid, X11Interop.IPC_RMID, IntPtr.Zero);
                X11Interop.XDestroyImage(_shmImage);
                _shmImage = IntPtr.Zero;
                return false;
            }

            // Mark for auto cleanup even if crash
            X11Interop.shmctl(_shmInfo.shmid, X11Interop.IPC_RMID, IntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public byte[] CaptureBgr24(IntPtr rootWindow, out int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            throw new InvalidOperationException("XShm not initialized");

        X11Interop.XShmGetImage(_display, rootWindow, _shmImage, 0, 0, X11Interop.AllPlanes);

        var ximg = Marshal.PtrToStructure<X11Interop.XImage>(_shmImage);
        var srcStride = ximg.bytes_per_line;
        var bpp = ximg.bits_per_pixel;

        if (bpp == 24)
        {
            return ImageProcessingUtils.CopyBgr24(ximg.data, _screenWidth, _screenHeight, srcStride, out stride);
        }

        return ImageProcessingUtils.ConvertBgraToBgr24(ximg.data, _screenWidth, _screenHeight, srcStride, out stride);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_initialized && _shmImage != IntPtr.Zero)
        {
            X11Interop.XShmDetach(_display, ref _shmInfo);
            X11Interop.shmdt(_shmInfo.shmaddr);
            X11Interop.XDestroyImage(_shmImage);
            _shmImage = IntPtr.Zero;
            _initialized = false;
        }
    }
}