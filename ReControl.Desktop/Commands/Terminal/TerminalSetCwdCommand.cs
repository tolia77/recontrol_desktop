using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.setCwd command. Sends cd to the shell session.
/// </summary>
internal sealed class TerminalSetCwdCommand : IAppCommand
{
    private readonly ITerminalService _terminal;
    private readonly TerminalSetCwdPayload _args;
    private readonly string _shellType;

    public TerminalSetCwdCommand(ITerminalService terminal, TerminalSetCwdPayload args, string? shellType = null)
    {
        _terminal = terminal;
        _args = args;
        _shellType = shellType ?? (System.OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash");
    }

    public Task<object?> ExecuteAsync()
    {
        _terminal.SetCwd(_args.Path, _shellType);
        return Task.FromResult<object?>(new { status = "ok" });
    }
}
