using System.Threading.Tasks;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Handles terminal.listProcesses command. Returns all running processes
/// with safe per-field access for cross-platform compatibility.
/// </summary>
internal sealed class TerminalListProcessesCommand : IAppCommand
{
    private readonly ProcessService _service;

    public TerminalListProcessesCommand(ProcessService service)
    {
        _service = service;
    }

    public Task<object?> ExecuteAsync()
    {
        return Task.FromResult<object?>(_service.ListProcesses());
    }
}
