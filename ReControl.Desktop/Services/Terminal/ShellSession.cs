using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ReControl.Desktop.WebSocket;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Services.Terminal;

/// <summary>
/// Persistent shell session that maintains a long-lived process with redirected I/O.
/// Output chunks are streamed in real-time via the OutputReceived event.
/// </summary>
public class ShellSession : IDisposable
{
    private static readonly Regex AnsiEscapeRegex = new(
        @"\x1B(?:\[[0-9;]*[a-zA-Z]|\].*?\x07|\[[0-9;]*m)",
        RegexOptions.Compiled);

    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly LogService _log;
    private readonly string _shellType;
    private volatile Func<string, Task>? _outputSender;
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

    public async Task<string> QueryCwdAsync()
    {
        if (_disposed || _process.HasExited)
            throw new InvalidOperationException("Shell session has exited");

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

        _outputSender = (chunk) =>
        {
            var stripped = StripAnsi(chunk).Trim();
            if (!string.IsNullOrWhiteSpace(stripped))
            {
                var lines = stripped.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0 && !trimmed.Contains(cwdCommand) && (
                        Path.IsPathRooted(trimmed) || trimmed.StartsWith("~") || trimmed.StartsWith("/")))
                    {
                        cwdResult.TrySetResult(trimmed);
                        return Task.CompletedTask;
                    }
                }
            }
            return Task.CompletedTask;
        };

        SendCommand(cwdCommand);

        try
        {
            return await cwdResult.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            _log.Warning("ShellSession.QueryCwd: timed out waiting for CWD response");
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        finally
        {
            _outputSender = previousSender;
        }
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
                _process.Kill(true); // .Net 6+ extension kill entire tree
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
