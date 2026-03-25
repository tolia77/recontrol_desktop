using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.getUptime command. Returns system uptime as a formatted string.
/// </summary>
internal sealed class TerminalGetUptimeCommand : IAppCommand
{
    private readonly ITerminalService _terminal;

    public TerminalGetUptimeCommand(ITerminalService terminal)
    {
        _terminal = terminal;
    }

    public Task<object?> ExecuteAsync()
    {
        var uptime = _terminal.GetUptime();
        return Task.FromResult<object?>(uptime.ToString(@"d\.hh\:mm\:ss"));
    }
}
