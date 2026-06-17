using System.Collections.Generic;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Tests.Commands.Fakes;

/// <summary>
/// Hand-rolled fake IKeyboardService that records every call.
/// No Moq — matches the no-new-dep constraint (D-08).
/// </summary>
public class FakeKeyboardService : IKeyboardService
{
    public List<ushort> KeyDownCalls { get; } = new();
    public List<ushort> KeyUpCalls { get; } = new();
    public List<(ushort vk, int holdMs)> PressCalls { get; } = new();
    public List<string> TypeTextCalls { get; } = new();

    public bool IsXtestAvailable => true;

    public void KeyDown(ushort vk) => KeyDownCalls.Add(vk);

    public void KeyUp(ushort vk) => KeyUpCalls.Add(vk);

    public void Press(ushort vk, int holdMs = 30) => PressCalls.Add((vk, holdMs));

    public void TypeText(string text) => TypeTextCalls.Add(text);
}
