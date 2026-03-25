using System;

namespace ReControl.Desktop.Models;

/// <summary>
/// Resolution presets for screen capture downscaling.
/// Native uses the monitor's native resolution; others cap to standard sizes.
/// </summary>
public enum ResolutionPreset { Native, P1080, P720, P480 }

/// <summary>
/// Utility methods for computing target capture dimensions from resolution presets.
/// Maintains aspect ratio and ensures even dimensions (VP8 requirement).
/// </summary>
public static class ResolutionPresets
{
    /// <summary>
    /// Get the maximum dimensions for a preset, or null for Native (use screen size).
    /// </summary>
    public static (int Width, int Height)? GetDimensions(ResolutionPreset preset)
    {
        return preset switch
        {
            ResolutionPreset.Native => null,
            ResolutionPreset.P1080 => (1920, 1080),
            ResolutionPreset.P720 => (1280, 720),
            ResolutionPreset.P480 => (854, 480),
            _ => null
        };
    }

    /// <summary>
    /// Compute target capture dimensions maintaining aspect ratio.
    /// Fits within preset bounds and rounds to even numbers for VP8 compatibility.
    /// </summary>
    public static (int Width, int Height) ComputeTargetSize(
        ResolutionPreset preset, int nativeWidth, int nativeHeight)
    {
        var dims = GetDimensions(preset);
        if (dims == null) return (EvenRound(nativeWidth), EvenRound(nativeHeight));

        var (maxW, maxH) = dims.Value;
        double scaleW = (double)maxW / nativeWidth;
        double scaleH = (double)maxH / nativeHeight;
        double scale = Math.Min(scaleW, scaleH);

        // If native is already smaller than preset, use native
        if (scale >= 1.0) return (EvenRound(nativeWidth), EvenRound(nativeHeight));

        int w = EvenRound((int)(nativeWidth * scale));
        int h = EvenRound((int)(nativeHeight * scale));
        return (Math.Max(2, w), Math.Max(2, h));
    }

    /// <summary>
    /// Round up to the nearest even number. VP8 requires even dimensions.
    /// </summary>
    private static int EvenRound(int v) => v % 2 == 0 ? v : v + 1;
}
