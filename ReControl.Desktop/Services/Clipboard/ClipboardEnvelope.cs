using System.Text.Json;
using ReControl.Desktop.Protocol.Generated;

namespace ReControl.Desktop.Services.Clipboard;

public static class ClipboardEnvelope
{
    public static string Serialize<TEnvelope>(TEnvelope envelope)
    {
        return JsonSerializer.Serialize(envelope, ClipboardConverter.Settings);
    }

    public static bool TryParseSet(JsonElement root, out ClipboardSetEnvelope? envelope)
    {
        envelope = null;
        if (!TryReadKind(root, out var kind) || kind != "set") return false;
        envelope = JsonSerializer.Deserialize<ClipboardSetEnvelope>(root.GetRawText(), ClipboardConverter.Settings);
        return envelope is not null;
    }

    public static bool TryParseRefused(JsonElement root, out ClipboardRefusedEnvelope? envelope)
    {
        envelope = null;
        if (!TryReadKind(root, out var kind) || kind != "refused") return false;
        envelope = JsonSerializer.Deserialize<ClipboardRefusedEnvelope>(root.GetRawText(), ClipboardConverter.Settings);
        return envelope is not null;
    }

    public static bool TryParseCapabilities(JsonElement root, out ClipboardCapabilitiesEnvelope? envelope)
    {
        envelope = null;
        if (!TryReadKind(root, out var kind) || kind != "capabilities") return false;
        envelope = JsonSerializer.Deserialize<ClipboardCapabilitiesEnvelope>(root.GetRawText(), ClipboardConverter.Settings);
        return envelope is not null;
    }

    private static bool TryReadKind(JsonElement root, out string kind)
    {
        kind = string.Empty;
        if (!root.TryGetProperty("kind", out var kindProp)) return false;
        kind = kindProp.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(kind);
    }
}
