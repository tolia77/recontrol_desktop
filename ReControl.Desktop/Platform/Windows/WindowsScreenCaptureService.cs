using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Windows screen capture using GDI BitBlt with a DIB section for direct pixel access.
/// Captures the primary monitor as BGRA pixel data.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsScreenCaptureService : IScreenCaptureService
{
    private readonly LogService _log;
    private readonly IntPtr _screenDc;
    private readonly IntPtr _memDc;
    private readonly IntPtr _hBitmap;
    private readonly IntPtr _oldBitmap;
    private readonly IntPtr _dibBits;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }

    public WindowsScreenCaptureService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        Width = GetSystemMetrics(SM_CXSCREEN);
        Height = GetSystemMetrics(SM_CYSCREEN);

        _screenDc = GetDC(IntPtr.Zero);
        if (_screenDc == IntPtr.Zero)
            throw new InvalidOperationException("Failed to get screen device context");

        _memDc = CreateCompatibleDC(_screenDc);
        if (_memDc == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, _screenDc);
            throw new InvalidOperationException("Failed to create compatible DC");
        }

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = Width;
        bmi.bmiHeader.biHeight = -Height; // negative = top-down DIB (matches expected scanline order)
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;

        _hBitmap = CreateDIBSection(_memDc, ref bmi, DIB_RGB_COLORS, out _dibBits, IntPtr.Zero, 0);
        if (_hBitmap == IntPtr.Zero)
        {
            DeleteDC(_memDc);
            ReleaseDC(IntPtr.Zero, _screenDc);
            throw new InvalidOperationException("Failed to create DIB section for screen capture");
        }

        _oldBitmap = SelectObject(_memDc, _hBitmap);

        _log.Info($"WindowsScreenCapture: display {Width}x{Height}");
    }

    public bool CaptureFrame(byte[] buffer)
    {
        if (_disposed) return false;

        int size = Width * Height * 4;
        if (buffer.Length < size) return false;

        if (!BitBlt(_memDc, 0, 0, Width, Height, _screenDc, 0, 0, SRCCOPY))
            return false;

        Marshal.Copy(_dibBits, buffer, 0, size);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SelectObject(_memDc, _oldBitmap);
        DeleteObject(_hBitmap);
        DeleteDC(_memDc);
        ReleaseDC(IntPtr.Zero, _screenDc);
    }

    // --- Constants ---

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    // --- Structs ---

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    // --- P/Invoke ---

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr hdc, int x, int y, int cx, int cy,
        IntPtr hdcSrc, int x1, int y1, uint rop);
}
