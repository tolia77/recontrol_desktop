using System;
using System.IO;

namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Resolves a caller-supplied path to an absolute, symlink-resolved canonical form
/// and verifies that it lies inside some allowlisted root. Every public filesystem
/// operation must route user input through Canonicalize BEFORE touching the FS.
/// </summary>
public sealed class PathCanonicalizer
{
    private readonly AllowlistService _allowlist;

    // NTFS (Windows) and APFS (macOS, default) are case-insensitive; Linux filesystems
    // are case-sensitive. The containment check must match the OS rules.
    private static readonly StringComparison Cmp =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public PathCanonicalizer(AllowlistService allowlist) => _allowlist = allowlist;

    /// <summary>
    /// Returns the absolute, symlink-resolved canonical path if it lies inside any
    /// allowlisted root. Throws AllowlistViolationException otherwise. basePath, if
    /// supplied, is used to resolve a relative userInput.
    /// </summary>
    public string Canonicalize(string userInput, string? basePath = null)
    {
        if (string.IsNullOrWhiteSpace(userInput))
            throw new AllowlistViolationException(userInput ?? "", "empty");

        string absolute;
        try
        {
            absolute = basePath is null
                ? Path.GetFullPath(userInput)
                : Path.GetFullPath(userInput, basePath);
        }
        catch (Exception)
        {
            throw new AllowlistViolationException(userInput, "malformed");
        }

        // Resolve symlinks: a path can pass lexical containment while pointing at
        // /etc/passwd via a symlink. ResolveLinkTarget follows chains when
        // returnFinalTarget:true.
        string resolved = absolute;
        try
        {
            FileSystemInfo info = Directory.Exists(absolute)
                ? new DirectoryInfo(absolute)
                : new FileInfo(absolute);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null) resolved = target.FullName;
        }
        catch (IOException)
        {
            // Path doesn't exist yet (e.g., mkdir target) -- fine, skip symlink step.
        }

        var roots = _allowlist.GetRoots();
        foreach (var root in roots)
        {
            var canonRoot = Path.GetFullPath(root);
            var rootWithSep = canonRoot.EndsWith(Path.DirectorySeparatorChar)
                ? canonRoot
                : canonRoot + Path.DirectorySeparatorChar;
            if (resolved.Equals(canonRoot, Cmp) || resolved.StartsWith(rootWithSep, Cmp))
                return resolved;
        }

        var cause = ClassifyFailure(userInput, absolute, resolved);
        throw new AllowlistViolationException(resolved, cause);
    }

    private static string ClassifyFailure(string userInput, string absolute, string resolved)
    {
        if (!absolute.Equals(resolved, Cmp)) return "symlink_escape";
        if (userInput.Contains("..")) return "traversal";
        if (Path.IsPathFullyQualified(userInput)) return "absolute_smuggling";
        return "outside_roots";
    }
}
