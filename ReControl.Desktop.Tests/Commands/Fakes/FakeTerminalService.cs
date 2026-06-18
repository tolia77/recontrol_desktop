using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Tests.Commands.Fakes;

/// <summary>
/// Hand-rolled fake ITerminalService that records every call.
/// No Moq — tests use hand-rolled fakes rather than a mocking dependency.
/// </summary>
public class FakeTerminalService : ITerminalService
{
    public List<(string command, string shellType)> ExecuteCalls { get; } = new();
    public List<string> GetCwdCalls { get; } = new();
    public List<(string path, string shellType)> SetCwdCalls { get; } = new();
    public int WhoAmICalls { get; private set; }
    public int GetUptimeCalls { get; private set; }
    public int GetAvailableShellsCalls { get; private set; }
    public List<string?> AbortCalls { get; } = new();
    public int DisposeAllSessionsCalls { get; private set; }

    /// <summary>When set, ExecuteAsync will throw this exception.</summary>
    public Exception? ThrowOnExecute { get; set; }

    public string ExecuteAsync(string command, string shellType, Func<string, Task> outputSender, int timeoutMs = 30000)
    {
        if (ThrowOnExecute != null)
            throw ThrowOnExecute;
        ExecuteCalls.Add((command, shellType));
        return "fake-session-id";
    }

    public Task<string> GetCwdAsync(string shellType)
    {
        GetCwdCalls.Add(shellType);
        return Task.FromResult("/fake/cwd");
    }

    public void SetCwd(string path, string shellType) => SetCwdCalls.Add((path, shellType));

    public string WhoAmI() { WhoAmICalls++; return "fakeuser"; }

    public TimeSpan GetUptime() { GetUptimeCalls++; return TimeSpan.Zero; }

    public List<string> GetAvailableShells() { GetAvailableShellsCalls++; return new List<string> { "cmd.exe" }; }

    public void Abort(string? shellType = null) => AbortCalls.Add(shellType);

    public void DisposeAllSessions() => DisposeAllSessionsCalls++;
}
