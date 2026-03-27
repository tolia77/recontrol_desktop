using System;

namespace ReControl.Desktop.Services.Interfaces;

/// <summary>
/// Platform-agnostic screen capture interface.
/// Returns raw BGRA pixel data for the primary display.
/// </summary>
public interface IScreenCaptureService : IDisposable
{
    /// <summary>Screen width in pixels.</summary>
    int Width { get; }

    /// <summary>Screen height in pixels.</summary>
    int Height { get; }

    /// <summary>
    /// Captures the current screen contents into the provided buffer.
    /// Buffer must be at least Width * Height * 4 bytes.
    /// Returns true if capture succeeded.
    /// </summary>
    bool CaptureFrame(byte[] buffer);
}
