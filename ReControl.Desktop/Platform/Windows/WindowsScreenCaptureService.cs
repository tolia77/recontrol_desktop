using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Windows screen capture using GDI (System.Drawing.Common).
/// Captures the primary monitor as raw BGR24 pixel data.
/// Uses Win32 GetSystemMetrics for screen dimensions (no WinForms dependency).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsScreenCaptureService : IScreenCaptureService
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>
    /// Round a dimension to the nearest even number (VP8 requirement).
    /// </summary>
    private static int RoundToEven(int value) => value % 2 == 0 ? value : value - 1;

    public byte[] CaptureFrame(out int width, out int height, out int stride)
    {
        width = GetSystemMetrics(SM_CXSCREEN);
        height = GetSystemMetrics(SM_CYSCREEN);

        using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(0, 0, 0, 0, new Size(width, height));
        }

        var bmpData = bmp.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            stride = Math.Abs(bmpData.Stride);
            var bytes = new byte[stride * height];
            Marshal.Copy(bmpData.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }
    }

    public byte[] CaptureFrame(int targetWidth, int targetHeight, out int stride)
    {
        targetWidth = Math.Max(2, RoundToEven(targetWidth));
        targetHeight = Math.Max(2, RoundToEven(targetHeight));

        var screenW = GetSystemMetrics(SM_CXSCREEN);
        var screenH = GetSystemMetrics(SM_CYSCREEN);

        // Capture full resolution first
        using var fullBmp = new Bitmap(screenW, screenH, PixelFormat.Format24bppRgb);
        using (var gFull = Graphics.FromImage(fullBmp))
        {
            gFull.CopyFromScreen(0, 0, 0, 0, new Size(screenW, screenH));
        }

        // Downscale to target resolution
        using var scaledBmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
        using (var gScaled = Graphics.FromImage(scaledBmp))
        {
            gScaled.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
            gScaled.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
            gScaled.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            gScaled.DrawImage(fullBmp, new Rectangle(0, 0, targetWidth, targetHeight));
        }

        var bmpData = scaledBmp.LockBits(
            new Rectangle(0, 0, targetWidth, targetHeight),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            stride = Math.Abs(bmpData.Stride);
            var bytes = new byte[stride * targetHeight];
            Marshal.Copy(bmpData.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            scaledBmp.UnlockBits(bmpData);
        }
    }

    public (int Width, int Height) GetScreenSize()
    {
        return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    public void Dispose()
    {
        // No persistent resources to dispose for GDI capture
    }
}
