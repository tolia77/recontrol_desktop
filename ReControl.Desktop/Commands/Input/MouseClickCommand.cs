using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Input;

internal sealed class MouseClickCommand : IAppCommand
{
    private readonly IMouseService _mouse;
    private readonly MouseClickPayload _args;

    public MouseClickCommand(IMouseService mouse, MouseClickPayload args)
    {
        _mouse = mouse;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        _mouse.Click(_args.Button, _args.DelayMs);
        return Task.FromResult<object?>(null);
    }
}
