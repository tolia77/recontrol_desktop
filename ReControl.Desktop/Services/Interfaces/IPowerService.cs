using System.Threading.Tasks;

namespace ReControl.Desktop.Services.Interfaces;

/// <summary>
/// Power management abstraction for platform-specific implementations.
/// Provides shutdown, restart, sleep, hibernate, logoff, and lock operations.
/// Operations are fire-and-forget since the machine may become unreachable immediately.
/// </summary>
public interface IPowerService
{
    Task ShutdownAsync();
    Task RestartAsync();
    Task SleepAsync();
    Task HibernateAsync();
    Task LogOffAsync();
    Task LockAsync();

    /// <summary>
    /// Check whether a specific power operation is supported on the current platform.
    /// </summary>
    bool IsOperationSupported(string operation);
}
