// Spike C -- RTCDataChannel close() state transitions (GitHub issue #882).
//
// Question: when the browser peer calls dc.close(), does the desktop
// observe readyState transition from open -> closing -> closed, or does
// it hang in 'closing' forever? Does onclose fire?
//
// Outcome drives tear-down policy in Phase 11 / Phase 9 resource cleanup:
//   - If dc.close() works --> per-channel close is safe.
//   - If it hangs --> tear down by closing the pc (RTCPeerConnection.close)
//     and never call dc.close() per-channel.
//
// Run: `dotnet run --project recontrol_desktop/Spikes/SpikeDcClose`
// Open spike-c-frontend.html in a Chromium-based browser.

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;

// See Spike B for why we bind both IPv4 and IPv6 loopback.
var listenPrefixes = new[]
{
    "http://127.0.0.1:8787/",
    "http://localhost:8787/",
};
const int PollIntervalMs = 500;
const int PollDurationMs = 15_000;

Console.WriteLine("=== Spike C: dc.close() state transitions ===");
Console.WriteLine($"SIPSorcery assembly: {typeof(RTCPeerConnection).Assembly.FullName}");
Console.WriteLine();

var http = new HttpListener();
foreach (var p in listenPrefixes) http.Prefixes.Add(p);
http.Start();
Console.WriteLine($"Signaling server listening on: {string.Join(", ", listenPrefixes)}");
Console.WriteLine("Open spike-c-frontend.html in a browser.");
Console.WriteLine();

var stateSamples = new List<(long tMs, string state)>();
bool oncloseFired = false;
long? oncloseAtMs = null;
var done = new TaskCompletionSource<bool>();

while (true)
{
    var ctx = await http.GetContextAsync();
    AddCors(ctx.Response);

    if (ctx.Request.HttpMethod == "OPTIONS")
    {
        ctx.Response.StatusCode = 204;
        ctx.Response.Close();
        continue;
    }

    if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url!.AbsolutePath == "/offer")
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var offerSdp = await reader.ReadToEndAsync();
        Console.WriteLine("Received offer from browser.");

        var pc = new RTCPeerConnection(null);

        pc.ondatachannel += channel =>
        {
            Console.WriteLine($"ondatachannel: label={channel.label} id={channel.id} initial readyState={channel.readyState}");
            if (channel.label != "spike-c") return;

            var sw = Stopwatch.StartNew();

            channel.onopen += () =>
            {
                Console.WriteLine($"[dc] onopen event t={sw.ElapsedMilliseconds}ms readyState={channel.readyState}");
            };
            channel.onclose += () =>
            {
                oncloseFired = true;
                oncloseAtMs = sw.ElapsedMilliseconds;
                Console.WriteLine($"[dc] onclose event t={oncloseAtMs}ms readyState={channel.readyState}");
            };
            channel.onmessage += (_, _, _) => { /* unused */ };

            // Start polling immediately from ondatachannel, regardless of whether
            // the onopen callback fires. This is how Spike B observed state
            // reliably even though the ondatachannel timing is subtle in SIPSorcery.
            _ = Task.Run(() => PollState(channel, sw));
        };

        var setResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = offerSdp,
        });
        Console.WriteLine($"setRemoteDescription: {setResult}");

        var answer = pc.createAnswer(null);
        await pc.setLocalDescription(answer);
        await WaitForIceGatheringComplete(pc);

        var answerBytes = Encoding.UTF8.GetBytes(pc.localDescription!.sdp.ToString());
        ctx.Response.ContentType = "application/sdp";
        ctx.Response.ContentLength64 = answerBytes.Length;
        await ctx.Response.OutputStream.WriteAsync(answerBytes);
        ctx.Response.Close();
        Console.WriteLine("Answer sent back.");

        await Task.WhenAny(done.Task, Task.Delay(PollDurationMs + 5000));
        PrintReport();
        return;
    }
    else
    {
        ctx.Response.StatusCode = 404;
        ctx.Response.Close();
    }
}

void AddCors(HttpListenerResponse res)
{
    res.AddHeader("Access-Control-Allow-Origin", "*");
    res.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
    res.AddHeader("Access-Control-Allow-Headers", "Content-Type");
}

static async Task WaitForIceGatheringComplete(RTCPeerConnection pc)
{
    var tcs = new TaskCompletionSource<bool>();
    void Check(RTCIceGatheringState state)
    {
        if (state == RTCIceGatheringState.complete) tcs.TrySetResult(true);
    }
    pc.onicegatheringstatechange += Check;
    if (pc.iceGatheringState == RTCIceGatheringState.complete) tcs.TrySetResult(true);
    await Task.WhenAny(tcs.Task, Task.Delay(3000));
    pc.onicegatheringstatechange -= Check;
}

void PollState(RTCDataChannel dc, Stopwatch sw)
{
    var deadline = sw.ElapsedMilliseconds + PollDurationMs;
    string lastLogged = "";
    while (sw.ElapsedMilliseconds < deadline)
    {
        var state = dc.readyState.ToString();
        stateSamples.Add((sw.ElapsedMilliseconds, state));
        if (state != lastLogged)
        {
            Console.WriteLine($"[poll] t={sw.ElapsedMilliseconds}ms readyState={state}");
            lastLogged = state;
        }
        if (state == "closed")
        {
            done.TrySetResult(true);
            return;
        }
        Thread.Sleep(PollIntervalMs);
    }
    Console.WriteLine($"[poll] gave up at t={sw.ElapsedMilliseconds}ms, final state={dc.readyState}");
    done.TrySetResult(true);
}

void PrintReport()
{
    Console.WriteLine();
    Console.WriteLine("=== STATE TIMELINE ===");
    string last = "";
    foreach (var (t, s) in stateSamples)
    {
        if (s != last)
        {
            Console.WriteLine($"t={t,6}ms state={s}");
            last = s;
        }
    }
    Console.WriteLine();
    Console.WriteLine($"onclose fired: {oncloseFired}" + (oncloseAtMs is long ms ? $" (t={ms}ms)" : ""));
    Console.WriteLine();
    Console.WriteLine("=== SUMMARY (JSON) ===");
    var finalState = stateSamples.Count > 0 ? stateSamples[^1].state : "never-opened";
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        oncloseFired,
        oncloseAtMs,
        finalReadyState = finalState,
        sampleCount = stateSamples.Count,
    }, new JsonSerializerOptions { WriteIndented = true }));
}
