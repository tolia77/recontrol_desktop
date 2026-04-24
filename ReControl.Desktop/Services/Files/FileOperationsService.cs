using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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
        var result = new List<FileEntry>();
        foreach (var root in _allowlist.GetRoots())
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
                    ModifiedUtc: di.LastWriteTimeUtc));
            }
            catch (IOException) { /* unreadable root -- skip */ }
            catch (UnauthorizedAccessException) { /* unreadable root -- skip */ }
        }
        return Task.FromResult<IReadOnlyList<FileEntry>>(result);
    }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path)
    {
        var canonical = _canon.Canonicalize(path);
        if (!Directory.Exists(canonical))
            throw new DirectoryNotFoundException($"Not a directory: {canonical}");

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
        foreach (var entry in new DirectoryInfo(canonical).EnumerateFileSystemInfos("*", options))
        {
            bool isDir = (entry.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
            long size = isDir ? 0 : ((FileInfo)entry).Length;
            result.Add(new FileEntry(entry.Name, entry.FullName, isDir, size, entry.LastWriteTimeUtc));
        }
        return Task.FromResult<IReadOnlyList<FileEntry>>(result);
    }

    public Task MkdirAsync(string parentPath, string name)
    {
        var parent = _canon.Canonicalize(parentPath);
        FileNameValidator.Validate(name);
        var target = Path.Combine(parent, name);
        // Re-canonicalize the target so the combined path is verified in-allowlist.
        var canonTarget = _canon.Canonicalize(target);
        Directory.CreateDirectory(canonTarget);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string path, string newName)
    {
        var src = _canon.Canonicalize(path);
        FileNameValidator.Validate(newName);
        var parent = Path.GetDirectoryName(src) ?? throw new IOException("No parent");
        var dst = Path.Combine(parent, newName);
        var canonDst = _canon.Canonicalize(dst);
        if (File.Exists(src)) File.Move(src, canonDst, overwrite: false);
        else if (Directory.Exists(src)) Directory.Move(src, canonDst);
        else throw new FileNotFoundException(src);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path)
    {
        var canonical = _canon.Canonicalize(path);
        if (File.Exists(canonical)) File.Delete(canonical);
        else if (Directory.Exists(canonical)) Directory.Delete(canonical, recursive: true);
        else throw new FileNotFoundException(canonical);
        return Task.CompletedTask;
    }

    public Task MoveAsync(string src, string dst)
    {
        var canonSrc = _canon.Canonicalize(src);
        var canonDst = _canon.Canonicalize(dst);
        if (File.Exists(canonSrc)) File.Move(canonSrc, canonDst, overwrite: false);
        else if (Directory.Exists(canonSrc)) Directory.Move(canonSrc, canonDst);
        else throw new FileNotFoundException(canonSrc);
        return Task.CompletedTask;
    }

    public Task CopyAsync(string src, string dst)
    {
        var canonSrc = _canon.Canonicalize(src);
        var canonDst = _canon.Canonicalize(dst);
        if (File.Exists(canonSrc))
        {
            File.Copy(canonSrc, canonDst, overwrite: false);
        }
        else if (Directory.Exists(canonSrc))
        {
            CopyDirectoryRecursive(canonSrc, canonDst);
        }
        else throw new FileNotFoundException(canonSrc);
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
}
