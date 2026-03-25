using System;
using System.Threading;
using System.Threading.Tasks;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.WebSocket.Connection;

/// <summary>
/// Provides exponential backoff reconnection strategies.
/// </summary>
public class ReconnectionPolicy
{
    private const int MaxDelaySeconds = 30;
    
    private readonly LogService _log;
    
    public ReconnectionPolicy(LogService log)
    {
        _log = log;
    }

    /// <summary>
    /// Executes the reconnection loop with exponential backoff (1s, 2s, 4s, 8s, 16s, max 30s)
    /// Returns true if successful, false if cancelled/aborted.
    /// </summary>
    public async Task<bool> ReconnectLoopAsync(
        Func<Task<bool>> tryConnect, 
        Func<Task<bool>> tryRefreshAuth,
        Action onAuthFailure,
        Action<string>? statusNotifier,
        Func<bool> isDisposedOrIntentional,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        
        while (!isDisposedOrIntentional())
        {
            attempt++;
            int delaySeconds = Math.Min((int)Math.Pow(2, attempt - 1), MaxDelaySeconds);

            statusNotifier?.Invoke($"Reconnecting in {delaySeconds}s (attempt {attempt})...");
            _log.Info($"ReconnectionPolicy: attempt {attempt}, waiting {delaySeconds}s");

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            
            if (isDisposedOrIntentional()) return false;

            statusNotifier?.Invoke($"Reconnecting (attempt {attempt})...");

            // Always try auth refresh before attempting to connect to ensure our tokens are fresh
            bool authSuccessful = false;
            try
            {
                // In context we would check if we HAVE tokens here natively, 
                // but delegating back out to the main caller simplifies decoupling.
                authSuccessful = await tryRefreshAuth();
                if (!authSuccessful)
                {
                     _log.Warning("ReconnectionPolicy: Auth refresh returned false, raising auth failure.");
                     onAuthFailure();
                     return false;
                }
            }
            catch (Exception ex)
            {
                 _log.Error("ReconnectionPolicy: Auth refresh error", ex);
                 onAuthFailure();
                 return false;
            }

            var connected = await tryConnect();
            if (connected)
            {
                _log.Info($"ReconnectionPolicy: reconnected on attempt {attempt}");
                statusNotifier?.Invoke("Reconnected");
                return true;
            }
        }
        
        return false;
    }
}