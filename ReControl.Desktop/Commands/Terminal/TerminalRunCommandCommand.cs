using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.Commands.Terminal;

/// <summary>
/// Phase 18 AI-tool one-shot execve handler. Distinct from the streaming
/// `terminal.execute` route (TerminalExecuteCommand) which runs through a
/// persistent /bin/bash session and streams output as separate frames:
/// this one spawns the binary directly with separate args (no shell parsing),
/// captures stdout/stderr/exit synchronously, and returns the full result as
/// the single response keyed by the existing `id` correlation field.
/// Used by AiTools::RunCommand on the backend; the agent-loop needs a
/// one-shot reply, not streamed terminal output.
/// </summary>
internal sealed class TerminalRunCommandCommand : IAppCommand
{
    private readonly TerminalRunCommandPayload _args;
    private readonly LogService _log;

    public TerminalRunCommandCommand(TerminalRunCommandPayload args, LogService log)
    {
        _args = args ?? throw new ArgumentNullException(nameof(args));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<object?> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(_args.Binary))
            throw new ArgumentException("binary is required");

        var args = _args.Args ?? new List<string>();
        var cwd = string.IsNullOrWhiteSpace(_args.Cwd) ? "/" : _args.Cwd;

        var psi = new ProcessStartInfo
        {
            FileName = _args.Binary,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        int exitCode;

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            exitCode = proc.ExitCode;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _log.Warning($"terminal.runCommand: {_args.Binary} threw: {ex.Message}");
            return new
            {
                stdout = stdout.ToString(),
                stderr = stderr.ToString() + ex.Message,
                exit_code = -1,
                elapsed_seconds = stopwatch.Elapsed.TotalSeconds,
            };
        }

        stopwatch.Stop();
        return new
        {
            stdout = stdout.ToString(),
            stderr = stderr.ToString(),
            exit_code = exitCode,
            elapsed_seconds = stopwatch.Elapsed.TotalSeconds,
        };
    }
}
