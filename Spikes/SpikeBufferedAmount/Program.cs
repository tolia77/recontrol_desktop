// Spike B -- RTCDataChannel.bufferedAmount during heavy send (GitHub issue #383).
//
// Question: does the desktop-side RTCDataChannel.bufferedAmount property
// rise during a 10 MB send loop and drain back toward 0 after sending
// stops, or is it always reported as 0?
//
// Outcome drives Phase 11 backpressure strategy:
//   - If bufferedAmount is reliable --> poll it in the send loop and pause
//     above a threshold (Recommendation A in 09-SPIKE-FINDINGS.md).
//   - If it is always 0 --> app-level windowing via files-ctl acks
//     (Recommendation B).
//
// Run: `dotnet run --project recontrol_desktop/Spikes/SpikeBufferedAmount`
// Then open spike-b-frontend.html in a Chromium-based browser.
// (The HTML page ships ICE candidates in the offer via
// waitForIceGatheringComplete so signaling is a single HTTP round trip.)

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;

// We bind both IPv4 and IPv6 loopback because HttpListener on Linux resolves
// "localhost" to IPv6 only, but Chromium sometimes dials 127.0.0.1. Listing
// both prefixes covers both paths.
var listenPrefixes = new[]
{
    "http://127.0.0.1:8787/",
    "http://localhost:8787/",
};
const int ChunkSize = 16 * 1024;
const int TotalBytes = 10 * 1024 * 1024; // 10 MB
const int PollIntervalMs = 50;
const int PollDurationMs = 30_000;

Console.WriteLine("=== Spike B: bufferedAmount during 10 MB send ===");
Console.WriteLine($"SIPSorcery assembly: {typeof(RTCPeerConnection).Assembly.FullName}");
Console.WriteLine($"Chunk size: {ChunkSize} B, total: {TotalBytes} B");
Console.WriteLine();

var http = new HttpListener();
foreach (var p in listenPrefixes) http.Prefixes.Add(p);
http.Start();
Console.WriteLine($"Signaling server listening on: {string.Join(", ", listenPrefixes)}");
Console.WriteLine("Open spike-b-frontend.html in a browser to start the spike.");
Console.WriteLine();

RTCPeerConnection? pc = null;
RTCDataChannel? dc = null;
var samples = new List<(long tMs, ulong buffered)>();
var doneSignal = new TaskCompletionSource<bool>();

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

        pc = new RTCPeerConnection(null);

        pc.ondatachannel += channel =>
        {
            Console.WriteLine($"ondatachannel: label={channel.label} id={channel.id}");
            if (channel.label == "spike-b")
            {
                dc = channel;
                channel.onopen += () => Console.WriteLine("[dc] onopen");
                channel.onclose += () => Console.WriteLine("[dc] onclose");
                channel.onmessage += (_, _, _) => { /* unused */ };
                _ = Task.Run(() => SendLoop(channel, doneSignal));
            }
        };

        var setResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = offerSdp,
        });
        Console.WriteLine($"setRemoteDescription: {setResult}");
        if (setResult != SetDescriptionResultEnum.OK)
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            continue;
        }

        var answer = pc.createAnswer(null);
        await pc.setLocalDescription(answer);
        await WaitForIceGatheringComplete(pc);

        var answerBytes = Encoding.UTF8.GetBytes(pc.localDescription!.sdp.ToString());
        ctx.Response.ContentType = "application/sdp";
        ctx.Response.ContentLength64 = answerBytes.Length;
        await ctx.Response.OutputStream.WriteAsync(answerBytes);
        ctx.Response.Close();
        Console.WriteLine("Answer sent back.");

        // Wait for the send loop to finish (or timeout).
        await Task.WhenAny(doneSignal.Task, Task.Delay(PollDurationMs + 5000));
        PrintSamples();
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

void SendLoop(RTCDataChannel channel, TaskCompletionSource<bool> done)
{
    try
    {
        // Wait for the channel to actually be open before we start sending.
        while (channel.readyState != RTCDataChannelState.open)
            Thread.Sleep(20);
        Console.WriteLine("[send] channel is open, starting loop");

        var chunk = new byte[ChunkSize];
        Random.Shared.NextBytes(chunk);

        var sw = Stopwatch.StartNew();
        var pollCts = new CancellationTokenSource(PollDurationMs);
        var pollTask = Task.Run(async () =>
        {
            while (!pollCts.IsCancellationRequested)
            {
                samples.Add((sw.ElapsedMilliseconds, channel.bufferedAmount));
                try { await Task.Delay(PollIntervalMs, pollCts.Token); }
                catch (TaskCanceledException) { break; }
            }
        });

        long sent = 0;
        ulong peakBuffered = 0;
        while (sent < TotalBytes)
        {
            channel.send(chunk);
            sent += ChunkSize;
            if (channel.bufferedAmount > peakBuffered) peakBuffered = channel.bufferedAmount;
        }
        var sendElapsed = sw.ElapsedMilliseconds;
        Console.WriteLine($"[send] finished: {sent} bytes in {sendElapsed} ms  peak bufferedAmount={peakBuffered}");

        // Keep sampling for another ~2 s so we can see whether bufferedAmount drains.
        Thread.Sleep(2000);
        pollCts.Cancel();
        pollTask.Wait();

        done.TrySetResult(true);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[send] EXCEPTION: {ex}");
        done.TrySetException(ex);
    }
}

void PrintSamples()
{
    Console.WriteLine();
    Console.WriteLine($"=== bufferedAmount TIMELINE ({samples.Count} samples) ===");
    ulong peak = 0;
    foreach (var (t, b) in samples)
    {
        if (b > peak) peak = b;
        Console.WriteLine($"t={t,6}ms buffered={b}");
    }
    Console.WriteLine($"Peak bufferedAmount over run: {peak}");
    Console.WriteLine();
    Console.WriteLine("=== SUMMARY (JSON) ===");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        totalSamples = samples.Count,
        peakBuffered = peak,
        firstNonZeroAtMs = samples.FirstOrDefault(s => s.buffered > 0).tMs,
        finalBuffered = samples.Count > 0 ? (long)samples[^1].buffered : 0L,
    }, new JsonSerializerOptions { WriteIndented = true }));
}
