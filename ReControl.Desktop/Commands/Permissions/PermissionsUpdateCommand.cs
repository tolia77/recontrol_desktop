using System.Text.Json;
using System.Threading.Tasks;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Permissions;

namespace ReControl.Desktop.Commands.Permissions;

/// <summary>
/// Applies a permissions snapshot pushed from the backend on owner-side
/// edits. Backend payload shape:
///   { recipient_user_id: "...", permissions: { see_screen: true, ... } }
/// We do not validate recipient_user_id against the active peer -- only one
/// peer connection exists at a time per WebRtcService instance, so any
/// update broadcast on this device's stream applies to it.
/// </summary>
public sealed class PermissionsUpdateCommand : IAppCommand
{
    private readonly WebRtcService _service;
    private readonly JsonElement _permissionsElement;

    public PermissionsUpdateCommand(WebRtcService service, JsonElement permissionsElement)
    {
        _service = service;
        _permissionsElement = permissionsElement;
    }

    public Task<object?> ExecuteAsync()
    {
        var snapshot = WebRtcService.ParsePermissionsSnapshot(_permissionsElement);
        _service.UpdatePermissions(snapshot);
        return Task.FromResult<object?>(null);
    }
}
