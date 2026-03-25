using System;
using System.Runtime.Versioning;
using ReControl.Desktop.Native;
using ReControl.Desktop.Platform.Linux.X11;
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

    private readonly XShmProvider _shmProvider;
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

        _shmProvider = new XShmProvider(_display, _screen, _screenWidth, _screenHeight);
    }

    public byte[] CaptureFrame(out int width, out int height, out int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        width = _screenWidth;
        height = _screenHeight;

        if (_shmProvider.IsInitialized)
        {
            return _shmProvider.CaptureBgr24(_rootWindow, out stride);
        }

        return XGetImageProvider.CaptureBgr24(_display, _rootWindow, width, height, out stride);
    }

    public byte[] CaptureFrame(int targetWidth, int targetHeight, out int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        targetWidth = Math.Max(2, ImageProcessingUtils.RoundToEven(targetWidth));
        targetHeight = Math.Max(2, ImageProcessingUtils.RoundToEven(targetHeight));

        var nativeData = CaptureFrame(out var nativeW, out var nativeH, out var nativeStride);

        if (targetWidth == nativeW && targetHeight == nativeH)
        {
            stride = nativeStride;
            return nativeData;
        }

        return ImageProcessingUtils.DownscaleBgr24(nativeData, nativeW, nativeH, nativeStride, targetWidth, targetHeight, out stride);
    }

    public (int Width, int Height) GetScreenSize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_screenWidth, _screenHeight);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _shmProvider?.Dispose();

        if (_display != IntPtr.Zero)
        {
            X11Interop.XCloseDisplay(_display);
        }
    }
}
