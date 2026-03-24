using System;
using System.Runtime.InteropServices;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Windows-specific system information provider.
/// </summary>
public class WindowsSystemInfoService : ISystemInfoService
{
    public string GetPlatformName() => "Windows";

    public string GetPlatformVersion() => RuntimeInformation.OSDescription;

    public string GetMachineName() => Environment.MachineName;
}
