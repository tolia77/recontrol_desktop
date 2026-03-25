using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Linux;

/// <summary>
/// Linux auto-start implementation using XDG desktop entry in ~/.config/autostart/.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxAutoStartService : IAutoStartService
{
    private readonly string _desktopFilePath;
    private readonly LogService _log;

    public LinuxAutoStartService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                         ?? Path.Combine(
                             Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                             ".config");

        _desktopFilePath = Path.Combine(configHome, "autostart", "recontrol.desktop");
    }

    public bool IsEnabled()
    {
        try
        {
            return File.Exists(_desktopFilePath);
        }
        catch (Exception ex)
        {
            _log.Error("LinuxAutoStartService.IsEnabled", ex);
            return false;
        }
    }

    public void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath
                          ?? Process.GetCurrentProcess().MainModule!.FileName!;

            var directory = Path.GetDirectoryName(_desktopFilePath)!;
            Directory.CreateDirectory(directory);

            var content =
"[Desktop Entry]\n" +
"Type=Application\n" +
"Name=ReControl\n" +
$"Exec=\"{exePath}\" --minimized\n" +
"Hidden=false\n" +
"X-GNOME-Autostart-enabled=true\n" +
"Comment=ReControl remote desktop agent\n";

            File.WriteAllText(_desktopFilePath, content);
            _log.Info($"LinuxAutoStartService: enabled autostart at \"{_desktopFilePath}\"");
        }
        catch (Exception ex)
        {
            _log.Error("LinuxAutoStartService.Enable", ex);
            throw;
        }
    }

    public void Disable()
    {
        try
        {
            if (File.Exists(_desktopFilePath))
            {
                File.Delete(_desktopFilePath);
                _log.Info("LinuxAutoStartService: disabled autostart");
            }
        }
        catch (Exception ex)
        {
            _log.Error("LinuxAutoStartService.Disable", ex);
            throw;
        }
    }
}
