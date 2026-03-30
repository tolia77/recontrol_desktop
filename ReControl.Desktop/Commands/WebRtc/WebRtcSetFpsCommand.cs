using System.Threading.Tasks;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.WebRtc;

/// <summary>
/// Handles webrtc.set_fps command by adjusting the capture loop frame rate
/// without restarting the stream.
/// </summary>
public sealed class WebRtcSetFpsCommand : IAppCommand
{
    private readonly WebRtcService _service;
    private readonly int _fps;

    public WebRtcSetFpsCommand(WebRtcService service, int fps)
    {
        _service = service;
        _fps = fps;
    }

    public Task<object?> ExecuteAsync()
    {
        _service.SetTargetFps(_fps);
        return Task.FromResult<object?>(new { status = "ok", fps = _fps });
    }
}
