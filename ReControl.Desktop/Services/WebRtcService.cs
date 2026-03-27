using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace ReControl.Desktop.Services;

/// <summary>
/// WebRTC peer connection lifecycle and signaling.
/// </summary>
public sealed class WebRtcService : IDisposable
{
    private readonly LogService _log;
    private readonly Func<string, Task> _sendSignal;

    private RTCPeerConnection? _pc;
    private volatile bool _disposed;
    private readonly List<RTCIceCandidateInit> _pendingCandidates = new();

    public WebRtcService(LogService log, Func<string, Task> sendSignal)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sendSignal = sendSignal ?? throw new ArgumentNullException(nameof(sendSignal));
    }

    public async Task HandleOfferAsync(string sdp)
    {
        _log.Info("WebRtcService: handling offer");
        CleanupPeerConnection();

        var config = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer>
            {
                new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
            }
        };

        _pc = new RTCPeerConnection(config);

        var vp8Format = new SDPAudioVideoMediaFormat(new VideoFormat(VideoCodecsEnum.VP8, 96));
        var videoTrack = new MediaStreamTrack(
            SDPMediaTypesEnum.video,
            false,
            new List<SDPAudioVideoMediaFormat> { vp8Format },
            MediaStreamStatusEnum.SendOnly
        );
        _pc.addTrack(videoTrack);

        _pc.onicecandidate += (candidate) =>
        {
            _log.Info("WebRtcService: local ICE candidate generated");
            var payload = new
            {
                command = "webrtc.ice_candidate",
                payload = new
                {
                    candidate = candidate.candidate,
                    sdpMid = candidate.sdpMid,
                    sdpMLineIndex = candidate.sdpMLineIndex
                }
            };
            _ = SendSignalSafe(System.Text.Json.JsonSerializer.Serialize(payload));
        };

        _pc.onconnectionstatechange += (state) =>
        {
            _log.Info($"WebRtcService: connection state -> {state}");
        };

        var remoteDesc = new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdp };
        var setResult = _pc.setRemoteDescription(remoteDesc);
        if (setResult != SetDescriptionResultEnum.OK)
        {
            _log.Error($"WebRtcService: failed to set remote description: {setResult}");
            throw new InvalidOperationException($"Failed to set remote description: {setResult}");
        }

        lock (_pendingCandidates)
        {
            foreach (var pending in _pendingCandidates)
            {
                _log.Info("WebRtcService: adding buffered ICE candidate");
                _pc.addIceCandidate(pending);
            }
            _pendingCandidates.Clear();
        }

        var answer = _pc.createAnswer();
        await _pc.setLocalDescription(answer);

        var answerPayload = new
        {
            command = "webrtc.answer",
            payload = new { sdp = answer.sdp }
        };
        await SendSignalSafe(System.Text.Json.JsonSerializer.Serialize(answerPayload));
        _log.Info("WebRtcService: answer sent");
    }

    public void HandleIceCandidate(string candidate, string? sdpMid, ushort? sdpMLineIndex)
    {
        var iceCandidate = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid ?? "0",
            sdpMLineIndex = sdpMLineIndex ?? 0
        };

        if (_pc == null)
        {
            _log.Info("WebRtcService: buffering ICE candidate (peer connection not ready)");
            lock (_pendingCandidates) { _pendingCandidates.Add(iceCandidate); }
            return;
        }

        _log.Info("WebRtcService: adding remote ICE candidate");
        _pc.addIceCandidate(iceCandidate);
    }

    public void Stop()
    {
        _log.Info("WebRtcService: stop requested");
        CleanupPeerConnection();
    }

    private void CleanupPeerConnection()
    {
        lock (_pendingCandidates) { _pendingCandidates.Clear(); }
        if (_pc != null)
        {
            _pc.close();
            _pc.Dispose();
            _pc = null;
        }
    }

    private async Task SendSignalSafe(string message)
    {
        try { await _sendSignal(message); }
        catch (Exception ex) { _log.Error("WebRtcService: failed to send signaling message", ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _log.Info("WebRtcService: disposing");
        CleanupPeerConnection();
    }
}
