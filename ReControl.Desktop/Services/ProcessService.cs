using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ReControl.Desktop.Models;

namespace ReControl.Desktop.Services;

/// <summary>
/// Cross-platform process management service.
/// Lists, kills, and starts processes with safe field access for Linux/Windows compatibility.
/// </summary>
public class ProcessService
{
    private readonly LogService _log;

    public ProcessService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Returns all running processes with safe per-field access.
    /// Inaccessible fields (CpuTime, StartTime on Linux system processes) return null.
    /// </summary>
    public List<ProcessInfo> ListProcesses()
    {
        _log.Info("ProcessService.ListProcesses called");

        return Process.GetProcesses()
            .Select(p =>
            {
                try
                {
                    return new ProcessInfo
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        MemoryMB = p.WorkingSet64 / (1024 * 1024),
                        CpuTime = SafeGetCpuTime(p),
                        StartTime = SafeGetStartTime(p)
                    };
                }
                catch (Exception ex)
                {
                    _log.Warning($"ProcessService.ListProcesses: skipped process: {ex.Message}");
                    return null;
                }
            })
            .Where(p => p != null)
            .OrderBy(p => p!.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    /// <summary>
    /// Kills a process by PID. When force is true, kills the entire process tree.
    /// </summary>
    public bool KillProcess(int pid, bool force = false)
    {
        _log.Info($"ProcessService.KillProcess called: pid={pid}, force={force}");
        try
        {
            var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: force);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"ProcessService.KillProcess failed: pid={pid}", ex);
            return false;
        }
    }

    /// <summary>
    /// Starts a new process and returns its PID. Returns -1 on failure.
    /// </summary>
    public int StartProcess(string fileName, string arguments = "", bool redirectOutput = false)
    {
        _log.Info($"ProcessService.StartProcess called: fileName={fileName}, arguments={arguments}, redirectOutput={redirectOutput}");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = !redirectOutput,
                CreateNoWindow = redirectOutput
            };

            if (redirectOutput)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }

            var process = Process.Start(psi);
            var pid = process?.Id ?? -1;
            _log.Info($"ProcessService.StartProcess: started with pid={pid}");
            return pid;
        }
        catch (Exception ex)
        {
            _log.Error($"ProcessService.StartProcess failed: {fileName}", ex);
            return -1;
        }
    }

    // ==================== Safe Field Accessors ====================

    private static string? SafeGetCpuTime(Process p)
    {
        try
        {
            return p.TotalProcessorTime.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeGetStartTime(Process p)
    {
        try
        {
            return p.StartTime.ToString("o");
        }
        catch
        {
            return null;
        }
    }
}
