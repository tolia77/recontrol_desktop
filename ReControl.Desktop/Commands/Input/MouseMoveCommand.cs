using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Input;

internal sealed class MouseMoveCommand : IAppCommand
{
    private readonly IMouseService _mouse;
    private readonly MouseMovePayload _args;

    public MouseMoveCommand(IMouseService mouse, MouseMovePayload args)
    {
        _mouse = mouse;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        _mouse.MoveTo(_args.X, _args.Y);
        return Task.FromResult<object?>(null);
    }
}
