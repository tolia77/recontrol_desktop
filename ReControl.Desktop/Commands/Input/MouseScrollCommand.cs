using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Input;

internal sealed class MouseScrollCommand : IAppCommand
{
    private readonly IMouseService _mouse;
    private readonly MouseScrollPayload _args;

    public MouseScrollCommand(IMouseService mouse, MouseScrollPayload args)
    {
        _mouse = mouse;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        _mouse.Scroll(_args.Clicks);
        return Task.FromResult<object?>(null);
    }
}
