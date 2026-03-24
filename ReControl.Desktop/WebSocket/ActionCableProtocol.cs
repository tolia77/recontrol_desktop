using System.Text.Json;

namespace ReControl.Desktop.WebSocket;

/// <summary>
/// ActionCable protocol constants, message type helpers, and control message handling.
/// Ported from WPF WebSocketClient inline handling to a dedicated protocol class.
/// </summary>
public static class ActionCableProtocol
{
    // Channel name matching backend CommandChannel
    public const string CommandChannel = "CommandChannel";

    // ActionCable message types
    public const string TypeWelcome = "welcome";
    public const string TypePing = "ping";
    public const string TypeDisconnect = "disconnect";
    public const string TypeConfirmSubscription = "confirm_subscription";
    public const string TypeRejectSubscription = "reject_subscription";

    /// <summary>
    /// Creates the JSON identifier string for a channel subscription.
    /// ActionCable expects {"channel":"CommandChannel"} as a serialized JSON string.
    /// </summary>
    public static string CreateIdentifier(string channel = CommandChannel)
    {
        return JsonSerializer.Serialize(new { channel });
    }

    /// <summary>
    /// Creates a subscribe message for ActionCable.
    /// </summary>
    public static string CreateSubscribeMessage(string channel = CommandChannel)
    {
        var message = new
        {
            command = "subscribe",
            identifier = CreateIdentifier(channel)
        };
        return JsonSerializer.Serialize(message);
    }

    /// <summary>
    /// Creates a channel message (for sending data through the channel).
    /// The data field must be a JSON-serialized string containing the actual payload.
    /// </summary>
    public static string CreateChannelMessage(object data, string channel = CommandChannel)
    {
        var message = new
        {
            command = "message",
            identifier = CreateIdentifier(channel),
            data = JsonSerializer.Serialize(data)
        };
        return JsonSerializer.Serialize(message);
    }

    /// <summary>
    /// Attempts to handle an ActionCable control message (welcome, ping, disconnect,
    /// confirm/reject subscription). Returns a result indicating whether the message
    /// was consumed as a control message.
    /// </summary>
    public static ControlMessageResult TryHandleControlMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                return ControlMessageResult.NotControlMessage;

            var type = typeProp.GetString();
            return type switch
            {
                TypePing => ControlMessageResult.Ping,
                TypeWelcome => ControlMessageResult.Welcome,
                TypeConfirmSubscription => ControlMessageResult.ConfirmSubscription,
                TypeRejectSubscription => ControlMessageResult.RejectSubscription,
                TypeDisconnect => ParseDisconnect(root),
                _ => ControlMessageResult.NotControlMessage
            };
        }
        catch
        {
            return ControlMessageResult.NotControlMessage;
        }
    }

    private static ControlMessageResult ParseDisconnect(JsonElement root)
    {
        var reconnect = ParseReconnectFlag(root);
        string? reason = root.TryGetProperty("reason", out var reasonProp)
            ? (reasonProp.ValueKind == JsonValueKind.String ? reasonProp.GetString() : reasonProp.GetRawText())
            : null;
        var unauthorized = IsUnauthorizedReason(reason);

        return new ControlMessageResult(ControlMessageType.Disconnect, reason, reconnect, unauthorized);
    }

    private static bool ParseReconnectFlag(JsonElement root)
    {
        if (!root.TryGetProperty("reconnect", out var recProp))
            return false;

        return recProp.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(recProp.GetString(), out var b) && b,
            _ => false
        };
    }

    private static bool IsUnauthorizedReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return false;

        return reason.Contains("unauth", System.StringComparison.OrdinalIgnoreCase)
            || reason.Contains("token", System.StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Types of ActionCable control messages.
/// </summary>
public enum ControlMessageType
{
    NotControlMessage,
    Welcome,
    Ping,
    Disconnect,
    ConfirmSubscription,
    RejectSubscription
}

/// <summary>
/// Result of attempting to handle a control message.
/// </summary>
public readonly struct ControlMessageResult
{
    public ControlMessageType Type { get; }
    public string? DisconnectReason { get; }
    public bool Reconnect { get; }
    public bool Unauthorized { get; }
    public bool IsControlMessage => Type != ControlMessageType.NotControlMessage;

    public ControlMessageResult(ControlMessageType type, string? disconnectReason = null, bool reconnect = false, bool unauthorized = false)
    {
        Type = type;
        DisconnectReason = disconnectReason;
        Reconnect = reconnect;
        Unauthorized = unauthorized;
    }

    // Pre-built results for common cases
    public static readonly ControlMessageResult NotControlMessage = new(ControlMessageType.NotControlMessage);
    public static readonly ControlMessageResult Ping = new(ControlMessageType.Ping);
    public static readonly ControlMessageResult Welcome = new(ControlMessageType.Welcome);
    public static readonly ControlMessageResult ConfirmSubscription = new(ControlMessageType.ConfirmSubscription);
    public static readonly ControlMessageResult RejectSubscription = new(ControlMessageType.RejectSubscription);
}
