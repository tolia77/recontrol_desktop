using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ReControl.Desktop.Protocol.Generated;
using SIPSorcery.Net;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// Factory that builds the <c>files-ctl</c> command-handler dictionary injected
/// into <see cref="FilesCtlChannel"/>.
///
/// Each handler:
///   - Parses its JSON payload using <see cref="JsonElement"/> accessors. The
///     strongly-typed codegen in <c>Protocol.Generated/FilesCtlTypes.cs</c> is
///     available, but the payload surface is small enough that ad-hoc
///     extraction is clearer.
///   - Delegates to <see cref="FileOperationsService"/> for the 7 directory /
///     metadata operations from Phase 9; for the 4 transfer-control commands
///     (upload.begin / upload.complete / download.begin / transfer.cancel)
///     it routes through <see cref="PathCanonicalizer"/> /
///     <see cref="FileNameValidator"/> directly and updates the
///     <see cref="TransferRegistry"/>.
///   - Returns a plain anonymous record (or <c>null</c>) that serializes through
///     <see cref="System.Text.Json.JsonSerializerOptions"/> with camelCase naming.
///
/// Error behavior: exceptions thrown here (including
/// <see cref="AllowlistViolationException"/>, <see cref="InvalidFileNameException"/>,
/// <see cref="System.IO.FileNotFoundException"/>, <see cref="TransferNotFoundException"/>,
/// etc.) fall through to the per-exception catch blocks in
/// <see cref="FilesCtlChannel.HandleAsync"/>, which serialize the structured
/// <c>{code, message, data}</c> error envelope.
/// </summary>
public static class FilesCommandHandlers
{
    /// <summary>
    /// Phase-9 baseline overload: builds only the 7 directory / metadata
    /// handlers. Kept for tests / harnesses that do not need the transfer
    /// engine; production wiring uses the full overload below.
    /// </summary>
    public static IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>> Build(FileOperationsService ops)
    {
        if (ops is null) throw new ArgumentNullException(nameof(ops));
        return BuildBaseHandlers(ops);
    }

