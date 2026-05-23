using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using ReControl.Desktop.Services;
using ReControl.Desktop.WebSocket;

namespace ReControl.Desktop.Tests.WebSocket;

public class WebSocketClientReconnectTests
{
    /// <summary>
    /// Regression test: a failed reconnect attempt used to cancel the loop's own
    /// backoff token. ConnectAsync cancels _cts via CloseInternalAsync on every
    /// attempt, and the loop was handed _cts.Token — so after attempt 1's connect
    /// failed, attempt 2's Task.Delay threw OperationCanceledException immediately
    /// and the loop aborted ("backoff wait cancelled on attempt 2"). The loop must
    /// instead survive failed attempts and keep retrying with exponential backoff.
    /// </summary>
    [Fact]
    public async Task ReconnectLoop_SurvivesFailedConnectAttempt()
    {
        var previousWsUrl = Environment.GetEnvironmentVariable("WS_URL");
        // Closed local port -> connection refused immediately on every attempt.
        Environment.SetEnvironmentVariable("WS_URL", "ws://127.0.0.1:59999/cable");
        try
        {
            var log = new LogService();
            using var client = new WebSocketClient(
                log,
                getAccessToken: () => Task.FromResult<string?>("test-token"),
                refreshTokens: () => Task.FromResult(true));

            var statuses = new ConcurrentQueue<string>();
            var reachedAttempt2PostDelay = new TaskCompletionSource();
            client.StatusMessage += msg =>
            {
                statuses.Enqueue(msg);
                // The policy emits this only AFTER attempt 2's backoff wait
                // completes — i.e. only if the loop survived attempt 1's failure.
                if (msg == "Reconnecting (attempt 2)...")
                    reachedAttempt2PostDelay.TrySetResult();
            };

            // Drive the reconnect path the same way a dropped connection does.
            var handleDisconnect = typeof(WebSocketClient).GetMethod(
                "HandleDisconnectAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            handleDisconnect.Should().NotBeNull("the private disconnect handler must exist");
            await (Task)handleDisconnect!.Invoke(client, new object?[] { null })!;

            // attempt 1 waits 1s, attempt 2 waits 2s -> the signal arrives ~3s in.
            var completed = await Task.WhenAny(
                reachedAttempt2PostDelay.Task,
                Task.Delay(TimeSpan.FromSeconds(15)));

            completed.Should().Be(reachedAttempt2PostDelay.Task,
                "the reconnect loop must continue past a failed connect attempt; " +
                $"statuses seen: [{string.Join(" | ", statuses)}]");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WS_URL", previousWsUrl);
        }
    }
}
