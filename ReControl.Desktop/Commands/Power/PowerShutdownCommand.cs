using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Power;

/// <summary>
/// Shuts down the machine. Fire-and-forget -- no response sent on success.
/// </summary>
public class PowerShutdownCommand : IAppCommand
{
    private readonly IPowerService _power;

    public PowerShutdownCommand(IPowerService power)
    {
        _power = power;
    }

    public async Task<object?> ExecuteAsync()
    {
        if (!_power.IsOperationSupported("shutdown"))
            return new { error = "Shutdown is not supported on this platform" };

        await _power.ShutdownAsync();
        return null;
    }
}
