namespace ReControl.Desktop.Services.Interfaces;

/// <summary>
/// Screen capture abstraction for platform-specific implementations.
/// Returns raw BGR24 pixel data suitable for VP8 encoding.
/// </summary>
public interface IScreenCaptureService : IDisposable
{
    /// <summary>
    /// Capture primary screen as raw BGR24 pixel data at native resolution.
    /// </summary>
    /// <param name="width">Output image width in pixels.</param>
    /// <param name="height">Output image height in pixels.</param>
    /// <param name="stride">Output row stride in bytes (width * 3, may include padding).</param>
    /// <returns>Raw BGR24 pixel data.</returns>
    byte[] CaptureFrame(out int width, out int height, out int stride);

    /// <summary>
    /// Capture primary screen downscaled to target resolution.
    /// targetWidth/targetHeight are rounded to even numbers internally (VP8 requirement).
    /// </summary>
    /// <param name="targetWidth">Desired output width (will be rounded to even number).</param>
    /// <param name="targetHeight">Desired output height (will be rounded to even number).</param>
    /// <param name="stride">Output row stride in bytes.</param>
    /// <returns>Raw BGR24 pixel data at the target resolution.</returns>
    byte[] CaptureFrame(int targetWidth, int targetHeight, out int stride);

    /// <summary>
    /// Get the native screen dimensions (primary monitor).
    /// </summary>
    (int Width, int Height) GetScreenSize();
}
