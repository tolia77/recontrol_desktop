using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Power;

/// <summary>
/// Hibernates the machine. Fire-and-forget -- no response sent on success.
/// Returns error if hibernate is unavailable (e.g., some Linux systems).
/// </summary>
public class PowerHibernateCommand : IAppCommand
{
    private readonly IPowerService _power;

    public PowerHibernateCommand(IPowerService power)
    {
        _power = power;
    }

    public async Task<object?> ExecuteAsync()
    {
        if (!_power.IsOperationSupported("hibernate"))
            return new { error = "Hibernate is not supported on this platform" };

        await _power.HibernateAsync();
        return null;
    }
}
