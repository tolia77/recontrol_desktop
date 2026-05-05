using System;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using ReControl.Desktop.Services.Clipboard;

namespace ReControl.Desktop.Tests.Clipboard;

public class ClipboardLoopGateTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UnixEpoch;
        public void AdvanceMs(int ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    [Fact]
    public void ShouldSuppressOutbound_TrueWithinTtl_FalseAfterTtl()
    {
        var clock = new FakeClock();
        var gate = new ClipboardLoopGate(clock);
        var hash = Hash8("hello");

        gate.RecordSent(hash);
        gate.ShouldSuppressOutbound(hash).Should().BeTrue();

        clock.AdvanceMs(2500);
        gate.ShouldSuppressOutbound(hash).Should().BeFalse();
    }

    [Fact]
    public void ShouldSuppressInbound_TrueWithinTtl_FalseAfterTtl()
    {
        var clock = new FakeClock();
        var gate = new ClipboardLoopGate(clock);
        var hash = Hash8("inbound");

        gate.RecordApplied(hash);
        gate.ShouldSuppressInbound(hash).Should().BeTrue();

        clock.AdvanceMs(2500);
        gate.ShouldSuppressInbound(hash).Should().BeFalse();
    }

    [Fact]
    public void RecordApplied_EvictsOldestBeyondCapacity()
    {
        var clock = new FakeClock();
        var gate = new ClipboardLoopGate(clock);
        var hashes = Enumerable.Range(0, 9).Select(i => Hash8($"h-{i}")).ToArray();

        foreach (var hash in hashes)
        {
            gate.RecordApplied(hash);
            clock.AdvanceMs(1);
        }

        gate.ShouldSuppressInbound(hashes[0]).Should().BeFalse();
        gate.ShouldSuppressInbound(hashes[8]).Should().BeTrue();
    }

    [Fact]
    public void Reset_ClearsSentAndRing()
    {
        var clock = new FakeClock();
        var gate = new ClipboardLoopGate(clock);
        var sent = Hash8("sent");
        var inbound = Hash8("inbound");

        gate.RecordSent(sent);
        gate.RecordApplied(inbound);
        gate.Reset();

        gate.ShouldSuppressOutbound(sent).Should().BeFalse();
        gate.ShouldSuppressInbound(inbound).Should().BeFalse();
    }

    [Fact]
    public void ShouldSuppressInbound_UsesByteEquality_NotString()
    {
        var clock = new FakeClock();
        var gate = new ClipboardLoopGate(clock);
        var hash = Hash8("same");
        var equalByBytes = hash.ToArray();

        gate.RecordApplied(hash);
        gate.ShouldSuppressInbound(equalByBytes).Should().BeTrue();
    }

    private static byte[] Hash8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return SHA256.HashData(bytes).AsSpan(0, 8).ToArray();
    }
}
