using System.Text.Json;

namespace ReControl.Desktop.Models;

/// <summary>
/// The base structure for all incoming command requests from the server.
/// Ported from WPF CommandJsonParser/ResponseModels.
/// </summary>
public class BaseRequest
{
    public string? Id { get; set; }
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// The payload is held as a raw JsonElement, to be deserialized
    /// later by the specific command handler.
    /// </summary>
    public JsonElement Payload { get; set; }

    /// <summary>
    /// Envelope-level permissions snapshot. The backend attaches this only on
    /// `webrtc.offer` broadcasts (Task 4). Default `JsonElement` (ValueKind
    /// Undefined) for every other command -- consumers must handle that.
    /// </summary>
    public JsonElement Permissions { get; set; }
}
