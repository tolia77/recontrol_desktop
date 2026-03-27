using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform;

/// <summary>
/// Runtime platform detection and DI registration for platform-specific services.
/// </summary>
public static class PlatformServices
{
    public static void Register(IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ITokenStorageService, Windows.WindowsTokenStorageService>();
            services.AddSingleton<ISystemInfoService, Windows.WindowsSystemInfoService>();
            services.AddSingleton<IAutoStartService, Windows.WindowsAutoStartService>();
            services.AddSingleton<IPowerService, Windows.WindowsPowerService>();

            services.AddSingleton<IKeyboardService, Windows.WindowsKeyboardService>();
            services.AddSingleton<IMouseService, Windows.WindowsMouseService>();
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<ITokenStorageService, Linux.LinuxTokenStorageService>();
            services.AddSingleton<ISystemInfoService, Linux.LinuxSystemInfoService>();
            services.AddSingleton<IAutoStartService, Linux.LinuxAutoStartService>();
            services.AddSingleton<IPowerService, Linux.LinuxPowerService>();

            services.AddSingleton<IKeyboardService, Linux.LinuxKeyboardService>();
            services.AddSingleton<IMouseService, Linux.LinuxMouseService>();
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"Platform not supported: {RuntimeInformation.OSDescription}");
        }
    }
}
