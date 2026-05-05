using System.IO;
using System.Text.Json;

namespace ReControl.Desktop.Services.Clipboard;

public sealed class ClipboardSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private ClipboardSettings _lastGood = ClipboardSettings.Defaults;

    public string JsonPath { get; }

    public ClipboardSettingsStore(string jsonPath) => JsonPath = jsonPath;

    public ClipboardSettings Load()
    {
        if (!File.Exists(JsonPath))
        {
            _lastGood = ClipboardSettings.Defaults;
            return Clone(_lastGood);
        }

        try
        {
            using var fs = File.OpenRead(JsonPath);
            var loaded = JsonSerializer.Deserialize<ClipboardSettings>(fs, JsonOpts) ?? ClipboardSettings.Defaults;
            _lastGood = Normalize(loaded);
            return Clone(_lastGood);
        }
        catch (JsonException)
        {
            return Clone(_lastGood);
        }
    }

    public void Save(ClipboardSettings settings)
    {
        var normalized = Normalize(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(JsonPath)!);
        var tmp = JsonPath + ".tmp";

        using (var fs = File.Create(tmp))
        {
            JsonSerializer.Serialize(fs, normalized, JsonOpts);
        }

        if (File.Exists(JsonPath)) File.Replace(tmp, JsonPath, null);
        else File.Move(tmp, JsonPath);

        _lastGood = Clone(normalized);
    }

    private static ClipboardSettings Normalize(ClipboardSettings settings)
    {
        return new ClipboardSettings
        {
            Version = settings.Version <= 0 ? 1 : settings.Version,
            Master = settings.Master,
            AllowOutbound = settings.AllowOutbound,
            AllowInbound = settings.AllowInbound
        };
    }

    private static ClipboardSettings Clone(ClipboardSettings settings)
    {
        return new ClipboardSettings
        {
            Version = settings.Version,
            Master = settings.Master,
            AllowOutbound = settings.AllowOutbound,
            AllowInbound = settings.AllowInbound
        };
    }
}
