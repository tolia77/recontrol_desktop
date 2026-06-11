using System.Threading.Tasks;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.WebRtc;

/// <summary>
/// Handles webrtc.request_keyframe command by forcing an IDR keyframe from the
/// active FFmpeg encoder, resolving orientation-change black frames on the viewer side.
/// </summary>
public sealed class WebRtcRequestKeyframeCommand : IAppCommand
{
    private readonly WebRtcService _service;

    public WebRtcRequestKeyframeCommand(WebRtcService service)
        => _service = service;

    public Task<object?> ExecuteAsync()
    {
        _service.RequestKeyframe();
        return Task.FromResult<object?>(new { status = "ok" });
    }
}
