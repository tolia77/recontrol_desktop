namespace ReControl.Desktop.Services.Interfaces;

/// <summary>
/// System information abstraction for platform-specific implementations.
/// </summary>
public interface ISystemInfoService
{
    /// <summary>
    /// Returns lowercase canonical platform identifier on the wire: "windows" or "linux".
    /// Capitalise only for user-facing labels (see DashboardViewModel).
    /// </summary>
    string GetPlatformName();

    /// <summary>Returns OS version string, e.g. "Microsoft Windows 11.0.26100".</summary>
    string GetPlatformVersion();

    /// <summary>Returns the machine/host name.</summary>
    string GetMachineName();

    /// <summary>Returns the local IP address used to reach the internet.</summary>
    string GetLocalIpAddress();
}
