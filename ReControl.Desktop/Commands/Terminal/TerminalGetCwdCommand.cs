using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.getCwd command. Queries the actual CWD from the shell session.
/// </summary>
internal sealed class TerminalGetCwdCommand : IAppCommand
{
    private readonly ITerminalService _terminal;
    private readonly string _shellType;

    public TerminalGetCwdCommand(ITerminalService terminal, string? shellType = null)
    {
        _terminal = terminal;
        _shellType = shellType ?? (System.OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash");
    }

    public async Task<object?> ExecuteAsync()
    {
        var cwd = await _terminal.GetCwdAsync(_shellType);
        return cwd;
    }
}
