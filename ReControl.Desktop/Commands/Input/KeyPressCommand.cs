using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Input;

internal sealed class KeyPressCommand : IAppCommand
{
    private readonly IKeyboardService _keyboard;
    private readonly KeyPressPayload _args;

    public KeyPressCommand(IKeyboardService keyboard, KeyPressPayload args)
    {
        _keyboard = keyboard;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        _keyboard.Press((ushort)_args.Key, _args.HoldMs);
        return Task.FromResult<object?>(null);
    }
}
