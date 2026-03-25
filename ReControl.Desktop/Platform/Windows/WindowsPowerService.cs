using System.Diagnostics;
using System.Threading.Tasks;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Windows power management via shutdown.exe and rundll32.exe.
/// All operations are fire-and-forget with no window created.
/// </summary>
public class WindowsPowerService : IPowerService
{
    private readonly LogService _log;

    public WindowsPowerService(LogService log)
    {
        _log = log;
    }

    public Task ShutdownAsync()
    {
        _log.Info("PowerService: executing shutdown");
        StartProcess("shutdown", "/s /t 0");
        return Task.CompletedTask;
    }

    public Task RestartAsync()
    {
        _log.Info("PowerService: executing restart");
        StartProcess("shutdown", "/r /t 0");
        return Task.CompletedTask;
    }

    public Task SleepAsync()
    {
        _log.Info("PowerService: executing sleep");
        StartProcess("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
        return Task.CompletedTask;
    }

    public Task HibernateAsync()
    {
        _log.Info("PowerService: executing hibernate");
        StartProcess("rundll32.exe", "powrprof.dll,SetSuspendState");
        return Task.CompletedTask;
    }

    public Task LogOffAsync()
    {
        _log.Info("PowerService: executing logoff");
        StartProcess("shutdown", "/l");
        return Task.CompletedTask;
    }

    public Task LockAsync()
    {
        _log.Info("PowerService: executing lock");
        StartProcess("rundll32.exe", "user32.dll,LockWorkStation");
        return Task.CompletedTask;
    }

    public bool IsOperationSupported(string operation) => true;

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
        catch (System.Exception ex)
        {
            _log.Error($"PowerService: failed to execute {fileName} {arguments}", ex);
        }
    }
}
