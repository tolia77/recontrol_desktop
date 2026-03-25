namespace ReControl.Desktop.Services.Interfaces;

/// <summary>
/// Mouse input abstraction for platform-specific implementations.
/// Windows: Win32 SetCursorPos + mouse_event. Linux: X11 XTEST extension.
/// </summary>
public interface IMouseService
{
    void MoveTo(int x, int y);
    void MouseDown(int button);
    void MouseUp(int button);
    void Scroll(int clicks);
    void Click(int button = 0, int delayMs = 30);
    void DoubleClick(int delayMs = 120);
}
