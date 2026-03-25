using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.killProcess command. Kills a process by PID immediately.
/// When force is true, kills the entire process tree.
/// </summary>
internal sealed class TerminalKillProcessCommand : IAppCommand
{
    private readonly ProcessService _service;
    private readonly TerminalKillPayload _args;

    public TerminalKillProcessCommand(ProcessService service, TerminalKillPayload args)
    {
        _service = service;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        var result = _service.KillProcess(_args.Pid, _args.Force);
        return Task.FromResult<object?>(new { success = result });
    }
}
