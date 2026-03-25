using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Power;

/// <summary>
/// Puts the machine to sleep. Fire-and-forget -- no response sent on success.
/// </summary>
public class PowerSleepCommand : IAppCommand
{
    private readonly IPowerService _power;

    public PowerSleepCommand(IPowerService power)
    {
        _power = power;
    }

    public async Task<object?> ExecuteAsync()
    {
        if (!_power.IsOperationSupported("sleep"))
            return new { error = "Sleep is not supported on this platform" };

        await _power.SleepAsync();
        return null;
    }
}