    /// <summary>
    /// Phase-11 production overload: builds all 11 handlers.
    /// <paramref name="filesDataAccessor"/> and <paramref name="filesCtlAccessor"/>
    /// are CLOSURES that defer the channel lookup until the handler runs --
    /// at construction time the files-ctl / files-data RTCDataChannels do
    /// not yet exist (they are created from <c>WebRtcService.HandleOfferAsync</c>
    /// when the SDP offer arrives). The closures resolve through
    /// <c>WebRtcService</c>'s public properties, so the handler always sees
    /// the currently-live channel.
    /// </summary>
    public static IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>> Build(
        FileOperationsService ops,
        TransferRegistry registry,
        PathCanonicalizer canonicalizer,
        AllowlistService allowlist,
        Func<RTCDataChannel?> filesDataAccessor,
        Func<FilesCtlChannel?> filesCtlAccessor,
        LogService log)
    {
        if (ops is null) throw new ArgumentNullException(nameof(ops));
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (canonicalizer is null) throw new ArgumentNullException(nameof(canonicalizer));
        if (allowlist is null) throw new ArgumentNullException(nameof(allowlist));
        if (filesDataAccessor is null) throw new ArgumentNullException(nameof(filesDataAccessor));
        if (filesCtlAccessor is null) throw new ArgumentNullException(nameof(filesCtlAccessor));
        if (log is null) throw new ArgumentNullException(nameof(log));

        var handlers = new Dictionary<string, Func<JsonElement, Task<object?>>>(
            BuildBaseHandlers(ops), StringComparer.Ordinal);

        // ----- files.upload.begin -----
        handlers["files.upload.begin"] = payload =>
        {
            var parentPath = GetRequiredString(payload, "parentPath");
            var name = GetRequiredString(payload, "name");
            var size = GetRequiredLong(payload, "size");
            var mode = GetOptionalConflictMode(payload);
            if (size < 0) throw new ArgumentException("size must be non-negative");

            var canonicalParent = canonicalizer.Canonicalize(parentPath);
            // Plan 12-02: parent disappeared between canonicalize and the
            // upload reservation -> DESTINATION_GONE.
            if (!Directory.Exists(canonicalParent))
                throw new DestinationGoneException(canonicalParent);
            FileNameValidator.Validate(name);

            var finalPath = Path.Combine(canonicalParent, name);
            // Plan 12-02: name-conflict resolution at the destination. KeepBoth
            // resolves a unique sibling DESKTOP-SIDE (browser never guesses).
            // Skip short-circuits with a synthetic success payload. Replace
            // proceeds to write into the same finalPath; the .partial -> final
            // rename below will overwrite via File.Move(overwrite:true) only
            // when mode == Replace.
            if (File.Exists(finalPath) || Directory.Exists(finalPath))
            {
                switch (mode)
                {
                    case NameConflictMode.Fail:
                        throw new NameConflictException(finalPath);
                    case NameConflictMode.Skip:
                        // No transfer reserved; the browser receives a synthetic
                        // success and never sends bytes for this id.
                        return Task.FromResult<object?>(new { skipped = true, path = finalPath });
                    case NameConflictMode.KeepBoth:
                        finalPath = FileOperationsService.ResolveUniqueName(finalPath);
                        break;
                    case NameConflictMode.Replace:
                        // Fall through; the receiver will overwrite on Complete.
                        break;
                }
            }

            // 64 MiB safety margin (RESEARCH Open Question 5). Maps to
            // FilesErrorCode.DISK_FULL via the FilesCtlChannel filter on
            // "DISK_FULL" prefix.
            const long DiskFullMargin = 64L << 20;
            try
            {
                var rootName = Path.GetPathRoot(canonicalParent);
                if (!string.IsNullOrEmpty(rootName))
                {
                    var drive = new DriveInfo(rootName);
                    if (drive.AvailableFreeSpace < size + DiskFullMargin)
                    {
                        throw new IOException(
                            $"DISK_FULL: insufficient free space ({drive.AvailableFreeSpace} bytes available, need {size + DiskFullMargin})");
                    }
                }
            }
            catch (ArgumentException) { /* drive-info couldn't parse the path -- skip the pre-flight, the OS will catch it on write */ }
            catch (DriveNotFoundException) { /* ditto */ }

            var id = registry.AllocateId();
            // Pitfall 9: .partial is sibling of finalPath -> File.Move atomic.
            var partialPath = $"{finalPath}.partial.{id}";

            UploadReceiver? receiver = null;
            try
            {
                // Plan 12-02: when the caller asked for Replace and the final
                // path already existed at the conflict check above, allow the
                // .partial -> final rename to overwrite. For Fail / Skip /
                // KeepBoth the resolved finalPath is fresh, so overwrite stays
                // false (the default).
                bool allowOverwrite = mode == NameConflictMode.Replace;
                receiver = new UploadReceiver(
                    id, partialPath, finalPath, size, filesCtlAccessor(), log, allowOverwrite);
                registry.RegisterUpload(id, receiver);
            }
            catch
            {
                // If the FileStream open succeeded but RegisterUpload threw
                // (extremely unlikely -- it is a dictionary insert), the
                // .partial would leak. Dispose the receiver to delete it.
                receiver?.Dispose();
                throw;
            }

            // Wire shape per FilesUploadBeginResponse: { transferId, partialPath }.
            // The actual final path (which may differ from the requested name
            // under KeepBoth) is returned later from files.upload.complete.
            return Task.FromResult<object?>(new { transferId = id, partialPath });
        };

        // ----- files.upload.complete -----
        handlers["files.upload.complete"] = async payload =>
        {
            var transferId = (uint)GetRequiredLong(payload, "transferId");
            var expectedBytes = GetRequiredLong(payload, "expectedBytes");

            if (!registry.TryGet(transferId, out var entry) || entry is not UploadReceiver up)
                throw new TransferNotFoundException(transferId);

            try
            {
                var path = await up.CompleteAsync(expectedBytes);
                registry.Remove(transferId);
                return new { path };
            }
            catch
            {
                // CompleteAsync already deleted the .partial on byte-count
                // mismatch; on File.Move failure the .partial is still on
                // disk -- be defensive and force a Cancel so we leave
                // nothing behind for the next sweep.
                up.Cancel();
                registry.Remove(transferId);
                throw;
            }
        };

        // ----- files.download.begin -----
        handlers["files.download.begin"] = payload =>
        {
            var path = GetRequiredString(payload, "path");
            var canonicalPath = canonicalizer.Canonicalize(path);

            var info = new FileInfo(canonicalPath);
            if (!info.Exists)
                // Plan 12-02: read-side disappearance -> SOURCE_GONE so the UI
                // can prompt "refresh" instead of generic NOT_FOUND.
                throw new SourceGoneException(canonicalPath);
            if ((info.Attributes & FileAttributes.Directory) != 0)
                throw new IOException($"path is a directory: {canonicalPath}");

            var id = registry.AllocateId();

            var dataChannel = filesDataAccessor();
            var ctlChannel = filesCtlAccessor();
            if (dataChannel is null) throw new InvalidOperationException("files-data channel not available");
            if (ctlChannel is null) throw new InvalidOperationException("files-ctl channel not available");

            var sender = new DownloadSender(
                id, canonicalPath, info.Length, dataChannel, ctlChannel, log, registry);
            registry.RegisterDownload(id, sender);

            // Build the response object FIRST. Then kick the send loop on a
            // background task. The handler returns immediately; the
            // FilesCtlChannel.HandleAsync await site serializes SendSuccess
            // before any chunk lands on files-data (the begin response and
            // chunks ride different SCTP streams; the simple form is correct
            // in practice -- see plan Task 1E note for the safer-but-slower
            // 50ms-delay fallback if 11-05 testing reveals an ordering bug).
            var resp = new { transferId = id, size = info.Length, name = info.Name };
            _ = Task.Run(() => sender.RunAsync());
            return Task.FromResult<object?>(resp);
        };

        // ----- files.transfer.cancel -----
        handlers["files.transfer.cancel"] = payload =>
        {
            var transferId = (uint)GetRequiredLong(payload, "transferId");
            // reason is REQUIRED on the wire (Plan 11-01 schema) but not
            // load-bearing for cancel logic -- read it for logging only.
            var reason = payload.ValueKind == JsonValueKind.Object &&
                         payload.TryGetProperty("reason", out var r)
                             ? (r.GetString() ?? "user")
                             : "user";

            if (registry.TryGet(transferId, out var entry))
            {
                try { entry.Cancel(); } catch { /* best effort */ }
                registry.Remove(transferId);
                log.Info($"transfer.cancel: id={transferId} reason={reason}");
            }
            else
            {
                // Cancel-after-complete race: the entry is already gone.
                // Per RESEARCH this is treated as a successful empty
                // response, NOT TRANSFER_NOT_FOUND -- the browser issued
                // the cancel and has already cleaned up its local state;
                // we just ack "nothing to cancel".
                log.Info($"transfer.cancel: id={transferId} not in registry (already complete) reason={reason}");
            }

            return Task.FromResult<object?>(new { });
        };

        return handlers;
    }

