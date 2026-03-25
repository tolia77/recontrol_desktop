using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Windows auto-start implementation using the HKCU Run registry key.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsAutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ReControl";

    private readonly LogService _log;

    public WindowsAutoStartService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName);
            return value is string;
        }
        catch (Exception ex)
        {
            _log.Error("WindowsAutoStartService.IsEnabled", ex);
            return false;
        }
    }

    public void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath
                          ?? Process.GetCurrentProcess().MainModule!.FileName!;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            key.SetValue(AppName, $"\"{exePath}\" --minimized");
            _log.Info($"WindowsAutoStartService: enabled autostart at \"{exePath}\"");
        }
        catch (Exception ex)
        {
            _log.Error("WindowsAutoStartService.Enable", ex);
            throw;
        }
    }

    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            key?.DeleteValue(AppName, false);
            _log.Info("WindowsAutoStartService: disabled autostart");
        }
        catch (Exception ex)
        {
            _log.Error("WindowsAutoStartService.Disable", ex);
            throw;
        }
    }
}
