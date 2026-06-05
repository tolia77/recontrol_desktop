using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Input;

internal sealed class TypeTextCommand : IAppCommand
{
    private readonly IKeyboardService _keyboard;
    private readonly TypeTextPayload _args;

    public TypeTextCommand(IKeyboardService keyboard, TypeTextPayload args)
    {
        _keyboard = keyboard;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        // No InputStateTracker involvement: typeText is transient (each character is
        // pressed and released atomically), so there is no held-key state to release.
        _keyboard.TypeText(_args.Text);
        return Task.FromResult<object?>(null);
    }
}