    private static Dictionary<string, Func<JsonElement, Task<object?>>> BuildBaseHandlers(FileOperationsService ops)
        => new(StringComparer.Ordinal)
        {
            ["files.listRoots"] = async _ =>
            {
                var roots = await ops.ListRootsAsync();
                return new { roots = roots.Select(Project).ToList() };
            },

            ["files.list"] = async payload =>
            {
                var path = GetRequiredString(payload, "path");
                var entries = await ops.ListAsync(path);
                return new { path, entries = entries.Select(Project).ToList() };
            },

            ["files.mkdir"] = async payload =>
            {
                var parentPath = GetRequiredString(payload, "parentPath");
                var name = GetRequiredString(payload, "name");
                await ops.MkdirAsync(parentPath, name);
                return new { path = System.IO.Path.Combine(parentPath, name) };
            },

            ["files.rename"] = async payload =>
            {
                var path = GetRequiredString(payload, "path");
                var newName = GetRequiredString(payload, "newName");
                await ops.RenameAsync(path, newName);
                var parent = System.IO.Path.GetDirectoryName(path);
                return new { path = parent is null ? newName : System.IO.Path.Combine(parent, newName) };
            },

            ["files.delete"] = async payload =>
            {
                var path = GetRequiredString(payload, "path");
                await ops.DeleteAsync(path);
                return (object?)null;
            },

            ["files.move"] = async payload =>
            {
                var src = GetRequiredString(payload, "src");
                var dst = GetRequiredString(payload, "dst");
                var mode = GetOptionalConflictMode(payload);
                // Plan 12-02: KeepBoth resolves a unique dst desktop-side; the
                // browser must learn the actual final path so it can refresh
                // the row. Skip returns a synthetic success { skipped: true }.
                bool destExisted = System.IO.File.Exists(dst) || System.IO.Directory.Exists(dst);
                if (mode == NameConflictMode.Skip && destExisted)
                {
                    await ops.MoveAsync(src, dst, mode); // no-op (skipped)
                    return new { src, dst, skipped = true };
                }
                var resolvedDst = mode == NameConflictMode.KeepBoth && destExisted
                    ? FileOperationsService.ResolveUniqueName(dst)
                    : dst;
                await ops.MoveAsync(src, dst, mode);
                return new { src, dst = resolvedDst };
            },

            ["files.copy"] = async payload =>
            {
                var src = GetRequiredString(payload, "src");
                var dst = GetRequiredString(payload, "dst");
                var mode = GetOptionalConflictMode(payload);
                bool destExisted = System.IO.File.Exists(dst) || System.IO.Directory.Exists(dst);
                if (mode == NameConflictMode.Skip && destExisted)
                {
                    await ops.CopyAsync(src, dst, mode); // no-op (skipped)
                    return new { src, dst, skipped = true };
                }
                var resolvedDst = mode == NameConflictMode.KeepBoth && destExisted
                    ? FileOperationsService.ResolveUniqueName(dst)
                    : dst;
                await ops.CopyAsync(src, dst, mode);
                return new { src, dst = resolvedDst };
            },
        };

