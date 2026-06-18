using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReControl.Desktop.Services;
using ReControl.Desktop.WebSocket.Connection;
using ReControl.Desktop.WebSocket.Protocol;

namespace ReControl.Desktop.WebSocket;

/// <summary>
/// ActionCable WebSocket client with automatic reconnection using exponential backoff.
/// </summary>
public class WebSocketClient : IDisposable
{
    private readonly LogService _log;
    private readonly Func<Task<string?>> _getAccessToken;
    private readonly Func<Task<bool>> _refreshTokens;
    private readonly Action? _onAuthFailure;
    private readonly ReconnectionPolicy _reconnectionPolicy;

    private ClientWebSocket? _ws;
    private CancellationTokenSource _cts = new();
    // Lifecycle token for the reconnect loop. Kept separate from _cts because
    // ConnectAsync cancels and replaces _cts on every attempt; the loop must
    // survive across attempts and is only torn down on intentional disconnect
    // or dispose.
    private CancellationTokenSource _reconnectCts = new();
    private readonly SemaphoreSlim _reconnectGuard = new(1, 1);
    private volatile bool _disposed;
    private volatile bool _intentionalDisconnect;
    // Recurring application-level heartbeat. The backend CommandChannel's
    // "heartbeat" handler is the ONLY recurring writer of devices.last_active_at;
    // SweepStaleDevicesJob marks devices inactive after 60s of silence, so
    // without this timer every connected desktop flips inactive ~60s after
    // connecting. This is NOT redundant with the ActionCable server ping —
    // the protocol ping does not touch last_active_at.
    private Timer? _heartbeatTimer;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    // Periodic resource sampler: CPU% estimate + working-set memory.
    // Runs on a low cadence (5 s) and is never coupled to the per-frame path.
    private Timer? _resourceSamplerTimer;
    private static readonly TimeSpan ResourceSampleInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _lastCpuTime = TimeSpan.Zero;
    private DateTime _lastCpuSampleAt = DateTime.MinValue;
    private readonly object _resourceSamplerLock = new();

    public event Action<string>? MessageReceived;
    public event Action<bool>? ConnectionStatusChanged;
    public event Action<string>? StatusMessage;
    // Fires when the server confirms the CommandChannel subscription. Only after
    // this is it safe to send channel messages; sending earlier (e.g. on raw
    // connect) is rejected by ActionCable with "Unable to find subscription".
    public event Action? SubscriptionConfirmed;

    public WebSocketClient(
        LogService log,
        Func<Task<string?>> getAccessToken,
        Func<Task<bool>> refreshTokens,
        Action? onAuthFailure = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _getAccessToken = getAccessToken ?? throw new ArgumentNullException(nameof(getAccessToken));
        _refreshTokens = refreshTokens ?? throw new ArgumentNullException(nameof(refreshTokens));
        _onAuthFailure = onAuthFailure;
        _reconnectionPolicy = new ReconnectionPolicy(_log);
        StartResourceSampler();
    }

    public async Task<bool> ConnectAsync()
    {
        if (_disposed) return false;
        _intentionalDisconnect = false;

        var token = await _getAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.Warning("WebSocketClient.ConnectAsync: no access token available");
            NotifyStatus("No access token available");
            return false;
        }

        var wsUrl = Environment.GetEnvironmentVariable("WS_URL");
        if (string.IsNullOrWhiteSpace(wsUrl))
        {
            _log.Error("WebSocketClient.ConnectAsync: WS_URL environment variable is not set");
            NotifyStatus("WS_URL not configured");
            return false;
        }

