using System;
using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.execute command. Starts command in a persistent shell session,
/// returns sessionId immediately. Output streams via sender callback.
/// </summary>
internal sealed class TerminalExecuteCommand : IAppCommand
{
    private readonly ITerminalService _terminal;
    private readonly TerminalCommandPayload _args;
    private readonly Func<string, Task> _sender;

    public TerminalExecuteCommand(ITerminalService terminal, TerminalCommandPayload args, Func<string, Task> sender)
    {
        _terminal = terminal;
        _args = args;
        _sender = sender;
    }

    public Task<object?> ExecuteAsync()
    {
        // Determine shell: use payload Shell if specified, otherwise platform default
        var shell = _args.Shell;
        if (string.IsNullOrWhiteSpace(shell))
        {
            shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";
        }

        var sessionId = _terminal.ExecuteAsync(_args.Command, shell, _sender, _args.Timeout);
        return Task.FromResult<object?>(new { sessionId });
    }
}
