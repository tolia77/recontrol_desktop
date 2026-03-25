using System.Text.Json;
using System.Threading.Tasks;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.WebRtc;

/// <summary>
/// Handles webrtc.quality_feedback command. Reads the available bitrate
/// from the payload and triggers adaptive quality adjustment.
/// </summary>
public class WebRtcQualityFeedbackCommand : IAppCommand
{
    private readonly WebRtcService _webRtcService;
    private readonly JsonElement _payload;

    public WebRtcQualityFeedbackCommand(WebRtcService webRtcService, JsonElement payload)
    {
        _webRtcService = webRtcService;
        _payload = payload;
    }

    public Task<object?> ExecuteAsync()
    {
        int availableBitrate = 0;
        if (_payload.TryGetProperty("availableBitrate", out var bitrateEl))
        {
            availableBitrate = bitrateEl.GetInt32();
        }

        _webRtcService.AdjustQuality(availableBitrate);
        return Task.FromResult<object?>(new { status = "ok" });
    }
}
