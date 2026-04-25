using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using ReControl.Desktop.Services.Files.FilesProtocol;
using Xunit;

namespace ReControl.Desktop.Tests.Files;

/// <summary>
/// Unit tests for <see cref="TransferRegistry"/> -- the contract surface
/// the Phase-11 transfer engine relies on.
///
/// Tests use thin <see cref="ITransferEntry"/> doubles (registered via
/// <see cref="TransferRegistry.RegisterEntry"/>) for the cancel-counter
/// cases so no FileStream / RTCDataChannel is required. The
/// KnownParentDirs test exercises a real <see cref="UploadReceiver"/>
/// against a temp directory because that path is what records the
/// parent-dir bookkeeping.
/// </summary>
public class TransferRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Services.LogService _log;
    private readonly List<UploadReceiver> _receivers = new();

    public TransferRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "recontrol-registry-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _log = new Services.LogService();
    }

    public void Dispose()
    {
        foreach (var r in _receivers) { try { r.Dispose(); } catch { } }
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void AllocateId_Returns_Monotonically_Increasing_Values_Starting_At_1()
    {
        var registry = new TransferRegistry();

        var a = registry.AllocateId();
        var b = registry.AllocateId();
        var c = registry.AllocateId();

        a.Should().Be(1u);
        b.Should().Be(2u);
        c.Should().Be(3u);
    }

    [Fact]
    public void TryGet_Returns_Registered_Entry_And_Remove_Removes_It()
    {
        var registry = new TransferRegistry();
        var entry = new CountingEntry();
        var id = registry.AllocateId();
        registry.RegisterEntry(id, entry);

        registry.TryGet(id, out var got).Should().BeTrue();
        got.Should().BeSameAs(entry);

        registry.Remove(id).Should().BeTrue();
        registry.TryGet(id, out _).Should().BeFalse();
    }

    [Fact]
    public void CancelAll_Cancels_Every_Entry_And_Empties_Registry()
    {
        var registry = new TransferRegistry();
        var d1 = new CountingEntry();
        var d2 = new CountingEntry();
        registry.RegisterEntry(registry.AllocateId(), d1);
        registry.RegisterEntry(registry.AllocateId(), d2);

        registry.CancelAll();

        d1.CancelCount.Should().Be(1);
        d2.CancelCount.Should().Be(1);
        registry.TryGet(1u, out _).Should().BeFalse();
        registry.TryGet(2u, out _).Should().BeFalse();
    }

    [Fact]
    public void CancelAll_Continues_When_An_Entry_Throws()
    {
        var registry = new TransferRegistry();
        var throwing = new CountingEntry { ThrowOnCancel = true };
        var normal = new CountingEntry();
        registry.RegisterEntry(registry.AllocateId(), throwing);
        registry.RegisterEntry(registry.AllocateId(), normal);

        var act = () => registry.CancelAll();

        act.Should().NotThrow();
        throwing.CancelCount.Should().Be(1);
        normal.CancelCount.Should().Be(1);
    }

    [Fact]
    public void KnownParentDirs_Records_Parent_Of_Each_Upload_Deduped()
    {
        var registry = new TransferRegistry();
        var dirA = Path.Combine(_tempDir, "a");
        var dirB = Path.Combine(_tempDir, "b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        // Two uploads in dirA, one in dirB -> KnownParentDirs has 2 entries.
        NewUpload(registry, "1.bin", dirA);
        NewUpload(registry, "2.bin", dirA);
        NewUpload(registry, "3.bin", dirB);

        var parents = registry.KnownParentDirs;
        parents.Should().HaveCount(2);
        parents.Should().Contain(new[] { dirA, dirB });
    }

    // ----- helpers -----

    /// <summary>
    /// Creates and registers a real <see cref="UploadReceiver"/> against a
    /// fresh .partial path under <paramref name="parent"/>. Tracked for
    /// Dispose-time cleanup so the temp dir tear-down does not leave
    /// stray .partial files.
    /// </summary>
    private UploadReceiver NewUpload(TransferRegistry registry, string finalName, string parent)
    {
        var id = registry.AllocateId();
        var finalPath = Path.Combine(parent, finalName);
        var partialPath = $"{finalPath}.partial.{id}";
        var r = new UploadReceiver(id, partialPath, finalPath, expectedSize: 0,
            ctlForErrors: null, log: _log);
        _receivers.Add(r);
        registry.RegisterUpload(id, r);
        return r;
    }

    /// <summary>
    /// Lightweight <see cref="ITransferEntry"/> double for tests that only
    /// need to count Cancel invocations.
    /// </summary>
    private sealed class CountingEntry : ITransferEntry
    {
        public int CancelCount { get; private set; }
        public bool ThrowOnCancel { get; init; }

        public void Cancel()
        {
            CancelCount++;
            if (ThrowOnCancel)
                throw new InvalidOperationException("test: cancel threw");
        }
    }
}
