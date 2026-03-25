namespace ReControl.Desktop.Services.Interfaces;

/// <summary>
/// Auto-start registration abstraction for platform-specific implementations.
/// </summary>
public interface IAutoStartService
{
    /// <summary>
    /// Returns whether auto-start is currently enabled for this application.
    /// </summary>
    bool IsEnabled();

    /// <summary>
    /// Enables auto-start so the application launches on user login.
    /// </summary>
    void Enable();

    /// <summary>
    /// Disables auto-start so the application no longer launches on user login.
    /// </summary>
    void Disable();
}
