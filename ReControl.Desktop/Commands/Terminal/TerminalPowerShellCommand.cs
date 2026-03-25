using System;
using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.powershell command. Same streaming pattern as execute,
/// defaults to powershell.exe on Windows, pwsh on Linux.
/// </summary>
internal sealed class TerminalPowerShellCommand : IAppCommand
{
    private readonly ITerminalService _terminal;
    private readonly TerminalCommandPayload _args;
    private readonly Func<string, Task> _sender;

    public TerminalPowerShellCommand(ITerminalService terminal, TerminalCommandPayload args, Func<string, Task> sender)
    {
        _terminal = terminal;
        _args = args;
        _sender = sender;
    }

    public Task<object?> ExecuteAsync()
    {
        var shell = _args.Shell;
        if (string.IsNullOrWhiteSpace(shell))
        {
            shell = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        }

        var sessionId = _terminal.ExecuteAsync(_args.Command, shell, _sender, _args.Timeout);
        return Task.FromResult<object?>(new { sessionId });
    }
}
