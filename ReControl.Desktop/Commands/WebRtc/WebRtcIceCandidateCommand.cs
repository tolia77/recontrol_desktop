using System.Threading.Tasks;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.WebRtc;

/// <summary>
/// Handles webrtc.ice_candidate command by delegating to WebRtcService.HandleIceCandidate.
/// </summary>
public sealed class WebRtcIceCandidateCommand : IAppCommand
{
    private readonly WebRtcService _service;
    private readonly string _candidate;
    private readonly string? _sdpMid;
    private readonly ushort? _sdpMLineIndex;

    public WebRtcIceCandidateCommand(WebRtcService service, string candidate, string? sdpMid, ushort? sdpMLineIndex)
    {
        _service = service;
        _candidate = candidate;
        _sdpMid = sdpMid;
        _sdpMLineIndex = sdpMLineIndex;
    }

    public Task<object?> ExecuteAsync()
    {
        _service.HandleIceCandidate(_candidate, _sdpMid, _sdpMLineIndex);
        return Task.FromResult<object?>(new { status = "ok" });
    }
}
