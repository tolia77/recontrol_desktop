using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    // Test doubles

    private sealed class FakeClipboardWatcher : IClipboardWatcher
    {
        public event Action<string>? ClipboardChanged;
        public bool Started { get; private set; }
        public void Start() { Started = true; }
        public void Stop() { Started = false; }
        public void Dispose() { Stop(); }
        public void Raise(string text) => ClipboardChanged?.Invoke(text);
    }

    // Factory helper

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

    // S1: CLIP-01 outbound fires after AttachChannel (uses TestAttachChannelMock + TestSendOverride)
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

    // S2: D-03 no-channel no-op: watcher raise before AttachChannel -> no send
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

    // S3: LOOP-01 echo prevention: RecordApplied for hash -> watcher raise -> no send
    [Fact]
    public void S3_EchoPrevention_RecordApplied_SuppressesOutbound()
    {
        var (svc, watcher, gate, _, settingsPath) = CreateSut();
        try
        {
            // Phase 15: TestAttachChannelMock now also emits a capabilities envelope, so we
            // filter to ClipboardSetEnvelope to isolate the outbound-set path under test.
            bool sentSet = false;
            svc.TestSendOverride = env => { if (env is ClipboardSetEnvelope) sentSet = true; return true; };
            svc.TestAttachChannelMock("origin-s3");

            gate.RecordApplied(Hash8("hello")); // simulate having just applied a remote payload
            watcher.Raise("hello");

            sentSet.Should().BeFalse("just-applied remote hash should suppress outbound echo");
        }
        finally { Cleanup(settingsPath); }
    }

    // S4: CLIP-04 session-start push sends current clipboard
    [Fact]
    public async Task S4_SessionStartPush_SendsCurrentClipboard()
    {
        var (svc, _, _, _, settingsPath) = CreateSut();
        try
        {
            // Phase 15: filter to ClipboardSetEnvelope to ignore the capabilities envelope
            // that TestAttachChannelMock now also emits.
            ClipboardSetEnvelope? captured = null;
            svc.TestSendOverride = env => { if (env is ClipboardSetEnvelope s) captured = s; return true; };
            svc.TestReadCurrentClipboardOverride = () => Task.FromResult<string?>("preset");
            svc.TestAttachChannelMock("origin-s4");

            // WR-09: synchronously gate on the session-start push instead of a 50ms sleep.
            await svc.SessionStartPushTask;

            captured.Should().NotBeNull();
            captured!.Content.Should().Be("preset");
        }
        finally { Cleanup(settingsPath); }
    }

    // S5: D-06 empty session-start skip
    [Fact]
    public async Task S5_SessionStartPush_SkipsEmpty()
    {
        var (svc, _, _, _, settingsPath) = CreateSut();
        try
        {
            // Phase 15: filter to ClipboardSetEnvelope; capabilities is expected on attach.
            bool sentSet = false;
            svc.TestSendOverride = env => { if (env is ClipboardSetEnvelope) sentSet = true; return true; };
            svc.TestReadCurrentClipboardOverride = () => Task.FromResult<string?>(null);
            svc.TestAttachChannelMock("origin-s5");

            // WR-09: synchronously gate on the session-start push instead of a 50ms sleep.
            await svc.SessionStartPushTask;

            sentSet.Should().BeFalse();
        }
        finally { Cleanup(settingsPath); }
    }

    // S6: POLICY-06 master gate
    [Fact]
    public void S6_PolicyMasterGate()
    {
        var (svc, watcher, _, store, settingsPath) = CreateSut(s => { s.Master = false; });
        try
        {
            // Phase 15: filter to ClipboardSetEnvelope; capabilities envelope on attach is expected.
            bool sentSet = false;
            svc.TestSendOverride = env => { if (env is ClipboardSetEnvelope) sentSet = true; return true; };
            svc.TestAttachChannelMock("origin-s6");

            watcher.Raise("hello");
            sentSet.Should().BeFalse("Master=false should suppress all sends");

            // Enable master and try again
            var enabled = ClipboardSettings.Defaults;
            enabled.Master = true;
            store.Save(enabled);
            watcher.Raise("world");
            sentSet.Should().BeTrue("Master=true should allow sends");
        }
        finally { Cleanup(settingsPath); }
    }

    // S7: POLICY-06 outbound gate
    [Fact]
    public void S7_PolicyOutboundGate()
    {
        var (svc, watcher, _, store, settingsPath) = CreateSut(s => { s.AllowOutbound = false; });
        try
        {
            // Phase 15: filter to ClipboardSetEnvelope; capabilities envelope on attach is expected.
            bool sentSet = false;
            svc.TestSendOverride = env => { if (env is ClipboardSetEnvelope) sentSet = true; return true; };
            svc.TestAttachChannelMock("origin-s7");

            watcher.Raise("hello");
            sentSet.Should().BeFalse("AllowOutbound=false should suppress sends");

            var enabled = ClipboardSettings.Defaults;
            enabled.AllowOutbound = true;
            store.Save(enabled);
            watcher.Raise("world");
            sentSet.Should().BeTrue("AllowOutbound=true should allow sends");
        }
        finally { Cleanup(settingsPath); }
    }

    // S8: POLICY-06 inbound gate on receive
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

    // S9: CLIP-08 non-text refusal: >20% control chars -> no send
    [Fact]
    public void S9_NonTextRefusal()
    {
        var (svc, watcher, _, _, settingsPath) = CreateSut();
        try
        {
            // Phase 15: filter to ClipboardSetEnvelope; capabilities envelope on attach is expected.
            bool sentSet = false;
            svc.TestSendOverride = env => { if (env is ClipboardSetEnvelope) sentSet = true; return true; };
            svc.TestAttachChannelMock("origin-s9");

            // Build a string where >20% of chars are control chars (excluding \t\n\r)
            var sb = new StringBuilder();
            for (int i = 0; i < 10; i++) sb.Append('\x01'); // 10 control chars
            sb.Append("hello");                               // 5 normal chars => 10/15 = 67%
            watcher.Raise(sb.ToString());

            sentSet.Should().BeFalse("non-text with >20% control chars should be refused");
        }
        finally { Cleanup(settingsPath); }
    }

    // S10: CLIP-07 CRLF normalization: \r\n -> \n
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

    // S11: Pitfall 1 apply-then-suppress order
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

    // S12: defensive 2 MB cap on outbound
    [Fact]
    public void S12_OutboundTwoMbCapRefuses()
    {
        var (svc, watcher, _, _, settingsPath) = CreateSut();
        try
        {
            // Phase 15: filter to ClipboardSetEnvelope; capabilities envelope on attach is expected.
            bool sentSet = false;
            svc.TestSendOverride = env => { if (env is ClipboardSetEnvelope) sentSet = true; return true; };
            svc.TestAttachChannelMock("origin-s12");

            // Build a 3 MB ASCII string (3 bytes per char in UTF-8 for ASCII is 1:1)
            var big = new string('A', 3_000_000);
            watcher.Raise(big);

            sentSet.Should().BeFalse("strings exceeding 2 MB UTF-8 should be refused outbound");
        }
        finally { Cleanup(settingsPath); }
    }

    // S13: DetachChannel idempotent + after detach, watcher raise -> no send
    [Fact]
    public void S13_DetachChannelIdempotent()
    {
        var (svc, watcher, _, _, settingsPath) = CreateSut();
        try
        {
            // Phase 15: filter to ClipboardSetEnvelope; the attach-time capabilities envelope is expected.
            bool sentSet = false;
            svc.TestSendOverride = env => { if (env is ClipboardSetEnvelope) sentSet = true; return true; };
            svc.TestAttachChannelMock("origin-s13");

            svc.DetachChannel(); // first detach
            svc.DetachChannel(); // second detach (should not throw)

            watcher.Raise("hello");
            sentSet.Should().BeFalse("after DetachChannel, watcher raise should be a no-op");
        }
        finally { Cleanup(settingsPath); }
    }

    // S14: AttachChannel resets loop gate
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

    // Phase 15 Plan 02: Capabilities advertisement + categorized refusal protocol

    // Phase 15 #1: CAP-01 — AttachChannel triggers a single capabilities envelope
    [Fact]
    public void Capabilities_OnAttachChannel_SendsEnvelope()
    {
        var (svc, _, _, _, settingsPath) = CreateSut();
        try
        {
            var sent = new List<object>();
            svc.TestSendOverride = env => { sent.Add(env); return true; };

            svc.TestAttachChannelMock("origin-1");

            var capsEnvs = sent.OfType<ClipboardCapabilitiesEnvelope>().ToList();
            capsEnvs.Should().HaveCount(1, "AttachChannel must advertise capabilities exactly once");
            var caps = capsEnvs.Single();
            caps.Kind.Should().Be(CapabilitiesEnvelopeKind.Capabilities);
            caps.OriginId.Should().Be("origin-1");
            caps.MaxBytes.Should().Be(2_000_000);
            caps.ProtocolVersion.Should().Be("1.0");
            caps.Seq.Should().Be(1, "first envelope on a fresh channel uses the seqCounter starting at 1");
        }
        finally { Cleanup(settingsPath); }
    }

    // Phase 15 #2: CAP-02 — OnSettingsChanged re-advertises
    [Fact]
    public void Capabilities_OnSettingsChanged_ReAdvertises()
    {
        var (svc, _, _, _, settingsPath) = CreateSut();
        try
        {
            var sent = new List<object>();
            svc.TestSendOverride = env => { sent.Add(env); return true; };

            svc.TestAttachChannelMock("origin-cap2");
            // Clear the initial AttachChannel emission so we can isolate the OnSettingsChanged effect.
            sent.Clear();

            svc.OnSettingsChanged();

            var capsEnvs = sent.OfType<ClipboardCapabilitiesEnvelope>().ToList();
            capsEnvs.Should().HaveCount(1, "OnSettingsChanged must emit a fresh capabilities envelope");
            capsEnvs.Single().Seq.Should().BeGreaterThan(1, "re-advertise must use a fresh seq from the shared counter");
        }
        finally { Cleanup(settingsPath); }
    }

    // Phase 15 #3: CAP-01 — flags reflect Master / AllowOutbound / AllowInbound conjunctions
    [Fact]
    public void Capabilities_FlagsReflectSettings()
    {
        // Case A: Master=true, AllowOutbound=true, AllowInbound=false
        var (svcA, _, _, _, pathA) = CreateSut(s =>
        {
            s.Master = true; s.AllowOutbound = true; s.AllowInbound = false;
        });
        try
        {
            var sentA = new List<object>();
            svcA.TestSendOverride = env => { sentA.Add(env); return true; };
            svcA.TestAttachChannelMock("origin-flagsA");

            var capsA = sentA.OfType<ClipboardCapabilitiesEnvelope>().Single();
            capsA.OutboundEnabled.Should().BeTrue("Master && AllowOutbound -> outboundEnabled");
            capsA.InboundEnabled.Should().BeFalse("AllowInbound=false -> inboundEnabled false");
        }
        finally { Cleanup(pathA); }

        // Case B: Master=false -> both flags false regardless of allow-* fields
        var (svcB, _, _, _, pathB) = CreateSut(s =>
        {
            s.Master = false; s.AllowOutbound = true; s.AllowInbound = true;
        });
        try
        {
            var sentB = new List<object>();
            svcB.TestSendOverride = env => { sentB.Add(env); return true; };
            svcB.TestAttachChannelMock("origin-flagsB");

            var capsB = sentB.OfType<ClipboardCapabilitiesEnvelope>().Single();
            capsB.OutboundEnabled.Should().BeFalse("Master=false -> outboundEnabled false");
            capsB.InboundEnabled.Should().BeFalse("Master=false -> inboundEnabled false");
        }
        finally { Cleanup(pathB); }
    }

    // Phase 15 #4: CAP-03 — Refused on PAUSED
    [Fact]
    public async Task Refused_OnPaused()
    {
        var (svc, _, gate, _, settingsPath) = CreateSut();
        try
        {
            var refusals = new List<ClipboardRefusedEnvelope>();
            svc.TestAttachChannelMock("origin-paused");
            svc.SetPaused(true);

            var content = "paused-content";
            var hash = HashHex(content);
            var env = new ClipboardSetEnvelope
            {
                Kind = SetEnvelopeKind.Set,
                Content = content,
                ContentHash = hash,
                OriginId = "browser-origin",
                Seq = 42,
                Ts = 1
            };

            await svc.ReceiveSetAsync(env, r => { refusals.Add(r); return Task.CompletedTask; });

            refusals.Should().HaveCount(1);
            var r = refusals.Single();
            r.Reason.Should().Be(ClipboardRefusalReason.Paused);
            r.OriginId.Should().Be("browser-origin", "OriginId echoes the offending sender's id (D-05)");
            r.Seq.Should().Be(42, "Seq echoes the offending envelope's seq");
            r.Kind.Should().Be(RefusedEnvelopeKind.Refused);

            // WR-08 invariant: refused-but-not-applied must NOT have poisoned the loop gate.
            gate.ShouldSuppressInbound(Hash8(content)).Should().BeFalse(
                "refused returns BEFORE RecordApplied -- loop gate must not have recorded this hash");
        }
        finally { Cleanup(settingsPath); }
    }

    // Phase 15 #5: CAP-03 — Refused on MASTER_DISABLED
    [Fact]
    public async Task Refused_OnMasterDisabled()
    {
        var (svc, _, gate, _, settingsPath) = CreateSut(s => { s.Master = false; });
        try
        {
            var refusals = new List<ClipboardRefusedEnvelope>();
            svc.TestAttachChannelMock("origin-master");

            var content = "master-content";
            var hash = HashHex(content);
            var env = new ClipboardSetEnvelope
            {
                Kind = SetEnvelopeKind.Set,
                Content = content,
                ContentHash = hash,
                OriginId = "browser-origin",
                Seq = 7,
                Ts = 1
            };

            await svc.ReceiveSetAsync(env, r => { refusals.Add(r); return Task.CompletedTask; });

            refusals.Should().ContainSingle().Which.Reason.Should().Be(ClipboardRefusalReason.MasterDisabled);
            gate.ShouldSuppressInbound(Hash8(content)).Should().BeFalse("refused must not poison loop gate");
        }
        finally { Cleanup(settingsPath); }
    }

    // Phase 15 #6: CAP-03 — Refused on INBOUND_DISABLED
    [Fact]
    public async Task Refused_OnInboundDisabled()
    {
        var (svc, _, gate, _, settingsPath) = CreateSut(s =>
        {
            s.Master = true; s.AllowInbound = false;
        });
        try
        {
            var refusals = new List<ClipboardRefusedEnvelope>();
            svc.TestAttachChannelMock("origin-inbound");

            var content = "inbound-content";
            var hash = HashHex(content);
            var env = new ClipboardSetEnvelope
            {
                Kind = SetEnvelopeKind.Set,
                Content = content,
                ContentHash = hash,
                OriginId = "browser-origin",
                Seq = 13,
                Ts = 1
            };

            await svc.ReceiveSetAsync(env, r => { refusals.Add(r); return Task.CompletedTask; });

            refusals.Should().ContainSingle().Which.Reason.Should().Be(ClipboardRefusalReason.InboundDisabled);
            gate.ShouldSuppressInbound(Hash8(content)).Should().BeFalse("refused must not poison loop gate");
        }
        finally { Cleanup(settingsPath); }
    }

    // Phase 15 #7: CAP-03 — Refused on NON_TEXT (>20% control chars)
    [Fact]
    public async Task Refused_OnNonText()
    {
        var (svc, _, gate, _, settingsPath) = CreateSut();
        try
        {
            var refusals = new List<ClipboardRefusedEnvelope>();
            svc.TestAttachChannelMock("origin-nontext");

            // Build content rejected by ClipboardNormalization (>20% control chars).
            // ClipboardNormalization strips NULs first, so use \x01 (SOH) which is preserved
            // and counts as a control char.
            var sb = new StringBuilder();
            for (int i = 0; i < 10; i++) sb.Append('\x01'); // 10 control chars
            sb.Append("hello");                              // 5 normal chars => 10/15 = 67% > 20%
            var content = sb.ToString();
            // ClipboardNormalization does not strip \x01, so envelope.Content hashes as-is.
            var hash = HashHex(content);
            var env = new ClipboardSetEnvelope
            {
                Kind = SetEnvelopeKind.Set,
                Content = content,
                ContentHash = hash,
                OriginId = "browser-origin",
                Seq = 99,
                Ts = 1
            };

            await svc.ReceiveSetAsync(env, r => { refusals.Add(r); return Task.CompletedTask; });

            refusals.Should().ContainSingle().Which.Reason.Should().Be(ClipboardRefusalReason.NonText);
            gate.ShouldSuppressInbound(Hash8(content)).Should().BeFalse("refused must not poison loop gate");
        }
        finally { Cleanup(settingsPath); }
    }

    // Phase 15 #8: D-03 — loop-gate suppression stays SILENT (no refusal emitted)
    [Fact]
    public async Task Refused_NotEmittedOnLoopSuppression()
    {
        var (svc, _, gate, _, settingsPath) = CreateSut();
        try
        {
            var refusals = new List<ClipboardRefusedEnvelope>();
            svc.TestAttachChannelMock("origin-loop");

            var content = "loop-content";
            var hash = HashHex(content);
            // Pre-seed the loop gate so the inbound is suppressed BEFORE policy checks run.
            gate.RecordApplied(Hash8(content));

            var env = new ClipboardSetEnvelope
            {
                Kind = SetEnvelopeKind.Set,
                Content = content,
                ContentHash = hash,
                OriginId = "browser-origin",
                Seq = 55,
                Ts = 1
            };

            await svc.ReceiveSetAsync(env, r => { refusals.Add(r); return Task.CompletedTask; });

            refusals.Should().BeEmpty(
                "loop-gate-suppressed inbound is by-design echo prevention -- no refused envelope per D-03");
        }
        finally { Cleanup(settingsPath); }
    }

    // Phase 15 #9: D-04 — hash mismatch stays SILENT (no refusal emitted)
    [Fact]
    public async Task Refused_NotEmittedOnHashMismatch()
    {
        var (svc, _, _, _, settingsPath) = CreateSut();
        try
        {
            var refusals = new List<ClipboardRefusedEnvelope>();
            svc.TestAttachChannelMock("origin-mismatch");

            // Hash deliberately does NOT match Content (16 chars of 0 != real hash of "abc").
            var env = new ClipboardSetEnvelope
            {
                Kind = SetEnvelopeKind.Set,
                Content = "abc",
                ContentHash = "0000000000000000",
                OriginId = "browser-origin",
                Seq = 1,
                Ts = 1
            };

            await svc.ReceiveSetAsync(env, r => { refusals.Add(r); return Task.CompletedTask; });

            refusals.Should().BeEmpty(
                "hash mismatch is a protocol-layer bug -- no refused envelope per D-04");
        }
        finally { Cleanup(settingsPath); }
    }

    // Phase 15 #10: CAP-06 — ReceiveCapabilities caches + re-advertises (handshake completion)
    [Fact]
    public void ReceiveCapabilities_CachesEnvelope_AndReAdvertises()
    {
        var (svc, _, _, _, settingsPath) = CreateSut();
        try
        {
            var sent = new List<object>();
            svc.TestSendOverride = env => { sent.Add(env); return true; };

            // Attach so the re-advertise reply is observable.
            svc.TestAttachChannelMock("origin-recvcaps");
            sent.Clear();

            var capsEnv = new ClipboardCapabilitiesEnvelope
            {
                Kind = CapabilitiesEnvelopeKind.Capabilities,
                OriginId = "browser-1",
                OutboundEnabled = true,
                InboundEnabled = false,
                MaxBytes = 2_000_000,
                ProtocolVersion = "1.0",
                Seq = 1,
                Ts = 1
            };

            // Each inbound caps advertisement is answered with exactly one of OUR
            // capabilities envelopes -- this completes the handshake after the
            // browser's listener is live (the attach-time send races ahead of it).
            svc.ReceiveCapabilities(capsEnv);
            svc.ReceiveCapabilities(capsEnv);

            var capsReplies = sent.OfType<ClipboardCapabilitiesEnvelope>().ToList();
            capsReplies.Should().HaveCount(2,
                "ReceiveCapabilities must re-advertise the desktop's own caps once per inbound advertisement");
            // D-09 asymmetric enforcement still holds: the reply carries the desktop's
            // OWN settings, never echoes the browser's, and never leaks clipboard content.
            sent.OfType<ClipboardSetEnvelope>().Should().BeEmpty(
                "ReceiveCapabilities must never push clipboard content");
            capsReplies.Should().OnlyContain(e => e.OriginId == "origin-recvcaps",
                "re-advertised caps use the desktop's channel originId, not the browser's");
        }
        finally { Cleanup(settingsPath); }
    }
}
