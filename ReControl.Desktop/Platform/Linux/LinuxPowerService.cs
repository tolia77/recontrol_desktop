using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Linux;

/// <summary>
/// Linux power management via systemctl and loginctl.
/// All operations are fire-and-forget.
/// </summary>
public class LinuxPowerService : IPowerService
{
    private readonly LogService _log;

    public LinuxPowerService(LogService log)
    {
        _log = log;
    }

    public Task ShutdownAsync()
    {
        _log.Info("PowerService: executing shutdown");
        StartProcess("systemctl", "poweroff");
        return Task.CompletedTask;
    }

    public Task RestartAsync()
    {
        _log.Info("PowerService: executing restart");
        StartProcess("systemctl", "reboot");
        return Task.CompletedTask;
    }

    public Task SleepAsync()
    {
        _log.Info("PowerService: executing sleep");
        StartProcess("systemctl", "suspend");
        return Task.CompletedTask;
    }

    public Task HibernateAsync()
    {
        _log.Info("PowerService: executing hibernate");
        StartProcess("systemctl", "hibernate");
        return Task.CompletedTask;
    }

    public Task LogOffAsync()
    {
        _log.Info("PowerService: executing logoff");
        // loginctl terminate-user terminates all sessions for the current user
        var user = Environment.UserName;
        StartProcess("loginctl", $"terminate-user {user}");
        return Task.CompletedTask;
    }

    public Task LockAsync()
    {
        _log.Info("PowerService: executing lock");
        StartProcess("loginctl", "lock-session");
        return Task.CompletedTask;
    }

    public bool IsOperationSupported(string operation)
    {
        if (string.Equals(operation, "hibernate", StringComparison.OrdinalIgnoreCase))
        {
            // Check if hibernate is available by looking for a swap partition/file
            // and checking systemctl can-hibernate
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = "hibernate --check-inhibitors=no --dry-run",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (process != null)
                {
                    process.WaitForExit(3000);
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                // systemctl not available or failed
            }

            return false;
        }

        return true;
    }

    private void StartProcess(string fileName, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            _log.Error($"PowerService: failed to execute {fileName} {arguments}", ex);
        }
    }
}
