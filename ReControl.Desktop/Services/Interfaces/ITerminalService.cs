using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ReControl.Desktop.Services.Interfaces;

/// <summary>
/// Terminal service abstraction with streaming session management.
/// Persistent shell sessions keyed by shell type, with real-time output streaming.
/// </summary>
public interface ITerminalService
{
    /// <summary>
    /// Starts a command in a persistent session for the given shell type.
    /// Returns the sessionId immediately. Output streams in real-time via the outputSender callback.
    /// </summary>
    string ExecuteAsync(string command, string shellType, Func<string, Task> outputSender, int timeoutMs = 30000);

    /// <summary>
    /// Queries the actual CWD from the shell session (sends pwd/cd and reads output).
    /// </summary>
    string GetCwd(string shellType);

    /// <summary>
    /// Sends a cd command to the shell session to change working directory.
    /// </summary>
    void SetCwd(string path, string shellType);

    /// <summary>
    /// Cross-platform user identity info (username + admin status).
    /// </summary>
    string WhoAmI();

    /// <summary>
    /// System uptime as a TimeSpan.
    /// </summary>
    TimeSpan GetUptime();

    /// <summary>
    /// Returns platform-specific available shells.
    /// </summary>
    List<string> GetAvailableShells();

    /// <summary>
    /// Kills the session for the given shell type, or all sessions if shellType is null.
    /// </summary>
    void Abort(string? shellType = null);

    /// <summary>
    /// Kills all active sessions. Called on WebSocket disconnect for clean slate.
    /// </summary>
    void DisposeAllSessions();
}
