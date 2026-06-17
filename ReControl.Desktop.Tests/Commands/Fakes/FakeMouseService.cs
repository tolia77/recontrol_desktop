using System.Collections.Generic;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Tests.Commands.Fakes;

/// <summary>
/// Hand-rolled fake IMouseService that records every call.
/// No Moq — matches the no-new-dep constraint (D-08).
/// </summary>
public class FakeMouseService : IMouseService
{
    public List<(int x, int y)> MoveToXalls { get; } = new();
    public List<int> MouseDownCalls { get; } = new();
    public List<int> MouseUpCalls { get; } = new();
    public List<int> ScrollCalls { get; } = new();
    public List<(int button, int delayMs)> ClickCalls { get; } = new();
    public List<int> DoubleClickCalls { get; } = new();

    public void MoveTo(int x, int y) => MoveToXalls.Add((x, y));

    public void MouseDown(int button) => MouseDownCalls.Add(button);

    public void MouseUp(int button) => MouseUpCalls.Add(button);

    public void Scroll(int clicks) => ScrollCalls.Add(clicks);

    public void Click(int button = 0, int delayMs = 30) => ClickCalls.Add((button, delayMs));

    public void DoubleClick(int delayMs = 120) => DoubleClickCalls.Add(delayMs);
}
