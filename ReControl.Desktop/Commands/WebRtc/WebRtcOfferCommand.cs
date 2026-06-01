using System.Text.Json;
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
    private readonly JsonElement _permissions;

    public WebRtcOfferCommand(WebRtcService service, string sdp, JsonElement permissions = default)
    {
        _service = service;
        _sdp = sdp;
        _permissions = permissions;
    }

    public async Task<object?> ExecuteAsync()
    {
        await _service.HandleOfferAsync(_sdp, _permissions);
        return new { status = "ok" };
    }
}
