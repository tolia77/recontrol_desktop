using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReControl.Desktop.Models;

namespace ReControl.Desktop.Commands;

/// <summary>
/// JSON parsing utilities for incoming command requests and outgoing responses.
/// Ported from WPF CommandJsonParser.
/// </summary>
public class CommandJsonParser
{
    private readonly JsonSerializerOptions _jsonOptions;

    public CommandJsonParser()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    /// <summary>
    /// Parses a raw JSON string into a BaseRequest.
    /// </summary>
    public BaseRequest ParseRequest(string json)
    {
        var req = JsonSerializer.Deserialize<BaseRequest>(json, _jsonOptions);
        if (req == null)
            throw new InvalidOperationException("Invalid request object or missing fields.");
        return req;
    }

    /// <summary>
    /// Deserializes a JsonElement payload into a strongly-typed object.
    /// </summary>
    public T DeserializePayload<T>(JsonElement payload)
    {
        var args = payload.Deserialize<T>(_jsonOptions);
        if (args == null)
            throw new InvalidOperationException($"Invalid payload for command. Could not deserialize to {typeof(T).Name}.");
        return args;
    }

    /// <summary>
    /// Serializes a success response with lowercase keys: id, status, result.
    /// </summary>
    public string SerializeSuccess(string id, object? result)
    {
        var response = new { id, status = "success", result };
        return JsonSerializer.Serialize(response, _jsonOptions);
    }

    /// <summary>
    /// Serializes an error response with lowercase keys: id, status, error.
    /// </summary>
    public string SerializeError(string id, string error)
    {
        var response = new { id, status = "error", error };
        return JsonSerializer.Serialize(response, _jsonOptions);
    }
}
