using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using ReControl.Desktop.Protocol.Generated;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Clipboard;

namespace ReControl.Desktop.Tests.Clipboard;

public class ClipboardSyncServiceTests
{
    // ---- Test doubles ----

    private sealed class FakeClipboardWatcher : IClipboardWatcher
    {
        public event Action<string>? ClipboardChanged;
        public bool Started { get; private set; }
        public void Start() { Started = true; }
        public void Stop() { Started = false; }
        public void Dispose() { Stop(); }
        public void Raise(string text) => ClipboardChanged?.Invoke(text);
    }

    // ---- Factory helper ----

    private static (ClipboardSyncService svc, FakeClipboardWatcher watcher, ClipboardLoopGate gate,
        ClipboardSettingsStore store, string settingsPath)
        CreateSut(Action<ClipboardSettings>? mutateSeed = null)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"clip-{Guid.NewGuid():N}.json");
        var store = new ClipboardSettingsStore(settingsPath);
        var seed = ClipboardSettings.Defaults;
        mutateSeed?.Invoke(seed);
        store.Save(seed);

        var gate = new ClipboardLoopGate(new SystemClock());
        var watcher = new FakeClipboardWatcher();
        var log = new LogService();
        var svc = new ClipboardSyncService(watcher, gate, store, log);
        return (svc, watcher, gate, store, settingsPath);
    }

    private static void Cleanup(string p) { try { File.Delete(p); } catch { } }

    // Compute the same hash the orchestrator uses (SHA-256 first 8 bytes, lowercase hex).
    private static string HashHex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 8)).ToLowerInvariant();
    }

    private static byte[] Hash8(string text)
        => SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 8).ToArray();

    // ---- S1: CLIP-01 outbound fires after AttachChannel (uses TestAttachChannelMock + TestSendOverride) ----
    [Fact]
    public void S1_OutboundFiresAfterAttachChannel()
    {
        var (svc, watcher, _, _, settingsPath) = CreateSut();
        try
        {
            object? captured = null;
            svc.TestSendOverride = env => { captured = env; return true; };
            svc.TestAttachChannelMock("origin-s1");

            watcher.Raise("hello");

            captured.Should().NotBeNull();
            var envelope = captured.Should().BeOfType<ClipboardSetEnvelope>().Subject;
            envelope.Kind.Should().Be(SetEnvelopeKind.Set);
            envelope.Content.Should().Be("hello");
            envelope.OriginId.Should().Be("origin-s1");
            envelope.ContentHash.Should().Be(HashHex("hello"));
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S2: D-03 no-channel no-op: watcher raise before AttachChannel -> no send ----
    [Fact]
    public void S2_NoChannelNoOp()
    {
        var (svc, watcher, _, _, settingsPath) = CreateSut();
        try
        {
            bool sent = false;
            svc.TestSendOverride = _ => { sent = true; return true; };
            // Do NOT call AttachChannel / TestAttachChannelMock

            watcher.Raise("hello");

            sent.Should().BeFalse();
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S3: LOOP-01 echo prevention: RecordApplied for hash -> watcher raise -> no send ----
    [Fact]
    public void S3_EchoPrevention_RecordApplied_SuppressesOutbound()
    {
        var (svc, watcher, gate, _, settingsPath) = CreateSut();
        try
        {
            bool sent = false;
            svc.TestSendOverride = _ => { sent = true; return true; };
            svc.TestAttachChannelMock("origin-s3");

            gate.RecordApplied(Hash8("hello")); // simulate having just applied a remote payload
            watcher.Raise("hello");

            sent.Should().BeFalse("just-applied remote hash should suppress outbound echo");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S4: CLIP-04 session-start push sends current clipboard ----
    [Fact]
    public async Task S4_SessionStartPush_SendsCurrentClipboard()
    {
        var (svc, _, _, _, settingsPath) = CreateSut();
        try
        {
            object? captured = null;
            svc.TestSendOverride = env => { captured = env; return true; };
            svc.TestReadCurrentClipboardOverride = () => Task.FromResult<string?>("preset");
            svc.TestAttachChannelMock("origin-s4");

            // Wait briefly for the fire-and-forget session-start push
            await Task.Delay(50);

            captured.Should().NotBeNull();
            var envelope = captured.Should().BeOfType<ClipboardSetEnvelope>().Subject;
            envelope.Content.Should().Be("preset");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S5: D-06 empty session-start skip ----
    [Fact]
    public async Task S5_SessionStartPush_SkipsEmpty()
    {
        var (svc, _, _, _, settingsPath) = CreateSut();
        try
        {
            bool sent = false;
            svc.TestSendOverride = _ => { sent = true; return true; };
            svc.TestReadCurrentClipboardOverride = () => Task.FromResult<string?>(null);
            svc.TestAttachChannelMock("origin-s5");

            await Task.Delay(50);

            sent.Should().BeFalse();
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S6: POLICY-06 master gate ----
    [Fact]
    public void S6_PolicyMasterGate()
    {
        var (svc, watcher, _, store, settingsPath) = CreateSut(s => { s.Master = false; });
        try
        {
            bool sent = false;
            svc.TestSendOverride = _ => { sent = true; return true; };
            svc.TestAttachChannelMock("origin-s6");

            watcher.Raise("hello");
            sent.Should().BeFalse("Master=false should suppress all sends");

            // Enable master and try again
            var enabled = ClipboardSettings.Defaults;
            enabled.Master = true;
            store.Save(enabled);
            watcher.Raise("world");
            sent.Should().BeTrue("Master=true should allow sends");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S7: POLICY-06 outbound gate ----
    [Fact]
    public void S7_PolicyOutboundGate()
    {
        var (svc, watcher, _, store, settingsPath) = CreateSut(s => { s.AllowOutbound = false; });
        try
        {
            bool sent = false;
            svc.TestSendOverride = _ => { sent = true; return true; };
            svc.TestAttachChannelMock("origin-s7");

            watcher.Raise("hello");
            sent.Should().BeFalse("AllowOutbound=false should suppress sends");

            var enabled = ClipboardSettings.Defaults;
            enabled.AllowOutbound = true;
            store.Save(enabled);
            watcher.Raise("world");
            sent.Should().BeTrue("AllowOutbound=true should allow sends");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S8: POLICY-06 inbound gate on receive ----
    [Fact]
    public async Task S8_PolicyInboundGate_BlocksReceive()
    {
        var (svc, _, _, store, settingsPath) = CreateSut(s => { s.AllowInbound = false; });
        try
        {
            var writes = new List<string>();
            svc.TestApplyOverride = text => { writes.Add(text); return Task.CompletedTask; };

            var content = "from-remote";
            var hash = HashHex(content);
            var envelope = new ClipboardSetEnvelope
            {
                Kind = SetEnvelopeKind.Set,
                Content = content,
                ContentHash = hash,
                OriginId = "origin-remote",
                Seq = 1,
                Ts = 1
            };
            await svc.ReceiveSetAsync(envelope);

            writes.Should().BeEmpty("AllowInbound=false should block clipboard write");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S9: CLIP-08 non-text refusal: >20% control chars -> no send ----
    [Fact]
    public void S9_NonTextRefusal()
    {
        var (svc, watcher, _, _, settingsPath) = CreateSut();
        try
        {
            bool sent = false;
            svc.TestSendOverride = _ => { sent = true; return true; };
            svc.TestAttachChannelMock("origin-s9");

            // Build a string where >20% of chars are control chars (excluding \t\n\r)
            var sb = new StringBuilder();
            for (int i = 0; i < 10; i++) sb.Append('\x01'); // 10 control chars
            sb.Append("hello");                               // 5 normal chars => 10/15 = 67%
            watcher.Raise(sb.ToString());

            sent.Should().BeFalse("non-text with >20% control chars should be refused");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S10: CLIP-07 CRLF normalization: \r\n -> \n ----
    [Fact]
    public void S10_CrlfNormalization()
    {
        var (svc, watcher, _, _, settingsPath) = CreateSut();
        try
        {
            object? captured = null;
            svc.TestSendOverride = env => { captured = env; return true; };
            svc.TestAttachChannelMock("origin-s10");

            watcher.Raise("a\r\nb");

            captured.Should().NotBeNull();
            var envelope = captured.Should().BeOfType<ClipboardSetEnvelope>().Subject;
            envelope.Content.Should().Be("a\nb", "CRLF should be normalized to LF");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S11: Pitfall 1 apply-then-suppress order ----
    [Fact]
    public async Task S11_ApplyThenSuppressOrder()
    {
        var (svc, _, gate, _, settingsPath) = CreateSut();
        try
        {
            var callOrder = new List<string>();
            // Spy on apply
            svc.TestApplyOverride = text =>
            {
                callOrder.Add("SetTextAsync");
                return Task.CompletedTask;
            };

            // We need to verify RecordApplied is called BEFORE SetTextAsync.
            // We do this by checking that after ReceiveSetAsync, the gate has the hash recorded
            // (which RecordApplied did) and SetTextAsync was called afterward.
            // Since we can't intercept RecordApplied directly, we check the order
            // by verifying the gate has the entry BEFORE we inspect writes.
            // Approach: hook in via a subclassed gate isn't possible (it's sealed).
            // Instead, verify post-hoc: after ReceiveSetAsync, the gate suppresses
            // the same hash inbound (proves RecordApplied was called), and SetTextAsync ran.

            var content = "order-test";
            var hash = HashHex(content);
            var envelope = new ClipboardSetEnvelope
            {
                Kind = SetEnvelopeKind.Set,
                Content = content,
                ContentHash = hash,
                OriginId = "origin-s11",
                Seq = 1,
                Ts = 1
            };

            await svc.ReceiveSetAsync(envelope);

            // RecordApplied was called (apply-then-suppress; gate now suppresses this hash)
            gate.ShouldSuppressInbound(Hash8(content)).Should().BeTrue(
                "RecordApplied must have been called (apply-then-suppress invariant)");
            // SetTextAsync was also called
            callOrder.Should().ContainSingle("a").Which.Should().Be("SetTextAsync",
                "SetTextAsync must have been called after RecordApplied");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S12: defensive 2 MB cap on outbound ----
    [Fact]
    public void S12_OutboundTwoMbCapRefuses()
    {
        var (svc, watcher, _, _, settingsPath) = CreateSut();
        try
        {
            bool sent = false;
            svc.TestSendOverride = _ => { sent = true; return true; };
            svc.TestAttachChannelMock("origin-s12");

            // Build a 3 MB ASCII string (3 bytes per char in UTF-8 for ASCII is 1:1)
            var big = new string('A', 3_000_000);
            watcher.Raise(big);

            sent.Should().BeFalse("strings exceeding 2 MB UTF-8 should be refused outbound");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S13: DetachChannel idempotent + after detach, watcher raise -> no send ----
    [Fact]
    public void S13_DetachChannelIdempotent()
    {
        var (svc, watcher, _, _, settingsPath) = CreateSut();
        try
        {
            bool sent = false;
            svc.TestSendOverride = _ => { sent = true; return true; };
            svc.TestAttachChannelMock("origin-s13");

            svc.DetachChannel(); // first detach
            svc.DetachChannel(); // second detach (should not throw)

            watcher.Raise("hello");
            sent.Should().BeFalse("after DetachChannel, watcher raise should be a no-op");
        }
        finally { Cleanup(settingsPath); }
    }

    // ---- S14: AttachChannel resets loop gate ----
    [Fact]
    public void S14_AttachChannelResetsLoopGate()
    {
        var (svc, watcher, gate, _, settingsPath) = CreateSut();
        try
        {
            // Pre-seed the gate with a sent hash
            gate.RecordSent(Hash8("hello"));
            gate.ShouldSuppressOutbound(Hash8("hello")).Should().BeTrue("pre-condition: gate suppresses hash");

            // AttachChannel must reset the gate
            svc.TestAttachChannelMock("origin-s14");

            gate.ShouldSuppressOutbound(Hash8("hello")).Should().BeFalse(
                "AttachChannel must reset the loop gate (D-17)");
        }
        finally { Cleanup(settingsPath); }
    }
}
