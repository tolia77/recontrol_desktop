using System;
using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.WebRtc;

/// <summary>
/// Handles webrtc.set_resolution command. Parses a preset string
/// ("native", "1080p", "720p", "480p") and updates the capture resolution.
/// </summary>
public class WebRtcSetResolutionCommand : IAppCommand
{
    private readonly WebRtcService _webRtcService;
    private readonly string _presetStr;

    public WebRtcSetResolutionCommand(WebRtcService webRtcService, string presetStr)
    {
        _webRtcService = webRtcService;
        _presetStr = presetStr;
    }

    public Task<object?> ExecuteAsync()
    {
        var preset = ParsePreset(_presetStr);
        _webRtcService.SetResolution(preset);
        return Task.FromResult<object?>(new { status = "ok", preset = _presetStr });
    }

    private static ResolutionPreset ParsePreset(string value)
    {
        return value?.ToLowerInvariant() switch
        {
            "native" => ResolutionPreset.Native,
            "1080p" => ResolutionPreset.P1080,
            "720p" => ResolutionPreset.P720,
            "480p" => ResolutionPreset.P480,
            _ => ResolutionPreset.Native
        };
    }
}
