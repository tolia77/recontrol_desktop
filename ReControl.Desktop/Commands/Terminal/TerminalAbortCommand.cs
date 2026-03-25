using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.abort command. Kills the session for a given shell type, or all sessions.
/// </summary>
internal sealed class TerminalAbortCommand : IAppCommand
{
    private readonly ITerminalService _terminal;
    private readonly string? _shellType;

    public TerminalAbortCommand(ITerminalService terminal, string? shellType = null)
    {
        _terminal = terminal;
        _shellType = shellType;
    }

    public Task<object?> ExecuteAsync()
    {
        _terminal.Abort(_shellType);
        return Task.FromResult<object?>(new { status = "ok" });
    }
}
