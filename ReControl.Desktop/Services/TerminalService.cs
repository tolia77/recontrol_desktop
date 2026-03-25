using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;
using ReControl.Desktop.Services.Terminal;

namespace ReControl.Desktop.Services;

/// <summary>
/// Cross-platform terminal service with persistent shell sessions and real-time streaming output.
/// Key architectural upgrade over WPF: sessions persist between commands, output streams in real-time.
/// </summary>
public class TerminalService : ITerminalService
{
    private const int MaxConcurrentSessions = 3;

    private readonly Dictionary<string, ShellSession> _sessions = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly LogService _log;

    public TerminalService(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public string ExecuteAsync(string command, string shellType, Func<string, Task> outputSender, int timeoutMs = 30000)
    {
        shellType = ShellLocator.NormalizeShellType(shellType);
        var session = GetOrCreateSession(shellType);

        // Wire output to sender
        session.SetOutputSender(outputSender);

        // Send command
        session.SendCommand(command);

        _log.Info($"TerminalService.ExecuteAsync: sent '{command}' to session {session.SessionId} ({shellType})");
        return session.SessionId;
    }

    public async Task<string> GetCwdAsync(string shellType)
    {
        shellType = ShellLocator.NormalizeShellType(shellType);
        var session = GetOrCreateSession(shellType);
        return await session.QueryCwdAsync();
    }

    public void SetCwd(string path, string shellType)
    {
        shellType = ShellLocator.NormalizeShellType(shellType);
        var session = GetOrCreateSession(shellType);

        var cdCommand = OperatingSystem.IsWindows()
            ? $"cd /d \"{path}\""
            : $"cd \"{path}\"";

        session.SendCommand(cdCommand);
        _log.Info($"TerminalService.SetCwd: sent cd to '{path}' in session ({shellType})");
    }

    public string WhoAmI()
    {
        _log.Info("TerminalService.WhoAmI called");
        var username = Environment.UserName;
        bool isAdmin = ShellLocator.CheckAdminStatus();

        var domain = Environment.UserDomainName;
        return $"{domain}\\{username} (Admin: {isAdmin})";
    }

    public TimeSpan GetUptime()
    {
        _log.Info("TerminalService.GetUptime called");
        return TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    public List<string> GetAvailableShells()
    {
        _log.Info("TerminalService.GetAvailableShells called");
        return ShellLocator.GetAvailableShells();
    }

    public void Abort(string? shellType = null)
    {
        _sessionLock.Wait();
        try
        {
            if (shellType != null)
            {
                var normalized = ShellLocator.NormalizeShellType(shellType);
                if (_sessions.TryGetValue(normalized, out var session))
                {
                    _log.Info($"TerminalService.Abort: killing session ({normalized})");
                    session.Dispose();
                    _sessions.Remove(normalized);
                }
            }
            else
            {
                _log.Info("TerminalService.Abort: killing all sessions");
                foreach (var session in _sessions.Values)
                    session.Dispose();
                _sessions.Clear();
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public void DisposeAllSessions()
    {
        _log.Info("TerminalService.DisposeAllSessions: cleaning up on disconnect");
        Abort(null);
    }

    // ==================== Private Helpers ====================

    private ShellSession GetOrCreateSession(string shellType)
    {
        _sessionLock.Wait();
        try
        {
            if (_sessions.TryGetValue(shellType, out var existing) && !existing.IsExited)
                return existing;

            // Remove exited session if any
            if (existing != null)
            {
                existing.Dispose();
                _sessions.Remove(shellType);
            }

            // Check max sessions limit
            if (_sessions.Count >= MaxConcurrentSessions)
            {
                // Dispose oldest session
                var oldest = _sessions.First();
                _log.Warning($"TerminalService: max sessions reached, disposing oldest ({oldest.Key})");
                oldest.Value.Dispose();
                _sessions.Remove(oldest.Key);
            }

            var session = new ShellSession(shellType, _log);
            _sessions[shellType] = session;
            _log.Info($"TerminalService: created new session for '{shellType}' (id: {session.SessionId})");
            return session;
        }
        finally
        {
            _sessionLock.Release();
        }
    }
}
