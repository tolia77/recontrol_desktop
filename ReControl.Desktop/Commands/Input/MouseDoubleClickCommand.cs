using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Input;

internal sealed class MouseDoubleClickCommand : IAppCommand
{
    private readonly IMouseService _mouse;
    private readonly MouseDoubleClickPayload _args;

    public MouseDoubleClickCommand(IMouseService mouse, MouseDoubleClickPayload args)
    {
        _mouse = mouse;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        _mouse.DoubleClick(_args.DelayMs);
        return Task.FromResult<object?>(null);
    }
}
