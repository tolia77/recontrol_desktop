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
    private readonly LogService _log;
    private readonly AllowlistStore _store;
    private readonly AllowlistWatcher _watcher;
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
        _roots = NormalizeRoots(_store.Load());
        _watcher = new AllowlistWatcher(jsonPath, Reload);
    }

    public IReadOnlyList<string> GetRoots() => _roots;

    private void Reload()
    {
        var next = NormalizeRoots(_store.Load());
        _roots = next;
        _log.Info($"AllowlistService: reloaded {next.Count} roots");
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
