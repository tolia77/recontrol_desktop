using System;
using System.Collections.Generic;
using System.IO;

namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Public facade over the allowlist on-disk store + hot-reload watcher.
/// - On first run (file missing), seeds the user's Documents and Downloads folders.
/// - GetRoots() returns a thread-safe snapshot of canonicalized absolute root paths.
/// - RootsChanged fires after the watcher reloads the file from disk.
/// </summary>
public sealed class AllowlistService : IDisposable
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly LogService _log;
    private readonly AllowlistStore _store;
    private readonly AllowlistWatcher _watcher;
    private readonly object _gate = new();
    private volatile IReadOnlyList<string> _roots = Array.Empty<string>();

    /// <summary>Fired (on a threadpool thread) after the watcher detects a change and roots are reloaded.</summary>
    public event Action? RootsChanged;

    public AllowlistService(LogService log, string? jsonPathOverride = null)
    {
        _log = log;
        var jsonPath = jsonPathOverride ?? DefaultJsonPath();
        _store = new AllowlistStore(jsonPath);

        if (!File.Exists(jsonPath))
        {
            _store.Save(SeedDefaults());
            _log.Info($"AllowlistService: seeded defaults at {jsonPath}");
        }
        var raw = _store.Load();
        _roots = NormalizeRoots(raw);
        _log.Info($"AllowlistService: loaded {_roots.Count} root(s) raw=[{string.Join(" | ", raw)}] normalized=[{string.Join(" | ", _roots)}]");
        _watcher = new AllowlistWatcher(jsonPath, Reload);
    }

    public IReadOnlyList<string> GetRoots() => _roots;

    public AllowlistUpdateResult SaveRoots(IEnumerable<string> roots)
    {
        if (roots is null)
            return AllowlistUpdateResult.Fail("path is invalid or inaccessible");

        var normalized = new List<string>();
        foreach (var root in roots)
        {
            if (!TryCanonicalizeRoot(root, out var canonical))
                return AllowlistUpdateResult.Fail("path is invalid or inaccessible");
            normalized.Add(canonical);
        }

        var validation = ValidateRoots(normalized);
        if (!validation.IsSuccess) return validation;

        lock (_gate)
        {
            _store.Save(normalized);
            _roots = normalized.AsReadOnly();
        }

        RootsChanged?.Invoke();
        return AllowlistUpdateResult.Success();
    }

    public AllowlistUpdateResult AddRoot(string root)
    {
        if (!TryCanonicalizeRoot(root, out var canonical))
            return AllowlistUpdateResult.Fail("path is invalid or inaccessible");

        var current = GetRoots();
        var next = new List<string>(current.Count + 1);
        next.AddRange(current);
        next.Add(canonical);
        return SaveRoots(next);
    }

    public AllowlistUpdateResult RemoveRoot(string root)
    {
        if (!TryCanonicalizeRoot(root, out var canonical))
            return AllowlistUpdateResult.Fail("path is invalid or inaccessible");

        var current = GetRoots();
        var next = new List<string>(current.Count);
        var removed = false;

        foreach (var existing in current)
        {
            if (!removed && existing.Equals(canonical, PathComparison))
            {
                removed = true;
                continue;
            }

            next.Add(existing);
        }

        if (!removed) return AllowlistUpdateResult.Fail("folder is not currently shared");
        return SaveRoots(next);
    }

    private void Reload()
    {
        List<string> next;
        List<string> raw;
        lock (_gate)
        {
            raw = new List<string>(_store.Load());
            next = NormalizeRoots(raw);
            _roots = next;
        }
        _log.Info($"AllowlistService: reloaded {next.Count} root(s) raw=[{string.Join(" | ", raw)}] normalized=[{string.Join(" | ", next)}]");
        RootsChanged?.Invoke();
    }

    /// <summary>
    /// Canonicalize roots exactly ONCE at load time so PathCanonicalizer can do
    /// cheap StringComparison on the results. Non-existent roots are KEPT (per
    /// CONTEXT.md: non-existent allowlist paths are accepted at add time; they
    /// are simply skipped at listing time).
    /// </summary>
    private static List<string> NormalizeRoots(IEnumerable<string> raw)
    {
        var result = new List<string>();
        foreach (var r in raw)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            try { result.Add(Path.GetFullPath(r)); }
            catch { /* malformed -- drop */ }
        }
        return result;
    }

    private static bool IsNestedPath(string candidate, string root)
    {
        if (candidate.Equals(root, PathComparison)) return false;
        var withSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(withSeparator, PathComparison);
    }

    private static AllowlistUpdateResult ValidateRoots(IReadOnlyList<string> roots)
    {
        for (var i = 0; i < roots.Count; i++)
        {
            var left = roots[i];
            for (var j = i + 1; j < roots.Count; j++)
            {
                var right = roots[j];
                if (left.Equals(right, PathComparison))
                    return AllowlistUpdateResult.Fail("already shared");

                if (IsNestedPath(left, right) || IsNestedPath(right, left))
                    return AllowlistUpdateResult.Fail("nested under an existing shared folder");
            }
        }

        return AllowlistUpdateResult.Success();
    }

    private static bool TryCanonicalizeRoot(string rawRoot, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(rawRoot)) return false;

        try
        {
            canonical = Path.GetFullPath(rawRoot);
            return !string.IsNullOrWhiteSpace(canonical);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyCollection<string> SeedDefaults()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var downloads = GetDownloads();
        var seed = new List<string>();
        if (!string.IsNullOrEmpty(docs)) seed.Add(docs);
        if (!string.IsNullOrEmpty(downloads)) seed.Add(downloads);
        return seed;
    }

    private static string? GetDownloads()
    {
        // SpecialFolder.Downloads is not present on every .NET target; fall back to
        // UserProfile/Downloads which is correct on both Windows and Linux.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return null;
        return Path.Combine(home, "Downloads");
    }

    public static string DefaultJsonPath()
    {
        var appData = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ReControl", "files", "allowlist.json");
    }

    public void Dispose() => _watcher.Dispose();
}

public readonly record struct AllowlistUpdateResult(bool IsSuccess, string? Error)
{
    public static AllowlistUpdateResult Success() => new(true, null);
    public static AllowlistUpdateResult Fail(string error) => new(false, error);
}
