using System;
using System.Text.Json;

namespace ReControl.Desktop.WebSocket.Protocol;

/// <summary>
/// Provides JSON serialization formatting for object payloads sent over ActionCable.
/// </summary>
public static class ActionCableFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, SerializerOptions);
    }

    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }
}
