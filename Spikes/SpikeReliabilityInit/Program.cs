// Spike A -- SIPSorcery RTCDataChannelInit round-trip (GitHub issue #701).
//
// Question: does SIPSorcery 10.0.3 honor ordered / maxRetransmits /
// maxPacketLifeTime / negotiated / id on a desktop-side createDataChannel,
// or are these fields silently ignored?
//
// This spike creates three channels with non-default init options, then
// prints the properties reported back. No browser peer is required --
// the offer SDP is inspected locally to see which flags survive into
// the SCTP section.

using System.Text.Json;
using SIPSorcery.Net;

static string Fmt(object? v) => v switch
{
    null => "null",
    bool b => b ? "true" : "false",
    _ => v.ToString() ?? "null",
};

static void Dump(string label, RTCDataChannel dc)
{
    Console.WriteLine(
        $"{label,-28} label={dc.label,-28} " +
        $"ordered={Fmt(dc.ordered),-5} " +
        $"maxRetransmits={Fmt(dc.maxRetransmits),-5} " +
        $"maxPacketLifeTime={Fmt(dc.maxPacketLifeTime),-5} " +
        $"negotiated={Fmt(dc.negotiated),-5} " +
        $"id={Fmt(dc.id),-5} " +
        $"readyState={dc.readyState}");
}

Console.WriteLine("=== Spike A: RTCDataChannelInit round-trip ===");
Console.WriteLine($"SIPSorcery assembly: {typeof(RTCPeerConnection).Assembly.FullName}");
Console.WriteLine();

var pc = new RTCPeerConnection(null);

// Case 1: ordered=false + maxRetransmits=5 (unreliable, fixed retransmit budget)
var init1 = new RTCDataChannelInit
{
    ordered = false,
    maxRetransmits = 5,
};
var dc1 = await pc.createDataChannel("test-unordered-retransmits", init1);
Console.WriteLine("Requested: ordered=false, maxRetransmits=5");
Dump("Channel 1 reported:", dc1);
Console.WriteLine();

// Case 2: ordered=true (default) -- should stick and not be overridden
var init2 = new RTCDataChannelInit
{
    ordered = true,
};
var dc2 = await pc.createDataChannel("test-default-ordered", init2);
Console.WriteLine("Requested: ordered=true (explicit)");
Dump("Channel 2 reported:", dc2);
Console.WriteLine();

// Case 3: negotiated=true with caller-supplied id
var init3 = new RTCDataChannelInit
{
    negotiated = true,
    id = 42,
};
var dc3 = await pc.createDataChannel("test-negotiated", init3);
Console.WriteLine("Requested: negotiated=true, id=42");
Dump("Channel 3 reported:", dc3);
Console.WriteLine();

// Case 4: maxPacketLifeTime (mutually exclusive with maxRetransmits)
var init4 = new RTCDataChannelInit
{
    ordered = false,
    maxPacketLifeTime = 1500,
};
var dc4 = await pc.createDataChannel("test-maxpacketlifetime", init4);
Console.WriteLine("Requested: ordered=false, maxPacketLifeTime=1500ms");
Dump("Channel 4 reported:", dc4);
Console.WriteLine();

// Generate offer SDP so we can inspect what actually made it onto the wire.
var offer = pc.createOffer(null);
await pc.setLocalDescription(offer);

Console.WriteLine("=== OFFER SDP ===");
Console.WriteLine(offer.sdp);
Console.WriteLine("=== END OFFER SDP ===");
Console.WriteLine();

// Summary table in JSON so the findings doc can copy/paste verbatim.
static object Row(string requested, RTCDataChannel dc) => new
{
    label = dc.label,
    requested,
    reported = new
    {
        ordered = dc.ordered,
        maxRetransmits = dc.maxRetransmits,
        maxPacketLifeTime = dc.maxPacketLifeTime,
        negotiated = dc.negotiated,
        id = dc.id,
    },
};

var summary = new[]
{
    Row("ordered=false, maxRetransmits=5", dc1),
    Row("ordered=true", dc2),
    Row("negotiated=true, id=42", dc3),
    Row("ordered=false, maxPacketLifeTime=1500", dc4),
};

Console.WriteLine("=== SUMMARY (JSON) ===");
Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("=== END SUMMARY ===");

pc.close();
