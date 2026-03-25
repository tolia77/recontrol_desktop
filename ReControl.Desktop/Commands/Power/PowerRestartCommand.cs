using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Power;

/// <summary>
/// Restarts the machine. Fire-and-forget -- no response sent on success.
/// </summary>
public class PowerRestartCommand : IAppCommand
{
    private readonly IPowerService _power;

    public PowerRestartCommand(IPowerService power)
    {
        _power = power;
    }

    public async Task<object?> ExecuteAsync()
    {
        if (!_power.IsOperationSupported("restart"))
            return new { error = "Restart is not supported on this platform" };

        await _power.RestartAsync();
        return null;
    }
}
