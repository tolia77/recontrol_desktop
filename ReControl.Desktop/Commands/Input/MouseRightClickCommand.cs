using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Commands.Input;

internal sealed class MouseRightClickCommand : IAppCommand
{
    private readonly IMouseService _mouse;

    public MouseRightClickCommand(IMouseService mouse)
    {
        _mouse = mouse;
    }

    public Task<object?> ExecuteAsync()
    {
        _mouse.Click(1, 30); // button 1 = right, per frontend mapping
        return Task.FromResult<object?>(null);
    }
}
