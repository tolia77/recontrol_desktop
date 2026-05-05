using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ReControl.Desktop.Protocol.Generated;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Clipboard;

namespace ReControl.Desktop.Tests.Clipboard;

public class ClipboardCtlChannelTests
{
    [Fact]
    public async Task Dispatch_Set_RoutesToSyncService()
    {
        var log = new LogService();
        var sync = new ClipboardSyncService(new ClipboardLoopGate(new FakeClock()), log);
        var payload = JsonSerializer.Serialize(new
        {
            kind = "set",
            content = "hello",
            contentHash = Hash16("hello"),
            originId = "origin-1",
            seq = 1,
            ts = 1
        });

        await ClipboardCtlChannel.DispatchEnvelopeAsync(payload, sync, _ => Task.CompletedTask, log);

        log.Snapshot().Any(l => l.Contains("received clipboard envelope hash=", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Dispatch_Refused_RoutesToSyncService()
    {
        var log = new LogService();
        var sync = new ClipboardSyncService(new ClipboardLoopGate(new FakeClock()), log);
        var payload = JsonSerializer.Serialize(new
        {
            kind = "refused",
            reason = "TOO_LARGE",
            originId = "origin-1",
            seq = 1,
            ts = 1
        });

        await ClipboardCtlChannel.DispatchEnvelopeAsync(payload, sync, _ => Task.CompletedTask, log);

        log.Snapshot().Any(l => l.Contains("clipboard refused reason=", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Dispatch_Capabilities_RoutesToSyncService()
    {
        var log = new LogService();
        var sync = new ClipboardSyncService(new ClipboardLoopGate(new FakeClock()), log);
        var payload = JsonSerializer.Serialize(new
        {
            kind = "capabilities",
            originId = "origin-1",
            outboundEnabled = true,
            inboundEnabled = true,
            maxBytes = 2_000_000,
            protocolVersion = "1.0",
            seq = 1,
            ts = 1
        });

        await ClipboardCtlChannel.DispatchEnvelopeAsync(payload, sync, _ => Task.CompletedTask, log);

        log.Snapshot().Any(l => l.Contains("clipboard capabilities", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Dispatch_UnknownKind_LogsWarning()
    {
        var log = new LogService();
        var sync = new ClipboardSyncService(new ClipboardLoopGate(new FakeClock()), log);
        var payload = JsonSerializer.Serialize(new { kind = "noop" });

        await ClipboardCtlChannel.DispatchEnvelopeAsync(payload, sync, _ => Task.CompletedTask, log);

        log.Snapshot().Any(l => l.Contains("unknown kind", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Dispatch_MalformedJson_LogsWarning()
    {
        var log = new LogService();
        var sync = new ClipboardSyncService(new ClipboardLoopGate(new FakeClock()), log);

        await ClipboardCtlChannel.DispatchEnvelopeAsync("{not json", sync, _ => Task.CompletedTask, log);

        log.Snapshot().Any(l => l.Contains("malformed json", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private static string Hash16(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
