using System.Threading.Tasks;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.WebRtc;

/// <summary>
/// Handles webrtc.offer command by delegating to WebRtcService.HandleOfferAsync.
/// </summary>
public sealed class WebRtcOfferCommand : IAppCommand
{
    private readonly WebRtcService _service;
    private readonly string _sdp;

    public WebRtcOfferCommand(WebRtcService service, string sdp)
    {
        _service = service;
        _sdp = sdp;
    }

    public async Task<object?> ExecuteAsync()
    {
        await _service.HandleOfferAsync(_sdp);
        return new { status = "ok" };
    }
}
