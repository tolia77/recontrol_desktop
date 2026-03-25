using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.getShells command. Returns list of available shells for this platform.
/// </summary>
internal sealed class TerminalGetShellsCommand : IAppCommand
{
    private readonly ITerminalService _terminal;

    public TerminalGetShellsCommand(ITerminalService terminal)
    {
        _terminal = terminal;
    }

    public Task<object?> ExecuteAsync()
    {
        var shells = _terminal.GetAvailableShells();
        return Task.FromResult<object?>(shells);
    }
}
