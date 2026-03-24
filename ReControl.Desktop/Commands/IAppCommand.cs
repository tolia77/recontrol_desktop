using System.Threading.Tasks;

namespace ReControl.Desktop.Commands;

/// <summary>
/// Command interface for all executable commands received from the server.
/// Each command handler implements this interface and returns a result object.
/// Ported from WPF IAppCommand.
/// </summary>
public interface IAppCommand
{
    Task<object?> ExecuteAsync();
}
