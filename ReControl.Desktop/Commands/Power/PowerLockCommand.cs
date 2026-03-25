using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Power;

/// <summary>
/// Locks the workstation. Fire-and-forget -- no response sent on success.
/// </summary>
public class PowerLockCommand : IAppCommand
{
    private readonly IPowerService _power;

    public PowerLockCommand(IPowerService power)
    {
        _power = power;
    }

    public async Task<object?> ExecuteAsync()
    {
        if (!_power.IsOperationSupported("lock"))
            return new { error = "Lock is not supported on this platform" };

        await _power.LockAsync();
        return null;
    }
}
