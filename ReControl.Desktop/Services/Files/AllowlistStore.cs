using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Serializes / deserializes a list of absolute root paths (the allowlist) to / from
/// a JSON file. Writes are atomic (temp file + replace) so a crash mid-save cannot
/// leave a half-written allowlist on disk.
/// </summary>
public sealed class AllowlistStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string JsonPath { get; }

    public AllowlistStore(string jsonPath) => JsonPath = jsonPath;

    /// <summary>
    /// Reads the allowlist from disk. Returns an empty list when the file does not
    /// exist or is malformed (the watcher will retrigger on the next save).
    /// </summary>
    public List<string> Load()
    {
        if (!File.Exists(JsonPath)) return new List<string>();
        try
        {
            using var fs = File.OpenRead(JsonPath);
            var doc = JsonSerializer.Deserialize<AllowlistDoc>(fs, JsonOpts);
            return doc?.Roots ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Writes the allowlist atomically: serialize to a .tmp file then replace/move
    /// into place. Creates the parent directory if missing.
    /// </summary>
    public void Save(IReadOnlyCollection<string> roots)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JsonPath)!);
        var tmp = JsonPath + ".tmp";
        using (var fs = File.Create(tmp))
        {
            JsonSerializer.Serialize(fs, new AllowlistDoc { Roots = new List<string>(roots) }, JsonOpts);
        }
        if (File.Exists(JsonPath)) File.Replace(tmp, JsonPath, null);
        else File.Move(tmp, JsonPath);
    }

    private sealed class AllowlistDoc
    {
        public List<string> Roots { get; set; } = new();
    }
}
