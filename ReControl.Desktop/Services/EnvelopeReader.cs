using System.Text.Json;

namespace ReControl.Desktop.Services;

/// <summary>Reads the standard backend response envelope: { data, meta, error }.</summary>
public sealed record ApiErrorInfo(string Code, string Message);

public static class EnvelopeReader
{
    /// <summary>True when the envelope has a non-null `data`, yielding that element.</summary>
    public static bool TryGetData(JsonElement root, out JsonElement data)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var d)
            && d.ValueKind != JsonValueKind.Null)
        {
            data = d;
            return true;
        }
        data = default;
        return false;
    }

    /// <summary>Parses the envelope `error` object, or null when absent/null.</summary>
    public static ApiErrorInfo? TryGetError(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("error", out var e)
            && e.ValueKind == JsonValueKind.Object)
        {
            var code = e.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
            var message = e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            return new ApiErrorInfo(code, message);
        }
        return null;
    }
}
