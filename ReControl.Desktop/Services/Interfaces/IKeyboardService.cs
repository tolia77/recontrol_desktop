namespace ReControl.Desktop.Services.Interfaces;

/// <summary>
/// Keyboard input abstraction for platform-specific implementations.
/// Windows: Win32 SendInput. Linux: X11 XTEST extension.
/// </summary>
public interface IKeyboardService
{
    void KeyDown(ushort vk);
    void KeyUp(ushort vk);
    void Press(ushort vk, int holdMs = 30);

    /// <summary>
    /// Whether keyboard simulation is available.
    /// Always true on Windows. On Linux, requires XTEST extension.
    /// </summary>
    bool IsXtestAvailable { get; }
}
