using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.startProcess command. Launches a new process
/// and returns its PID (-1 on failure).
/// </summary>
internal sealed class TerminalStartProcessCommand : IAppCommand
{
    private readonly ProcessService _service;
    private readonly TerminalStartPayload _args;

    public TerminalStartProcessCommand(ProcessService service, TerminalStartPayload args)
    {
        _service = service;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        var pid = _service.StartProcess(_args.FileName, _args.Arguments, _args.RedirectOutput);
        return Task.FromResult<object?>(new { pid });
    }
}
