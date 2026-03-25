using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Power;

/// <summary>
/// Logs off the current user. Fire-and-forget -- no response sent on success.
/// </summary>
public class PowerLogOffCommand : IAppCommand
{
    private readonly IPowerService _power;

    public PowerLogOffCommand(IPowerService power)
    {
        _power = power;
    }

    public async Task<object?> ExecuteAsync()
    {
        if (!_power.IsOperationSupported("logoff"))
            return new { error = "Log off is not supported on this platform" };

        await _power.LogOffAsync();
        return null;
    }
}
