using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ReControl.Desktop.Protocol.Generated;

namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Safe filesystem primitives. Every public method routes user-supplied paths
/// through PathCanonicalizer BEFORE touching the filesystem. Directory
/// enumeration never follows symlinked subdirectories out of the allowlist.
/// </summary>
public sealed class FileOperationsService
{
    private readonly PathCanonicalizer _canon;
    private readonly AllowlistService _allowlist;
    private readonly LogService _log;

    public FileOperationsService(PathCanonicalizer canon, AllowlistService allowlist, LogService log)
    {
        _canon = canon;
        _allowlist = allowlist;
        _log = log;
    }

    /// <summary>
    /// List the allowlisted roots themselves (for the sidebar). Non-existent roots
    /// are silently skipped per CONTEXT decision.
    /// </summary>
    public Task<IReadOnlyList<FileEntry>> ListRootsAsync()
    {
        var roots = _allowlist.GetRoots();
        _log.Info($"FileOps.ListRootsAsync: {roots.Count} root(s) [{string.Join(" | ", roots)}]");
        var result = new List<FileEntry>();
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var di = new DirectoryInfo(root);
                result.Add(new FileEntry(
                    Name: di.Name.Length > 0 ? di.Name : root,
                    Path: di.FullName,
                    IsDirectory: true,
                    SizeBytes: 0,
                    ModifiedUtc: di.LastWriteTimeUtc,
                    IsHidden: false));
            }
            catch (IOException) { /* unreadable root -- skip */ }
            catch (UnauthorizedAccessException) { /* unreadable root -- skip */ }
        }
        return Task.FromResult<IReadOnlyList<FileEntry>>(result);
    }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path)
    {
        _log.Info($"FileOps.ListAsync request path='{path}'");
        var canonical = _canon.Canonicalize(path);
        if (!Directory.Exists(canonical))
        {
            _log.Warning($"FileOps.ListAsync: canonical='{canonical}' is not a directory");
            throw new DirectoryNotFoundException($"Not a directory: {canonical}");
        }

        // Security-critical enumeration options. Equivalent of FollowDirectoryLinks = false:
        // AttributesToSkip includes ReparsePoint, so symlinked directories are not descended
        // into AND are not returned as entries. (The dedicated FollowDirectoryLinks property
        // is not present on this .NET version of EnumerationOptions; skipping ReparsePoint
        // is the documented way to get the same guarantee.)
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Device,
            MatchType = MatchType.Simple,
            ReturnSpecialDirectories = false,
            IgnoreInaccessible = true
        };

        var result = new List<FileEntry>();
        try
        {
            foreach (var entry in new DirectoryInfo(canonical).EnumerateFileSystemInfos("*", options))
            {
                bool isDir = (entry.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                long size = isDir ? 0 : ((FileInfo)entry).Length;
                bool isHidden =
                    (entry.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden
                    || entry.Name.StartsWith(".", StringComparison.Ordinal);
                result.Add(new FileEntry(entry.Name, entry.FullName, isDir, size, entry.LastWriteTimeUtc, isHidden));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // Read-side: cannot enumerate the directory.
            throw new PermissionReadException(canonical, ex);
        }
        return Task.FromResult<IReadOnlyList<FileEntry>>(result);
    }

    public Task MkdirAsync(string parentPath, string name)
    {
        var parent = _canon.Canonicalize(parentPath);
        FileNameValidator.Validate(name);
        // Plan 12-02: parent disappeared between canonicalize and the write.
        if (!Directory.Exists(parent))
            throw new DestinationGoneException(parent);
        var target = Path.Combine(parent, name);
        // Re-canonicalize the target so the combined path is verified in-allowlist.
        var canonTarget = _canon.Canonicalize(target);
        try
        {
            Directory.CreateDirectory(canonTarget);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PermissionWriteException(canonTarget, ex);
        }
        return Task.CompletedTask;
    }

    public Task RenameAsync(string path, string newName)
    {
        var src = _canon.Canonicalize(path);
        FileNameValidator.Validate(newName);
        var parent = Path.GetDirectoryName(src) ?? throw new IOException("No parent");
        var dst = Path.Combine(parent, newName);
        var canonDst = _canon.Canonicalize(dst);

        bool srcIsFile = File.Exists(src);
        bool srcIsDir = !srcIsFile && Directory.Exists(src);
        if (!srcIsFile && !srcIsDir)
            throw new SourceGoneException(src);
        // The parent directory is the destination parent for rename; if the
        // source vanished after canonicalize, the parent likely vanished too,
        // but be explicit.
        if (!Directory.Exists(parent))
            throw new DestinationGoneException(parent);

        try
        {
            if (srcIsFile) File.Move(src, canonDst, overwrite: false);
            else Directory.Move(src, canonDst);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PermissionWriteException(canonDst, ex);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path)
    {
        var canonical = _canon.Canonicalize(path);
        bool isFile = File.Exists(canonical);
        bool isDir = !isFile && Directory.Exists(canonical);
        if (!isFile && !isDir)
            throw new SourceGoneException(canonical);

        try
        {
            if (isFile) File.Delete(canonical);
            else Directory.Delete(canonical, recursive: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PermissionWriteException(canonical, ex);
        }
        return Task.CompletedTask;
    }

    public Task MoveAsync(string src, string dst, NameConflictMode mode = NameConflictMode.Fail)
    {
        var canonSrc = _canon.Canonicalize(src);
        var canonDst = _canon.Canonicalize(dst);

        bool srcIsFile = File.Exists(canonSrc);
        bool srcIsDir = !srcIsFile && Directory.Exists(canonSrc);
        if (!srcIsFile && !srcIsDir)
            throw new SourceGoneException(canonSrc);

        var dstParent = Path.GetDirectoryName(canonDst);
        if (string.IsNullOrEmpty(dstParent) || !Directory.Exists(dstParent))
            throw new DestinationGoneException(dstParent ?? canonDst);

        var resolvedDst = ResolveDestinationForMode(canonDst, mode, out bool skipped);
        if (skipped) return Task.CompletedTask;

        try
        {
            if (srcIsFile)
            {
                // overwrite:true is only safe when the caller asked for Replace.
                bool overwrite = mode == NameConflictMode.Replace;
                File.Move(canonSrc, resolvedDst, overwrite: overwrite);
            }
            else
            {
                // Directory.Move has no built-in overwrite. For Replace, delete
                // the existing destination tree first (only when the caller
                // explicitly asked for it -- never for Fail/Skip/KeepBoth).
                if (mode == NameConflictMode.Replace && Directory.Exists(resolvedDst))
                    Directory.Delete(resolvedDst, recursive: true);
                Directory.Move(canonSrc, resolvedDst);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PermissionWriteException(resolvedDst, ex);
        }
        return Task.CompletedTask;
    }

    public Task CopyAsync(string src, string dst, NameConflictMode mode = NameConflictMode.Fail)
    {
        var canonSrc = _canon.Canonicalize(src);
        var canonDst = _canon.Canonicalize(dst);

        bool srcIsFile = File.Exists(canonSrc);
        bool srcIsDir = !srcIsFile && Directory.Exists(canonSrc);
        if (!srcIsFile && !srcIsDir)
            throw new SourceGoneException(canonSrc);

        var dstParent = Path.GetDirectoryName(canonDst);
        if (string.IsNullOrEmpty(dstParent) || !Directory.Exists(dstParent))
            throw new DestinationGoneException(dstParent ?? canonDst);

        var resolvedDst = ResolveDestinationForMode(canonDst, mode, out bool skipped);
        if (skipped) return Task.CompletedTask;

        try
        {
            if (srcIsFile)
            {
                bool overwrite = mode == NameConflictMode.Replace;
                File.Copy(canonSrc, resolvedDst, overwrite: overwrite);
            }
            else
            {
                if (mode == NameConflictMode.Replace && Directory.Exists(resolvedDst))
                    Directory.Delete(resolvedDst, recursive: true);
                CopyDirectoryRecursive(canonSrc, resolvedDst);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PermissionWriteException(resolvedDst, ex);
        }
        return Task.CompletedTask;
    }

    private static void CopyDirectoryRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectoryRecursive(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    /// <summary>
    /// Apply the caller's NameConflictMode at the destination. Returns the path
    /// the operation should target. If the caller asked for Skip and the
    /// destination already exists, sets <paramref name="skipped"/>=true and
    /// the caller MUST short-circuit -- the returned path is meaningless in
    /// that case. Replace returns the original path so the operation can pass
    /// overwrite:true to the underlying move/copy primitive. KeepBoth resolves
    /// a unique sibling name via <see cref="ResolveUniqueName(string)"/>.
    /// </summary>
    public static string ResolveDestinationForMode(string canonDst, NameConflictMode mode, out bool skipped)
    {
        skipped = false;
        bool exists = File.Exists(canonDst) || Directory.Exists(canonDst);
        if (!exists) return canonDst;

        switch (mode)
        {
            case NameConflictMode.Fail:
                throw new NameConflictException(canonDst);
            case NameConflictMode.Skip:
                skipped = true;
                return canonDst;
            case NameConflictMode.Replace:
                return canonDst;
            case NameConflictMode.KeepBoth:
                return ResolveUniqueName(canonDst);
            default:
                throw new NameConflictException(canonDst);
        }
    }

    /// <summary>
    /// Resolve "/parent/name.ext" to the first non-existent
    /// "/parent/name (1).ext", "/parent/name (2).ext", ... sibling. Suffix is
    /// chosen DESKTOP-SIDE (never browser-side) so racing browser clients do
    /// not all pick the same number. Caps at (999) to avoid runaway loops on
    /// directories with thousands of conflicts; callers should fall back to
    /// Fail if the cap is hit.
    /// </summary>
    public static string ResolveUniqueName(string canonDst)
    {
        var parent = Path.GetDirectoryName(canonDst) ?? "";
        var stem = Path.GetFileNameWithoutExtension(canonDst);
        var ext = Path.GetExtension(canonDst);
        for (int i = 1; i <= 999; i++)
        {
            var candidate = Path.Combine(parent, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        // Pathological case: 999 siblings already exist. Surface as a
        // NAME_CONFLICT so the UI re-prompts; the user can manually rename.
        throw new NameConflictException(canonDst);
    }
}