    private static string GetRequiredString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"payload must be an object; got {payload.ValueKind}");
        if (!payload.TryGetProperty(propertyName, out var prop))
            throw new ArgumentException($"required property '{propertyName}' missing from payload");
        var s = prop.GetString();
        if (string.IsNullOrEmpty(s))
            throw new ArgumentException($"required property '{propertyName}' must be a non-empty string");
        return s;
    }

    private static long GetRequiredLong(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"payload must be an object; got {payload.ValueKind}");
        if (!payload.TryGetProperty(propertyName, out var prop))
            throw new ArgumentException($"required property '{propertyName}' missing from payload");
        if (prop.ValueKind != JsonValueKind.Number)
            throw new ArgumentException($"required property '{propertyName}' must be a number");
        return prop.GetInt64();
    }

    /// <summary>
    /// Plan 12-02: parse the optional <c>mode</c> field on upload.begin / move / copy
    /// payloads. Default is <see cref="NameConflictMode.Fail"/> when the field is
    /// missing, null, or unrecognized -- the must-have rule is "default fail unless
    /// caller passes explicit mode". Unknown strings fall through to Fail rather
    /// than throw so a Phase-13 client adding new modes does not crash older
    /// desktops; the strict-mode behavior matches the schema's enum semantics.
    /// </summary>
    private static NameConflictMode GetOptionalConflictMode(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return NameConflictMode.Fail;
        if (!payload.TryGetProperty("mode", out var prop)) return NameConflictMode.Fail;
        if (prop.ValueKind != JsonValueKind.String) return NameConflictMode.Fail;
        var s = prop.GetString();
        return s switch
        {
            "fail" => NameConflictMode.Fail,
            "replace" => NameConflictMode.Replace,
            "skip" => NameConflictMode.Skip,
            "keepBoth" => NameConflictMode.KeepBoth,
            _ => NameConflictMode.Fail,
        };
    }

    private static object Project(FileEntry e) => new
    {
        name = e.Name,
        path = e.Path,
        isDirectory = e.IsDirectory,
        sizeBytes = e.SizeBytes,
        // ISO-8601 round-trip "O" format, e.g. 2026-04-24T08:25:31.1234567Z -- matches
        // the schema's ISO-8601 / RFC-3339 UTC shape in Protocol.Generated/FileEntry.
        modifiedUtc = e.ModifiedUtc.ToString("O"),
        isHidden = e.IsHidden
    };
}
