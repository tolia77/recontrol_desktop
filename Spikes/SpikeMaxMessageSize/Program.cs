// Spike D -- SDP max-message-size round-trip (RFC 8841).
//
// Question: given an SDP offer that announces a=max-message-size:262144 in
// its SCTP m-section, what value does SIPSorcery's answer announce? Does it
// honor the offered value, clamp it, or leave it absent (implied 64 KiB
// default per RFC 8841)?
//
// This spike is entirely offline: no browser peer, no ICE. We feed a
// hard-coded Chromium-style offer into a desktop RTCPeerConnection, ask
// for the answer, and dump both.

using SIPSorcery.Net;

// Chromium-style offer with an application (SCTP) m-section. Fingerprint
// and ICE credentials are placeholders; they do not need to match real
// endpoints because we never attempt DTLS handshake here -- we only read
// the SDP back after createAnswer().
const string OFFER_SDP = """
v=0
o=- 4611686018427387904 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0
a=msid-semantic: WMS
m=application 9 UDP/DTLS/SCTP webrtc-datachannel
c=IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwxyz012345
a=ice-options:trickle
a=fingerprint:sha-256 11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00
a=setup:actpass
a=mid:0
a=sctp-port:5000
a=max-message-size:262144
""";

Console.WriteLine("=== Spike D: SDP max-message-size round-trip ===");
Console.WriteLine($"SIPSorcery assembly: {typeof(RTCPeerConnection).Assembly.FullName}");
Console.WriteLine();
Console.WriteLine("--- OFFER SDP (input) ---");
Console.WriteLine(OFFER_SDP);
Console.WriteLine("--- END OFFER SDP ---");
Console.WriteLine();

var pc = new RTCPeerConnection(null);

// Must create at least one data channel on our side so SIPSorcery
// produces a valid SCTP answer m-section. (If we skip this, the m-section
// may be rejected / port 0.)
var dc = await pc.createDataChannel("probe", new RTCDataChannelInit { ordered = true });
Console.WriteLine($"Local probe channel created: label={dc.label} ordered={dc.ordered}");
Console.WriteLine();

// Parse + set the remote offer. SIPSorcery returns a SetDescriptionResultEnum;
// anything other than OK means the offer was rejected and we can't meaningfully
// read an answer.
var remoteResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
{
    type = RTCSdpType.offer,
    sdp = OFFER_SDP,
});
Console.WriteLine($"setRemoteDescription result: {remoteResult}");
if (remoteResult != SetDescriptionResultEnum.OK)
{
    Console.WriteLine("ERROR: remote offer rejected; aborting spike.");
    return;
}

var answer = pc.createAnswer(null);
await pc.setLocalDescription(answer);

Console.WriteLine();
Console.WriteLine("--- ANSWER SDP (output) ---");
Console.WriteLine(answer.sdp);
Console.WriteLine("--- END ANSWER SDP ---");
Console.WriteLine();

// Scan the answer for the max-message-size attribute and record the value.
int announcedInAnswer = -1;
bool foundInAnswer = false;
foreach (var line in (answer.sdp ?? string.Empty).Split('\n'))
{
    var trimmed = line.Trim();
    if (trimmed.StartsWith("a=max-message-size:"))
    {
        foundInAnswer = true;
        var val = trimmed.Substring("a=max-message-size:".Length);
        if (int.TryParse(val, out var parsed)) announcedInAnswer = parsed;
    }
}

Console.WriteLine("=== ANSWER max-message-size ANALYSIS ===");
if (!foundInAnswer)
{
    Console.WriteLine("a=max-message-size: ABSENT -- RFC 8841 implied default is 65536 (64 KiB).");
}
else
{
    Console.WriteLine($"a=max-message-size: {announcedInAnswer}");
    Console.WriteLine(announcedInAnswer == 262144
        ? "--> Value matches the offered 262144 (SIPSorcery honors the offer)."
        : $"--> Value differs from offered 262144 (SIPSorcery clamps or overrides).");
}
Console.WriteLine();

// Also consult the parsed SDP object directly, in case the answer text stripped
// the attribute but SIPSorcery still tracks it internally on the m-section.
Console.WriteLine("=== SDPMediaAnnouncement.MaxMessageSize (parsed) ===");
var parsedAnswer = SDP.ParseSDPDescription(answer.sdp);
foreach (var media in parsedAnswer.Media)
{
    Console.WriteLine($"m-section mediaType={media.Media} port={media.Port} MaxMessageSize={media.MaxMessageSize}");
}
Console.WriteLine();

// And for completeness, parse the offer and print what SIPSorcery saw.
Console.WriteLine("=== SDPMediaAnnouncement.MaxMessageSize (offer, parsed) ===");
var parsedOffer = SDP.ParseSDPDescription(OFFER_SDP);
foreach (var media in parsedOffer.Media)
{
    Console.WriteLine($"m-section mediaType={media.Media} port={media.Port} MaxMessageSize={media.MaxMessageSize}");
}
Console.WriteLine();

pc.close();
Console.WriteLine("Spike D complete.");
