using System.Threading.Tasks;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.WebRtc;

/// <summary>
/// Handles webrtc.stop command by tearing down the WebRTC peer connection.
/// </summary>
public sealed class WebRtcStopCommand : IAppCommand
{
    private readonly WebRtcService _service;

    public WebRtcStopCommand(WebRtcService service)
    {
        _service = service;
    }

    public Task<object?> ExecuteAsync()
    {
        _service.Stop();
        return Task.FromResult<object?>(new { status = "ok" });
    }
}
