using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;
using ReControl.Desktop.WebSocket;

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
        shellType = NormalizeShellType(shellType);
        var session = GetOrCreateSession(shellType);

        // Wire output to sender
        session.SetOutputSender(outputSender);

        // Send command
        session.SendCommand(command);

        _log.Info($"TerminalService.ExecuteAsync: sent '{command}' to session {session.SessionId} ({shellType})");
        return session.SessionId;
    }

    public string GetCwd(string shellType)
    {
        shellType = NormalizeShellType(shellType);
        var session = GetOrCreateSession(shellType);
        return session.QueryCwd();
    }

    public void SetCwd(string path, string shellType)
    {
        shellType = NormalizeShellType(shellType);
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
        bool isAdmin;

        if (OperatingSystem.IsWindows())
        {
            isAdmin = CheckWindowsAdmin();
        }
        else
        {
            isAdmin = CheckLinuxAdmin();
        }

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
        var shells = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            shells.Add("cmd.exe");
            shells.Add("powershell.exe");
            if (IsInPath("pwsh.exe"))
                shells.Add("pwsh.exe");
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                var lines = File.ReadAllLines("/etc/shells");
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                        continue;
                    if (File.Exists(trimmed))
                        shells.Add(trimmed);
                }
            }
            catch
            {
                shells.Add("/bin/bash");
                shells.Add("/bin/sh");
            }

            if (IsInPath("pwsh"))
                shells.Add("pwsh");
        }

        return shells;
    }

    public void Abort(string? shellType = null)
    {
        _sessionLock.Wait();
        try
        {
            if (shellType != null)
            {
                var normalized = NormalizeShellType(shellType);
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

    private static string NormalizeShellType(string shellType)
    {
        if (string.IsNullOrWhiteSpace(shellType))
            return GetDefaultShell();

        return shellType.Trim().ToLowerInvariant();
    }

    private static string GetDefaultShell()
    {
        if (OperatingSystem.IsWindows())
            return "cmd.exe";

        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
    }

    private static bool CheckWindowsAdmin()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckLinuxAdmin()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "id",
                Arguments = "-u",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            return output == "0";
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInPath(string executable)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ==================== ShellSession Inner Class ====================

    /// <summary>
    /// Persistent shell session that maintains a long-lived process with redirected I/O.
    /// Output chunks are streamed in real-time via the OutputReceived event.
    /// </summary>
    private class ShellSession : IDisposable
    {
        private static readonly Regex AnsiEscapeRegex = new(
            @"\x1B(?:\[[0-9;]*[a-zA-Z]|\].*?\x07|\[[0-9;]*m)",
            RegexOptions.Compiled);

        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private readonly LogService _log;
        private readonly string _shellType;
        private Func<string, Task>? _outputSender;
        private bool _disposed;

        public string SessionId { get; }
        public bool IsExited => _disposed || _process.HasExited;

        public ShellSession(string shellType, LogService log)
        {
            _shellType = shellType;
            _log = log;
            SessionId = Guid.NewGuid().ToString("N")[..12];

            var psi = new ProcessStartInfo
            {
                FileName = shellType,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            // On Windows cmd.exe, disable echo to avoid command echo in output
            if (OperatingSystem.IsWindows() &&
                shellType.Contains("cmd", StringComparison.OrdinalIgnoreCase))
            {
                psi.Arguments = "/Q";
            }

            _process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start shell process: {shellType}");
            _stdin = _process.StandardInput;

            // Start async output readers
            _ = ReadOutputAsync(_process.StandardOutput, "stdout");
            _ = ReadOutputAsync(_process.StandardError, "stderr");

            _log.Info($"ShellSession: started '{shellType}' (pid: {_process.Id}, session: {SessionId})");
        }

        public void SetOutputSender(Func<string, Task> sender)
        {
            _outputSender = sender;
        }

        public void SendCommand(string command)
        {
            if (_disposed || _process.HasExited)
                throw new InvalidOperationException("Shell session has exited");

            _stdin.WriteLine(command);
            _stdin.Flush();
        }

        public string QueryCwd()
        {
            if (_disposed || _process.HasExited)
                throw new InvalidOperationException("Shell session has exited");

            // Use a unique end-marker to capture CWD output
            var marker = $"__CWD_{Guid.NewGuid():N}__";
            string cwdCommand;

            if (OperatingSystem.IsWindows())
            {
                if (_shellType.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                    _shellType.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
                {
                    cwdCommand = $"Write-Host (Get-Location).Path";
                }
                else
                {
                    cwdCommand = "cd";
                }
            }
            else
            {
                cwdCommand = "pwd";
            }

            // Temporarily capture output for CWD query
            var cwdResult = new TaskCompletionSource<string>();
            var previousSender = _outputSender;

            _outputSender = async (chunk) =>
            {
                var stripped = StripAnsi(chunk).Trim();
                if (!string.IsNullOrWhiteSpace(stripped))
                {
                    // Get the last non-empty line as the CWD
                    var lines = stripped.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length > 0 && !trimmed.Contains(cwdCommand) && (
                            Path.IsPathRooted(trimmed) || trimmed.StartsWith("~") || trimmed.StartsWith("/")))
                        {
                            cwdResult.TrySetResult(trimmed);
                            return;
                        }
                    }
                }
            };

            SendCommand(cwdCommand);

            // Wait up to 3 seconds for the CWD result
            if (cwdResult.Task.Wait(3000))
            {
                _outputSender = previousSender;
                return cwdResult.Task.Result;
            }

            _outputSender = previousSender;
            _log.Warning("ShellSession.QueryCwd: timed out waiting for CWD response");
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private async Task ReadOutputAsync(StreamReader reader, string streamName)
        {
            var buffer = new char[4096];
            try
            {
                while (!_disposed && !_process.HasExited)
                {
                    int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) break;

                    var chunk = new string(buffer, 0, bytesRead);
                    var stripped = StripAnsi(chunk);

                    if (string.IsNullOrEmpty(stripped)) continue;

                    var sender = _outputSender;
                    if (sender != null)
                    {
                        try
                        {
                            var message = JsonSerializer.Serialize(new
                            {
                                command = "terminal.output",
                                payload = new
                                {
                                    sessionId = SessionId,
                                    stream = streamName,
                                    data = stripped
                                }
                            });

                            var channelMessage = ActionCableProtocol.CreateChannelMessage(
                                JsonSerializer.Deserialize<JsonElement>(message));

                            await sender(channelMessage);
                        }
                        catch (Exception ex)
                        {
                            _log.Warning($"ShellSession.ReadOutput: failed to send chunk: {ex.Message}");
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected on session dispose
            }
            catch (Exception ex)
            {
                if (!_disposed)
                    _log.Warning($"ShellSession.ReadOutput ({streamName}): {ex.Message}");
            }
        }

        private static string StripAnsi(string input)
        {
            return AnsiEscapeRegex.Replace(input, string.Empty);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _log.Warning($"ShellSession.Dispose: kill failed: {ex.Message}");
            }

            try { _stdin.Dispose(); } catch { }
            try { _process.Dispose(); } catch { }

            _log.Info($"ShellSession: disposed session {SessionId} ({_shellType})");
        }
    }
}
