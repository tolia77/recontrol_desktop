using System.Threading.Tasks;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.WebRtc;

/// <summary>
/// Handles webrtc.set_resolution command by adjusting the target resolution
/// and restarting the encoder at the new dimensions.
/// </summary>
public sealed class WebRtcSetResolutionCommand : IAppCommand
{
    private readonly WebRtcService _service;
    private readonly int _resolution;

    public WebRtcSetResolutionCommand(WebRtcService service, int resolution)
    {
        _service = service;
        _resolution = resolution;
    }

    public Task<object?> ExecuteAsync()
    {
        _service.SetTargetResolution(_resolution);
        return Task.FromResult<object?>(new { status = "ok", resolution = _resolution });
    }
}
