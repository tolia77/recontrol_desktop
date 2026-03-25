using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;
using ReControl.Desktop.Services.WebRtc;

namespace ReControl.Desktop.Services;

/// <summary>
/// WebRTC peer connection lifecycle, VP8 encoding, and screen capture feed loop.
/// </summary>
public sealed class WebRtcService : IDisposable
{
    private readonly LogService _log;
    private readonly Func<string, Task> _sendSignal;
    private readonly MediaStreamManager _mediaStreamManager;

    private RTCPeerConnection? _pc;
    private volatile bool _disposed;

    public WebRtcService(IScreenCaptureService screenCapture, LogService log, Func<string, Task> sendSignal)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sendSignal = sendSignal ?? throw new ArgumentNullException(nameof(sendSignal));
        _mediaStreamManager = new MediaStreamManager(screenCapture, log);
    }

    /// <summary>
    /// Handle an incoming WebRTC offer from the browser.
    /// Creates a peer connection, adds VP8 video track, sets remote description,
    /// creates and sends an answer, and wires ICE and connection state handlers.
    /// </summary>
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

        // Add VP8 video track (SendOnly)
        var vp8Format = new SDPAudioVideoMediaFormat(new VideoFormat(VideoCodecsEnum.VP8, 96));
        var videoTrack = new MediaStreamTrack(
            SDPMediaTypesEnum.video,
            false,
            new List<SDPAudioVideoMediaFormat> { vp8Format },
            MediaStreamStatusEnum.SendOnly
        );
        _pc.addTrack(videoTrack);

        // ICE candidate handler -- send candidates to remote peer via ActionCable
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

        // Connection state handler
        _pc.onconnectionstatechange += (state) =>
        {
            _log.Info($"WebRtcService: connection state -> {state}");
            switch (state)
            {
                case RTCPeerConnectionState.connected:
                    _mediaStreamManager.StartScreenFeed(_pc);
                    break;
                case RTCPeerConnectionState.failed:
                case RTCPeerConnectionState.closed:
                case RTCPeerConnectionState.disconnected:
                    _mediaStreamManager.StopScreenFeed();
                    break;
            }
        };

        // Set remote description (the offer)
        var remoteDesc = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = sdp
        };
        var setResult = _pc.setRemoteDescription(remoteDesc);
        if (setResult != SetDescriptionResultEnum.OK)
        {
            _log.Error($"WebRtcService: failed to set remote description: {setResult}");
            throw new InvalidOperationException($"Failed to set remote description: {setResult}");
        }

        // Create answer
        var answer = _pc.createAnswer();
        await _pc.setLocalDescription(answer);

        // Send answer back via signaling
        var answerPayload = new
        {
            command = "webrtc.answer",
            payload = new
            {
                sdp = answer.sdp
            }
        };
        await SendSignalSafe(System.Text.Json.JsonSerializer.Serialize(answerPayload));

        _log.Info("WebRtcService: answer sent");
    }

    /// <summary>
    /// Add an ICE candidate received from the remote peer.
    /// </summary>
    public void HandleIceCandidate(string candidate, string? sdpMid, ushort? sdpMLineIndex)
    {
        if (_pc == null)
        {
            _log.Warning("WebRtcService: HandleIceCandidate called with no peer connection");
            return;
        }

        _log.Info("WebRtcService: adding remote ICE candidate");
        var iceCandidate = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid ?? "0",
            sdpMLineIndex = sdpMLineIndex ?? 0
        };
        _pc.addIceCandidate(iceCandidate);
    }

    /// <summary>
    /// Stop the WebRTC session and tear down the peer connection.
    /// </summary>
    public void Stop()
    {
        _log.Info("WebRtcService: stop requested");
        CleanupPeerConnection();
    }

    /// <summary>
    /// Change the resolution preset. Forces a keyframe on the next frame and clears
    /// the previous frame buffer so the full frame is encoded at the new dimensions.
    /// Sets the manual override flag to prevent adaptive quality from changing it.
    /// </summary>
    public void SetResolution(ResolutionPreset preset)
    {
        _mediaStreamManager.SetResolution(preset);
    }

    /// <summary>
    /// Adjust resolution preset based on available bandwidth. Auto-downgrades when
    /// bandwidth is insufficient, auto-upgrades when bandwidth improves.
    /// Skips adjustment if the user manually set a preset.
    /// </summary>
    public void AdjustQuality(int availableBitrateKbps)
    {
        if (_mediaStreamManager.ManualPresetOverride)
        {
            _log.Info($"WebRtcService: adaptive quality skipped (manual override active), bitrate={availableBitrateKbps}kbps");
            return;
        }

        var previous = _mediaStreamManager.CurrentPreset;

        if (availableBitrateKbps < 500 && _mediaStreamManager.CurrentPreset != ResolutionPreset.P480)
        {
            _mediaStreamManager.SetResolutionInternal(ResolutionPreset.P480);
        }
        else if (availableBitrateKbps < 1500 && _mediaStreamManager.CurrentPreset < ResolutionPreset.P720)
        {
            // Current preset is Native or P1080, downgrade to 720p
            _mediaStreamManager.SetResolutionInternal(ResolutionPreset.P720);
        }
        else if (availableBitrateKbps > 3000 && _mediaStreamManager.CurrentPreset > ResolutionPreset.Native)
        {
            // Upgrade one step: P480 -> P720 -> P1080 -> Native
            var upgraded = _mediaStreamManager.CurrentPreset switch
            {
                ResolutionPreset.P480 => ResolutionPreset.P720,
                ResolutionPreset.P720 => ResolutionPreset.P1080,
                ResolutionPreset.P1080 => ResolutionPreset.Native,
                _ => _mediaStreamManager.CurrentPreset
            };
            _mediaStreamManager.SetResolutionInternal(upgraded);
        }

        if (_mediaStreamManager.CurrentPreset != previous)
        {
            _log.Info($"WebRtcService: adaptive quality {previous} -> {_mediaStreamManager.CurrentPreset} (bitrate={availableBitrateKbps}kbps)");
        }
    }

    /// <summary>
    /// Tear down the peer connection and stop the screen feed.
    /// </summary>
    private void CleanupPeerConnection()
    {
        _mediaStreamManager.StopScreenFeed();

        if (_pc != null)
        {
            _pc.close();
            _pc.Dispose();
            _pc = null;
        }
    }

    /// <summary>
    /// Send a signaling message through ActionCable, catching and logging errors.
    /// </summary>
    private async Task SendSignalSafe(string message)
    {
        try
        {
            await _sendSignal(message);
        }
        catch (Exception ex)
        {
            _log.Error("WebRtcService: failed to send signaling message", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _log.Info("WebRtcService: disposing");
        CleanupPeerConnection();
    }
}

