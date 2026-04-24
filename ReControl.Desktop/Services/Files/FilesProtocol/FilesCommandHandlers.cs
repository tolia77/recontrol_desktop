using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

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
///   - Delegates to <see cref="FileOperationsService"/>, which is the only
///     component that routes user-supplied paths through
///     <see cref="PathCanonicalizer"/> and therefore the allowlist boundary.
///   - Returns a plain anonymous record (or <c>null</c>) that serializes through
///     <see cref="System.Text.Json.JsonSerializerOptions"/> with camelCase naming.
///
/// Error behavior: exceptions thrown here (including
/// <see cref="AllowlistViolationException"/>, <see cref="InvalidFileNameException"/>,
/// <see cref="System.IO.FileNotFoundException"/>, etc.) fall through to the
/// per-exception catch blocks in <see cref="FilesCtlChannel.HandleAsync"/>,
/// which serialize the structured <c>{code, message, data}</c> error envelope.
/// <see cref="InvalidFileNameException.Reason"/> is lifted into
/// <c>error.data.reason</c> by the channel; this is the wire-level demo of
/// ALLOW-04 that Plan 09-05 exercises end-to-end.
/// </summary>
public static class FilesCommandHandlers
{
    public static IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>> Build(FileOperationsService ops)
    {
        if (ops is null) throw new ArgumentNullException(nameof(ops));

        return new Dictionary<string, Func<JsonElement, Task<object?>>>(StringComparer.Ordinal)
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
                await ops.MoveAsync(src, dst);
                return new { src, dst };
            },

            ["files.copy"] = async payload =>
            {
                var src = GetRequiredString(payload, "src");
                var dst = GetRequiredString(payload, "dst");
                await ops.CopyAsync(src, dst);
                return new { src, dst };
            },
        };
    }

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