        try
        {
            await CloseInternalAsync();

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            var uri = new Uri($"{wsUrl}?access_token={Uri.EscapeDataString(token)}");
            _log.Info($"WebSocketClient.ConnectAsync: connecting to {wsUrl}");

            await _ws.ConnectAsync(uri, _cts.Token);

            ConnectionStatusChanged?.Invoke(true);
            NotifyStatus("Connected");
            _log.Info("WebSocketClient.ConnectAsync: connected successfully");
            _log.Info($"socket_connected url={wsUrl}");

            // Subscribe before starting receive loop to avoid race condition:
            // if the server sends an immediate disconnect (e.g. expired token),
            // the receive loop would close the socket before we can send subscribe.
            var subscribeMessage = ActionCableProtocol.CreateSubscribeMessage();
            await SendAsync(subscribeMessage);

            // Start receive loop without awaiting (runs in background)
            _ = ReceiveLoopAsync(_ws, _cts.Token)
                .ContinueWith(
                    t => _log.Error($"WebSocketClient.ReceiveLoop faulted: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);

            StartHeartbeat();

            return true;
        }
        catch (OperationCanceledException)
        {
            _log.Info("WebSocketClient.ConnectAsync: connection cancelled");
            return false;
        }
        catch (Exception ex)
        {
            _log.Error("WebSocketClient.ConnectAsync", ex);
            NotifyStatus($"Connection failed: {ex.Message}");
            ConnectionStatusChanged?.Invoke(false);
            return false;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult? result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _log.Info("WebSocketClient.ReceiveLoop: server sent close frame");
                        _log.Info("socket_disconnected reason=close_frame");
                        ConnectionStatusChanged?.Invoke(false);
                        if (!_intentionalDisconnect)
                        {
                            _ = HandleDisconnectAsync(reason: null)
                                .ContinueWith(
                                    t => _log.Error($"WebSocketClient.HandleDisconnect faulted: {t.Exception?.GetBaseException().Message}"),
                                    TaskContinuationOptions.OnlyOnFaulted);
                        }
                        return;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                var text = sb.ToString();

                var controlResult = ActionCableProtocol.TryHandleControlMessage(text);
                if (controlResult.IsControlMessage)
                {
                    HandleControlMessage(controlResult);
                    continue;
                }

                MessageReceived?.Invoke(text);
            }
        }
        catch (OperationCanceledException)
        {
            _log.Info("WebSocketClient.ReceiveLoop: cancelled");
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _log.Warning($"WebSocketClient.ReceiveLoop: connection closed prematurely");
            _log.Info($"socket_disconnected reason=connection_closed_prematurely exType={ex.GetType().Name}");
            ConnectionStatusChanged?.Invoke(false);
            if (!_intentionalDisconnect)
            {
                _ = HandleDisconnectAsync(reason: null)
                    .ContinueWith(
                        t => _log.Error($"WebSocketClient.HandleDisconnect faulted: {t.Exception?.GetBaseException().Message}"),
                        TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (Exception ex)
        {
            _log.Error("WebSocketClient.ReceiveLoop", ex);
            _log.Info($"socket_disconnected reason=receive_error exType={ex.GetType().Name}");
            ConnectionStatusChanged?.Invoke(false);
            if (!_intentionalDisconnect)
            {
                _ = HandleDisconnectAsync(reason: null)
                    .ContinueWith(
                        t => _log.Error($"WebSocketClient.HandleDisconnect faulted: {t.Exception?.GetBaseException().Message}"),
                        TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }

    private void HandleControlMessage(ControlMessageResult control)
    {
        switch (control.Type)
        {
            case ControlMessageType.Welcome:
                _log.Info("WebSocketClient: welcome received");
                NotifyStatus("Welcome received");
                break;
            case ControlMessageType.Ping:
                break;
            case ControlMessageType.ConfirmSubscription:
                _log.Info("WebSocketClient: subscription confirmed");
                NotifyStatus("Subscribed to CommandChannel");
                try { SubscriptionConfirmed?.Invoke(); } catch { }
                break;
            case ControlMessageType.RejectSubscription:
                _log.Warning("WebSocketClient: subscription rejected");
                NotifyStatus("Subscription rejected");
                break;
            case ControlMessageType.Disconnect:
                _log.Info($"WebSocketClient: disconnect received (reason={control.DisconnectReason}, reconnect={control.Reconnect}, unauthorized={control.Unauthorized})");
                NotifyStatus($"Server disconnect: {control.DisconnectReason ?? "unknown"}");
                if (control.Unauthorized || control.Reconnect)
                {
                    _ = HandleDisconnectAsync(control.DisconnectReason)
                        .ContinueWith(
                            t => _log.Error($"WebSocketClient.HandleDisconnect faulted: {t.Exception?.GetBaseException().Message}"),
                            TaskContinuationOptions.OnlyOnFaulted);
                }
                break;
        }
    }

    private async Task HandleDisconnectAsync(string? reason)
    {
        if (_disposed || _intentionalDisconnect) return;

        if (!await _reconnectGuard.WaitAsync(0))
        {
            return;
        }

        try
        {
            await CloseInternalAsync();
            // CloseInternalAsync cancels _cts to stop the in-flight ReceiveLoop.
            // Replace it with a fresh source so the unauthorized-path ConnectAsync
            // below and subsequent sends have a live per-connection token.
            // Deliberately NOT disposed here: an in-flight SendAsync may be
            // reading the old source's Token concurrently; a cancelled,
            // unreferenced CTS is simply garbage-collected, so eager disposal
            // buys nothing and risks ObjectDisposedException in senders.
            _cts = new CancellationTokenSource();
            ConnectionStatusChanged?.Invoke(false);

            bool unauthorized = !string.IsNullOrWhiteSpace(reason) &&
                (reason!.Contains("unauth", StringComparison.OrdinalIgnoreCase) ||
                 reason.Contains("token", StringComparison.OrdinalIgnoreCase));

            if (unauthorized)
            {
                _log.Info("WebSocketClient: unauthorized disconnect, attempting token refresh");
                NotifyStatus("Refreshing authentication...");

                try
                {
                    var refreshed = await _refreshTokens();
                    if (!refreshed)
                    {
                        _log.Warning("WebSocketClient: token refresh failed, invoking auth failure");
                        NotifyStatus("Authentication failed");
                        _onAuthFailure?.Invoke();
                        return;
                    }

                    _log.Info("WebSocketClient: token refresh succeeded, reconnecting");
                    var connected = await ConnectAsync();
                    if (connected) return;
                }
                catch (Exception ex)
                {
                    _log.Error("WebSocketClient.HandleDisconnect: refresh error", ex);
                    _onAuthFailure?.Invoke();
                    return;
                }
            }

            // Start a fresh reconnect lifecycle token, cancelling any prior loop.
            // This token is deliberately NOT _cts: ConnectAsync (invoked by the
            // loop on each attempt) cancels _cts via CloseInternalAsync, which
            // would otherwise tear down the loop's own backoff wait after the
            // first failed attempt.
            _reconnectCts.Cancel();
            try { _reconnectCts.Dispose(); } catch { }
            _reconnectCts = new CancellationTokenSource();

            // Fire off reconnection loop using policy
            _ = _reconnectionPolicy.ReconnectLoopAsync(
                tryConnect: ConnectAsync,
                tryRefreshAuth: async () =>
                {
                    var tk = await _getAccessToken();
                    if (string.IsNullOrWhiteSpace(tk)) return await _refreshTokens();
                    return true;
                },
                onAuthFailure: () => _onAuthFailure?.Invoke(),
                statusNotifier: NotifyStatus,
                isDisposedOrIntentional: () => _disposed || _intentionalDisconnect,
                cancellationToken: _reconnectCts.Token
            ).ContinueWith(
                t => _log.Error($"WebSocketClient.ReconnectLoop faulted: {t.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        finally
        {
            _reconnectGuard.Release();
        }
    }

    public async Task SendAsync(string message)
    {
        // Snapshot both fields: _ws can be nulled and _cts disposed/replaced
        // concurrently by HandleDisconnectAsync/Dispose between the state
        // check and the send. Reading a field once keeps this method working
        // against a consistent pair even if the connection turns over mid-call.
        var ws = _ws;
        var cts = _cts;
        if (ws == null || ws.State != WebSocketState.Open)
        {
            _log.Warning("WebSocketClient.SendAsync: not connected");
            throw new InvalidOperationException("WebSocket is not connected");
        }

        var bytes = Encoding.UTF8.GetBytes(message);
        CancellationToken token;
        try
        {
            token = cts.Token;
        }
        catch (ObjectDisposedException)
        {
            // Dispose() raced us; surface the socket-shaped failure callers
            // expect instead of an unexpected ObjectDisposedException.
            throw new InvalidOperationException("WebSocket is not connected");
        }
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    public async Task SendObjectAsync(object obj)
    {
        var json = ActionCableFormatter.Serialize(obj);
        await SendAsync(json);
    }

    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;
        _log.Info("WebSocketClient.DisconnectAsync called");
        try { _reconnectCts.Cancel(); } catch { }
        await CloseInternalAsync();
        ConnectionStatusChanged?.Invoke(false);
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        _heartbeatTimer = new Timer(_ => _ = SendHeartbeatAsync(), null, HeartbeatInterval, HeartbeatInterval);
    }

    private void StopHeartbeat()
    {
        try { _heartbeatTimer?.Dispose(); } catch { }
        _heartbeatTimer = null;
    }

    // -------------------------------------------------------------------------
    // Periodic resource sampler
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the periodic CPU/memory resource sampler. Guard against double-start via _resourceSamplerLock.
    /// Runs at ResourceSampleInterval (~5 s) cadence — never per-frame.
    /// </summary>
    private void StartResourceSampler()
    {
        lock (_resourceSamplerLock)
        {
            if (_resourceSamplerTimer != null) return; // guard against double-start

            // Capture initial CPU baseline so the first sample computes a valid delta.
            try
            {
                var proc = Process.GetCurrentProcess();
                _lastCpuTime = proc.TotalProcessorTime;
                _lastCpuSampleAt = DateTime.UtcNow;
            }
            catch
            {
                // If initial sample fails, the first interval will report 0% CPU — acceptable.
            }

            _resourceSamplerTimer = new Timer(SampleResourcesCallback, null, ResourceSampleInterval, ResourceSampleInterval);
        }
    }

    private void StopResourceSampler()
    {
        lock (_resourceSamplerLock)
        {
            try { _resourceSamplerTimer?.Dispose(); } catch { }
            _resourceSamplerTimer = null;
        }
    }

    private void SampleResourcesCallback(object? state)
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            proc.Refresh(); // refresh cached values before reading

            var nowCpu = proc.TotalProcessorTime;
            var now = DateTime.UtcNow;

            double cpuPercent = 0.0;
            double elapsedSec = (now - _lastCpuSampleAt).TotalSeconds;
            if (elapsedSec > 0 && _lastCpuSampleAt != DateTime.MinValue)
            {
                double cpuDeltaSec = (nowCpu - _lastCpuTime).TotalSeconds;
                // Divide by number of logical processors to get 0–100% on a per-core basis.
                int processorCount = Math.Max(1, Environment.ProcessorCount);
                cpuPercent = Math.Round((cpuDeltaSec / (elapsedSec * processorCount)) * 100.0, 1);
            }

            _lastCpuTime = nowCpu;
            _lastCpuSampleAt = now;

            double memoryMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 1);
            _log.Info($"resource cpuPercent={cpuPercent} memoryMb={memoryMb}");
        }
        catch (Exception ex)
        {
            _log.Warning($"WebSocketClient.SampleResources: {ex.Message}");
        }
    }

    private async Task SendHeartbeatAsync()
    {
        if (_ws?.State != WebSocketState.Open) return;
        try
        {
            var msg = ActionCableProtocol.CreateChannelMessage(new { command = "heartbeat" });
            await SendAsync(msg);
        }
        catch (Exception ex)
        {
            _log.Warning($"WebSocketClient.Heartbeat: {ex.Message}");
        }
    }

    private async Task CloseInternalAsync()
    {
        StopHeartbeat();
        _cts.Cancel();

        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived || _ws.State == WebSocketState.CloseSent)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", closeCts.Token);
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"WebSocketClient.CloseInternal: {ex.Message}");
            }

            try { _ws.Dispose(); } catch { }
            _ws = null;
        }
    }

    private void NotifyStatus(string message)
    {
        try { StatusMessage?.Invoke(message); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _intentionalDisconnect = true;

        StopHeartbeat();
        StopResourceSampler();
        try { _reconnectCts.Cancel(); } catch { }
        try { _reconnectCts.Dispose(); } catch { }
        try { _cts.Cancel(); } catch { }
        try { _cts.Dispose(); } catch { }
        try { _ws?.Dispose(); } catch { }
        _reconnectGuard.Dispose();
    }
}

