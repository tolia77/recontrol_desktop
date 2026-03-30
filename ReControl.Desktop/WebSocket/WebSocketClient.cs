using System;
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
    private readonly SemaphoreSlim _reconnectGuard = new(1, 1);
    private volatile bool _disposed;
    private volatile bool _intentionalDisconnect;

    public event Action<string>? MessageReceived;
    public event Action<bool>? ConnectionStatusChanged;
    public event Action<string>? StatusMessage;

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

            // Subscribe before starting receive loop to avoid race condition:
            // if the server sends an immediate disconnect (e.g. expired token),
            // the receive loop would close the socket before we can send subscribe.
            var subscribeMessage = ActionCableProtocol.CreateSubscribeMessage();
            await SendAsync(subscribeMessage);

            // Start receive loop without awaiting (runs in background)
            _ = ReceiveLoopAsync(_ws, _cts.Token);

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
                        ConnectionStatusChanged?.Invoke(false);
                        if (!_intentionalDisconnect)
                        {
                            _ = HandleDisconnectAsync(reason: null);
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

                if (!IsHighFrequencyMessage(text))
                    _log.Info($"WebSocketClient.MessageReceived: {text}");
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
            ConnectionStatusChanged?.Invoke(false);
            if (!_intentionalDisconnect)
            {
                _ = HandleDisconnectAsync(reason: null);
            }
        }
        catch (Exception ex)
        {
            _log.Error("WebSocketClient.ReceiveLoop", ex);
            ConnectionStatusChanged?.Invoke(false);
            if (!_intentionalDisconnect)
            {
                _ = HandleDisconnectAsync(reason: null);
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
                    _ = HandleDisconnectAsync(control.DisconnectReason);
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
                cancellationToken: _cts.Token
            );
        }
        finally
        {
            _reconnectGuard.Release();
        }
    }

    public async Task SendAsync(string message)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            _log.Warning("WebSocketClient.SendAsync: not connected");
            throw new InvalidOperationException("WebSocket is not connected");
        }

        if (!IsHighFrequencyMessage(message))
            _log.Info($"WebSocketClient.SendAsync: {message}");
        var bytes = Encoding.UTF8.GetBytes(message);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
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
        await CloseInternalAsync();
        ConnectionStatusChanged?.Invoke(false);
    }

    private async Task CloseInternalAsync()
    {
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

    private static bool IsHighFrequencyMessage(string message) =>
        message.Contains("\"mouse.move\"") || message.Contains("\"request\":\"mouse.move\"");

    private void NotifyStatus(string message)
    {
        try { StatusMessage?.Invoke(message); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _intentionalDisconnect = true;

        try { _cts.Cancel(); } catch { }
        try { _cts.Dispose(); } catch { }
        try { _ws?.Dispose(); } catch { }
        _reconnectGuard.Dispose();
    }
}

