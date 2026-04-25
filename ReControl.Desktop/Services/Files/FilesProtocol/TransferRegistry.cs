using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Process-wide registry of in-flight file transfers (uploads + downloads),
/// keyed by a u32 transferId allocated via <see cref="Interlocked.Increment(ref uint)"/>.
///
/// Lifecycle:
///   - <see cref="AllocateId"/> mints a fresh id; collisions across reconnects
///     are impossible because the counter is process-wide and monotonic.
///   - <see cref="RegisterUpload"/> / <see cref="RegisterDownload"/> store a
///     concrete <see cref="ITransferEntry"/> implementation.
///   - <see cref="TryGet"/> / <see cref="Remove"/> drive normal completion
///     and cancel paths.
///   - <see cref="CancelAll"/> is invoked from
///     <c>WebRtcService.CleanupPeerConnection</c> on every tear-down path
///     (Stop / Disconnect / page-refresh) so .partial files do not leak.
///
/// The registry also tracks the parent directories of every upload's
/// <c>.partial</c> file (<see cref="KnownParentDirs"/>) so that
/// <see cref="PartialFileSweeper"/> only walks the dirs the desktop has
/// touched -- keeping the 60s sweep tick cheap and bounded.
/// <see cref="ActivePartialPaths"/> lets the sweeper skip files that are
/// the active write-target of a still-running upload.
///
/// Thread-safety: all members are safe for concurrent use; <see cref="_entries"/>
/// is a <see cref="ConcurrentDictionary{TKey, TValue}"/>, the id counter is
/// updated via Interlocked, and <see cref="_knownParentDirs"/> is also a
/// concurrent dictionary used as a thread-safe set.
/// </summary>
public sealed class TransferRegistry
{
    private readonly ConcurrentDictionary<uint, ITransferEntry> _entries = new();
    private readonly ConcurrentDictionary<string, byte> _knownParentDirs =
        new(StringComparer.Ordinal);
    private uint _nextId;

    /// <summary>
    /// Atomically allocates a fresh u32 transferId. First call returns 1.
    /// Wraparound after 4_294_967_295 ids is a non-issue for any realistic
    /// session (would require ~4.3 billion individual transfers in one
    /// process lifetime).
    /// </summary>
    public uint AllocateId() => Interlocked.Increment(ref _nextId);

    public void RegisterUpload(uint id, UploadReceiver receiver)
    {
        if (receiver is null) throw new ArgumentNullException(nameof(receiver));
        _entries[id] = receiver;
        var parent = Path.GetDirectoryName(receiver.PartialPath);
        if (!string.IsNullOrEmpty(parent))
            _knownParentDirs[parent] = 0;
    }

    public void RegisterDownload(uint id, DownloadSender sender)
    {
        if (sender is null) throw new ArgumentNullException(nameof(sender));
        _entries[id] = sender;
    }

    /// <summary>
    /// Low-level entry registration used primarily by unit tests that need
    /// to insert a lightweight <see cref="ITransferEntry"/> double without
    /// constructing a real <see cref="UploadReceiver"/> /
    /// <see cref="DownloadSender"/> (which would require a FileStream or
    /// RTCDataChannel). Production code paths should prefer
    /// <see cref="RegisterUpload"/> / <see cref="RegisterDownload"/>.
    /// </summary>
    public void RegisterEntry(uint id, ITransferEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        _entries[id] = entry;
    }

    public bool TryGet(uint id, out ITransferEntry entry)
        => _entries.TryGetValue(id, out entry!);

    public bool Remove(uint id) => _entries.TryRemove(id, out _);

    /// <summary>
    /// Cancel every in-flight transfer and empty the registry. Each
    /// <see cref="ITransferEntry.Cancel"/> call is wrapped in try/catch so a
    /// throwing entry does NOT prevent the remaining entries from being
    /// cancelled. Invoked from <c>WebRtcService.CleanupPeerConnection</c>
    /// (the single tear-down hook covering Stop / Disconnect / page-refresh).
    /// </summary>
    public void CancelAll()
    {
        foreach (var kv in _entries)
        {
            try { kv.Value.Cancel(); }
            catch { /* swallow: foreach must finish */ }
        }
        _entries.Clear();
    }

    /// <summary>
    /// Snapshot of parent directories of currently-tracked-or-recently-aborted
    /// .partial files. <see cref="PartialFileSweeper"/> only enumerates these
    /// directories on its 60s tick; entries are NOT removed when an upload
    /// completes (the sweeper is then a no-op for that dir, which is fine).
    /// </summary>
    public IReadOnlyCollection<string> KnownParentDirs => _knownParentDirs.Keys.ToArray();

    /// <summary>
    /// Snapshot of currently-active upload .partial paths. Used by the sweeper
    /// to AVOID deleting a .partial file owned by an in-flight upload (the
    /// upload may legitimately be quiet for &gt; 5 minutes during a stall).
    /// </summary>
    public IReadOnlyCollection<string> ActivePartialPaths
        => _entries.Values.OfType<UploadReceiver>().Select(u => u.PartialPath).ToArray();
}

/// <summary>
/// Common cancellation contract for an entry stored in the
/// <see cref="TransferRegistry"/>. Implemented by <see cref="UploadReceiver"/>
/// (close stream + delete .partial) and <see cref="DownloadSender"/> (cancel CTS,
/// dispose FileStream).
///
/// <see cref="Cancel"/> MUST be idempotent: it may be invoked from the
/// <c>files.transfer.cancel</c> handler, from <see cref="TransferRegistry.CancelAll"/>
/// during pc.close, or implicitly from a write-error path inside the
/// implementation itself.
/// </summary>
public interface ITransferEntry
{
    void Cancel();
}
