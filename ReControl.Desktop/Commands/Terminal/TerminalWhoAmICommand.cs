using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.whoAmI command. Returns cross-platform user identity.
/// </summary>
internal sealed class TerminalWhoAmICommand : IAppCommand
{
    private readonly ITerminalService _terminal;

    public TerminalWhoAmICommand(ITerminalService terminal)
    {
        _terminal = terminal;
    }

    public Task<object?> ExecuteAsync()
    {
        var result = _terminal.WhoAmI();
        return Task.FromResult<object?>(result);
    }
}
