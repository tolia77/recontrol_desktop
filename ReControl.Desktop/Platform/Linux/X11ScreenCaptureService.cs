using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ReControl.Desktop.Native;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Linux;

[SupportedOSPlatform("linux")]
internal sealed class X11ScreenCaptureService : IScreenCaptureService
{
    private readonly LogService _log;
    private readonly IntPtr _display;
    private readonly int _screen;
    private readonly IntPtr _rootWindow;

    private bool _useShmCapture;
    private IntPtr _shmImage;
    private X11Interop.XShmSegmentInfo _shmInfo;
    private int _bufferSize;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }

    public X11ScreenCaptureService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        X11Interop.XInitThreads();
        _display = X11Interop.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException(
                "Failed to open X11 display for screen capture. Ensure DISPLAY is set.");

        _screen = X11Interop.XDefaultScreen(_display);
        _rootWindow = X11Interop.XRootWindow(_display, _screen);

        Width = X11Interop.XDisplayWidth(_display, _screen);
        Height = X11Interop.XDisplayHeight(_display, _screen);
        _bufferSize = Width * Height * 4;

        _log.Info($"X11ScreenCapture: display {Width}x{Height}");

        InitShm();
    }

    private void InitShm()
    {
        if (X11Interop.XShmQueryExtension(_display) == 0)
        {
            _log.Warning("X11ScreenCapture: XShm not available, using XGetImage fallback");
            _useShmCapture = false;
            return;
        }

        var visual = X11Interop.XDefaultVisual(_display, _screen);
        var depth = (uint)X11Interop.XDefaultDepth(_display, _screen);

        _shmInfo = new X11Interop.XShmSegmentInfo();
        _shmImage = X11Interop.XShmCreateImage(
            _display, visual, depth,
            X11Interop.ZPixmap, IntPtr.Zero,
            ref _shmInfo, (uint)Width, (uint)Height);

        if (_shmImage == IntPtr.Zero)
        {
            _log.Warning("X11ScreenCapture: XShmCreateImage failed, using fallback");
            _useShmCapture = false;
            return;
        }

        var ximg = Marshal.PtrToStructure<X11Interop.XImage>(_shmImage);
        _bufferSize = ximg.bytes_per_line * ximg.height;

        _shmInfo.shmid = X11Interop.shmget(
            X11Interop.IPC_PRIVATE, (nint)_bufferSize,
            X11Interop.IPC_CREAT | X11Interop.SHM_R | X11Interop.SHM_W);

        if (_shmInfo.shmid < 0)
        {
            _log.Warning("X11ScreenCapture: shmget failed, using fallback");
            X11Interop.XDestroyImage(_shmImage);
            _shmImage = IntPtr.Zero;
            _useShmCapture = false;
            return;
        }

        _shmInfo.shmaddr = X11Interop.shmat(_shmInfo.shmid, IntPtr.Zero, 0);
        if (_shmInfo.shmaddr == IntPtr.Zero || _shmInfo.shmaddr == new IntPtr(-1))
        {
            _log.Warning("X11ScreenCapture: shmat failed, using fallback");
            X11Interop.shmctl(_shmInfo.shmid, X11Interop.IPC_RMID, IntPtr.Zero);
            X11Interop.XDestroyImage(_shmImage);
            _shmImage = IntPtr.Zero;
            _useShmCapture = false;
            return;
        }

        _shmInfo.readOnly = 0;

        Marshal.WriteIntPtr(_shmImage,
            Marshal.OffsetOf<X11Interop.XImage>(nameof(X11Interop.XImage.data)).ToInt32(),
            _shmInfo.shmaddr);

        if (X11Interop.XShmAttach(_display, ref _shmInfo) == 0)
        {
            _log.Warning("X11ScreenCapture: XShmAttach failed, using fallback");
            X11Interop.shmdt(_shmInfo.shmaddr);
            X11Interop.shmctl(_shmInfo.shmid, X11Interop.IPC_RMID, IntPtr.Zero);
            X11Interop.XDestroyImage(_shmImage);
            _shmImage = IntPtr.Zero;
            _useShmCapture = false;
            return;
        }

        _useShmCapture = true;
        _log.Info("X11ScreenCapture: XShm initialized successfully");
    }

    public bool CaptureFrame(byte[] buffer)
    {
        if (_disposed) return false;
        if (buffer.Length < _bufferSize) return false;

        bool captured;
        if (_useShmCapture)
        {
            captured = CaptureShmFrame(buffer);
            if (!captured)
                captured = CaptureSlowFrame(buffer);
        }
        else
        {
            captured = CaptureSlowFrame(buffer);
        }

        if (captured)
            CompositeCursor(buffer);

        return captured;
    }

    private bool CaptureShmFrame(byte[] buffer)
    {
        var result = X11Interop.XShmGetImage(
            _display, _rootWindow, _shmImage,
            0, 0, X11Interop.AllPlanes);

        if (result == 0) return false;

        Marshal.Copy(_shmInfo.shmaddr, buffer, 0, _bufferSize);
        return true;
    }

    private bool CaptureSlowFrame(byte[] buffer)
    {
        var image = X11Interop.XGetImage(
            _display, _rootWindow,
            0, 0, (uint)Width, (uint)Height,
            X11Interop.AllPlanes, X11Interop.ZPixmap);

        if (image == IntPtr.Zero) return false;

        try
        {
            var ximg = Marshal.PtrToStructure<X11Interop.XImage>(image);
            var size = ximg.bytes_per_line * ximg.height;
            if (buffer.Length < size) return false;
            Marshal.Copy(ximg.data, buffer, 0, size);
            return true;
        }
        finally
        {
            X11Interop.XDestroyImage(image);
        }
    }

    /// <summary>
    /// Composites the X11 cursor onto the capture buffer using XFixes.
    /// XShmGetImage/XGetImage don't include the cursor overlay, so we
    /// fetch it separately and alpha-blend it onto the captured frame.
    /// </summary>
    private unsafe void CompositeCursor(byte[] buffer)
    {
        var cursorPtr = IntPtr.Zero;
        try
        {
            cursorPtr = X11Interop.XFixesGetCursorImage(_display);
            if (cursorPtr == IntPtr.Zero) return;

            // XFixesCursorImage layout (LP64): x(2) y(2) width(2) height(2) xhot(2) yhot(2) pad(4) cursor_serial(8) pixels(8)
            short cx = Marshal.ReadInt16(cursorPtr, 0);
            short cy = Marshal.ReadInt16(cursorPtr, 2);
            ushort cw = (ushort)Marshal.ReadInt16(cursorPtr, 4);
            ushort ch = (ushort)Marshal.ReadInt16(cursorPtr, 6);
            ushort xhot = (ushort)Marshal.ReadInt16(cursorPtr, 8);
            ushort yhot = (ushort)Marshal.ReadInt16(cursorPtr, 10);
            // cursor_serial at offset 16 (8 bytes), pixels pointer at offset 24 (8 bytes)
            var pixelsPtr = Marshal.ReadIntPtr(cursorPtr, 24);
            if (pixelsPtr == IntPtr.Zero || cw == 0 || ch == 0) return;

            int stride = _bufferSize / Height;

            fixed (byte* buf = buffer)
            {
                long* pixels = (long*)pixelsPtr;

                for (int py = 0; py < ch; py++)
                {
                    int sy = cy - yhot + py;
                    if (sy < 0 || sy >= Height) continue;

                    for (int px = 0; px < cw; px++)
                    {
                        int sx = cx - xhot + px;
                        if (sx < 0 || sx >= Width) continue;

                        // XFixes pixels are unsigned long (8 bytes on LP64), ARGB in lower 32 bits, premultiplied alpha
                        long pixel = pixels[py * cw + px];
                        byte a = (byte)((pixel >> 24) & 0xFF);
                        if (a == 0) continue;

                        byte r = (byte)((pixel >> 16) & 0xFF);
                        byte g = (byte)((pixel >> 8) & 0xFF);
                        byte b = (byte)(pixel & 0xFF);

                        int off = sy * stride + sx * 4;

                        if (a == 255)
                        {
                            buf[off] = b;
                            buf[off + 1] = g;
                            buf[off + 2] = r;
                            buf[off + 3] = 255;
                        }
                        else
                        {
                            // Premultiplied alpha blend: result = src + dst * (1 - srcAlpha)
                            int invA = 255 - a;
                            buf[off] = (byte)Math.Min(b + buf[off] * invA / 255, 255);
                            buf[off + 1] = (byte)Math.Min(g + buf[off + 1] * invA / 255, 255);
                            buf[off + 2] = (byte)Math.Min(r + buf[off + 2] * invA / 255, 255);
                            buf[off + 3] = 255;
                        }
                    }
                }
            }
        }
        catch
        {
            // XFixes may not be available — silently degrade to no cursor
        }
        finally
        {
            if (cursorPtr != IntPtr.Zero)
                X11Interop.XFree(cursorPtr);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_useShmCapture && _shmImage != IntPtr.Zero)
        {
            X11Interop.XShmDetach(_display, ref _shmInfo);
            X11Interop.shmdt(_shmInfo.shmaddr);
            X11Interop.shmctl(_shmInfo.shmid, X11Interop.IPC_RMID, IntPtr.Zero);
            X11Interop.XDestroyImage(_shmImage);
            _shmImage = IntPtr.Zero;
        }

        if (_display != IntPtr.Zero)
        {
            X11Interop.XCloseDisplay(_display);
        }
    }
}
