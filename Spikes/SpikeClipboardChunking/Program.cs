// Spike Clipboard Chunking -- single-frame feasibility near the 2 MB UTF-8 cap.
//
// Question: can the five D-10 fixtures round-trip as a single RTCDataChannel frame
// on SIPSorcery 10.0.3 <-> Chromium without SCTP errors, corruption, or timeout?
//
// PASS/FAIL rule per CONTEXT D-10 and RESEARCH Pitfall 33:
// - PASS: bytes round-trip identically and no SCTP/data-channel errors.
// - REFUSED_AS_EXPECTED: only valid for CyrillicOverCap (> 2 MB UTF-8 cap).
// - FAIL: any other result (timeout, mismatch, truncation, SCTP/data-channel error).
//
// Run: `dotnet run --project recontrol_desktop/Spikes/SpikeClipboardChunking`
// Open spike-clipboard-frontend.html in a Chromium-based browser.

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SIPSorcery.Net;

var listenPrefixes = new[]
{
    "http://127.0.0.1:8787/",
    "http://localhost:8787/",
};

Console.WriteLine("=== Spike Clipboard Chunking ===");
Console.WriteLine($"SIPSorcery assembly: {typeof(RTCPeerConnection).Assembly.FullName}");
Console.WriteLine();

var fixtures = GenerateFixtures.Matrix();
var summary = new Dictionary<string, FixtureSummary>(StringComparer.OrdinalIgnoreCase);
var activeByFixture = new Dictionary<string, RTCPeerConnection>(StringComparer.OrdinalIgnoreCase);

var http = new HttpListener();
foreach (var p in listenPrefixes) http.Prefixes.Add(p);
http.Start();
Console.WriteLine($"Signaling server listening on: {string.Join(", ", listenPrefixes)}");
Console.WriteLine("Open spike-clipboard-frontend.html in a browser to start the spike.");
Console.WriteLine();

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

    var path = ctx.Request.Url?.AbsolutePath ?? string.Empty;
    if (ctx.Request.HttpMethod == "GET" && path.StartsWith("/fixture/", StringComparison.OrdinalIgnoreCase))
    {
        var fixtureName = path["/fixture/".Length..];
        if (!fixtures.TryGetValue(fixtureName, out var factory))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            continue;
        }

        var bytes = factory();
        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
        Console.WriteLine($"served fixture {fixtureName} bytes={bytes.Length}");
        continue;
    }

    if (ctx.Request.HttpMethod == "POST" && path == "/offer")
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var offerSdp = await reader.ReadToEndAsync();
        var fixtureName = ctx.Request.QueryString["fixture"] ?? "unknown";
        var metric = summary.GetValueOrDefault(fixtureName) ?? new FixtureSummary(fixtureName);
        metric.StartedAt = DateTimeOffset.UtcNow;
        summary[fixtureName] = metric;

        var pc = new RTCPeerConnection(null);
        activeByFixture[fixtureName] = pc;

        pc.onconnectionstatechange += state =>
        {
            Console.WriteLine($"[{fixtureName}] pc state={state}");
            if (state == RTCPeerConnectionState.closed)
            {
                metric.EndedAt = DateTimeOffset.UtcNow;
                PrintSummaryTable(summary);
            }
        };

        pc.ondatachannel += channel =>
        {
            Console.WriteLine($"[{fixtureName}] ondatachannel label={channel.label} id={channel.id}");
            channel.onopen += () => Console.WriteLine($"[{fixtureName}] [dc] onopen");
            channel.onclose += () => Console.WriteLine($"[{fixtureName}] [dc] onclose");
            channel.onerror += error =>
            {
                metric.SctpOrChannelError = true;
                metric.ErrorMessages.Add(error);
                Console.WriteLine($"[{fixtureName}] [dc] error={error}");
            };
            channel.onmessage += (_, _, data) =>
            {
                metric.BytesIn += data.Length;
                channel.send(data);
                metric.BytesOutEchoed += data.Length;
                var hash8 = Convert.ToHexString(SHA256.HashData(data).AsSpan(0, 8)).ToLowerInvariant();
                Console.WriteLine($"[{fixtureName}] echoed {data.Length} bytes hashFirst8={hash8}");
            };
        };

        var setResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = offerSdp,
        });

        if (setResult != SetDescriptionResultEnum.OK)
        {
            metric.SctpOrChannelError = true;
            metric.ErrorMessages.Add($"setRemoteDescription={setResult}");
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            continue;
        }

        var answer = pc.createAnswer(null);
        await pc.setLocalDescription(answer);
        await WaitForIceGatheringComplete(pc);

        var answerSdp = pc.localDescription!.sdp.ToString();
        var maxMessageSizeLine = answerSdp
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("a=max-message-size", StringComparison.Ordinal));
        Console.WriteLine($"[{fixtureName}] observed {maxMessageSizeLine ?? "a=max-message-size:<missing>"}");

        var answerBytes = Encoding.UTF8.GetBytes(answerSdp);
        ctx.Response.ContentType = "application/sdp";
        ctx.Response.ContentLength64 = answerBytes.Length;
        await ctx.Response.OutputStream.WriteAsync(answerBytes);
        ctx.Response.Close();
        continue;
    }

    if (ctx.Request.HttpMethod == "POST" && path == "/ice")
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var candidate = await reader.ReadToEndAsync();
        var fixtureName = ctx.Request.QueryString["fixture"] ?? "unknown";
        if (activeByFixture.TryGetValue(fixtureName, out var pc))
        {
            pc.addIceCandidate(new RTCIceCandidateInit { candidate = candidate });
        }

        ctx.Response.StatusCode = 202;
        ctx.Response.Close();
        continue;
    }

    ctx.Response.StatusCode = 404;
    ctx.Response.Close();
}

static void AddCors(HttpListenerResponse res)
{
    res.AddHeader("Access-Control-Allow-Origin", "*");
    res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
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

static void PrintSummaryTable(IReadOnlyDictionary<string, FixtureSummary> summary)
{
    Console.WriteLine();
    Console.WriteLine("=== SPIKE SUMMARY ===");
    Console.WriteLine("Fixture | BytesIn | BytesOutEchoed | SctpOrChannelError | RoundTripMs");
    foreach (var row in summary.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
    {
        var roundTripMs = row.EndedAt.HasValue && row.StartedAt.HasValue
            ? (long)(row.EndedAt.Value - row.StartedAt.Value).TotalMilliseconds
            : -1;
        Console.WriteLine($"{row.Name} | {row.BytesIn} | {row.BytesOutEchoed} | {row.SctpOrChannelError} | {roundTripMs}");
    }
    Console.WriteLine();
}

internal sealed class FixtureSummary(string name)
{
    public string Name { get; } = name;
    public long BytesIn { get; set; }
    public long BytesOutEchoed { get; set; }
    public bool SctpOrChannelError { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public List<string> ErrorMessages { get; } = [];
}
