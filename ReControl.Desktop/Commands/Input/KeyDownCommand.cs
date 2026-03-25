using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Input;

internal sealed class KeyDownCommand : IAppCommand
{
    private readonly IKeyboardService _keyboard;
    private readonly InputStateTracker _tracker;
    private readonly KeyPayload _args;

    public KeyDownCommand(IKeyboardService keyboard, InputStateTracker tracker, KeyPayload args)
    {
        _keyboard = keyboard;
        _tracker = tracker;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        _tracker.OnKeyDown((ushort)_args.Key);
        _keyboard.KeyDown((ushort)_args.Key);
        return Task.FromResult<object?>(null);
    }
}
