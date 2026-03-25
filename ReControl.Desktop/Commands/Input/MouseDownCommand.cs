using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Input;

internal sealed class MouseDownCommand : IAppCommand
{
    private readonly IMouseService _mouse;
    private readonly InputStateTracker _tracker;
    private readonly MouseButtonPayload _args;

    public MouseDownCommand(IMouseService mouse, InputStateTracker tracker, MouseButtonPayload args)
    {
        _mouse = mouse;
        _tracker = tracker;
        _args = args;
    }

    public Task<object?> ExecuteAsync()
    {
        _tracker.OnMouseDown(_args.Button);
        _mouse.MouseDown(_args.Button);
        return Task.FromResult<object?>(null);
    }
}
