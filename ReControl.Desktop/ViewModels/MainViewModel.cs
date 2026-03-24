using System;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReControl.Desktop.Commands;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;
using ReControl.Desktop.WebSocket;

namespace ReControl.Desktop.ViewModels;

/// <summary>
/// ViewModel for the main window. Manages WebSocket connection lifecycle,
/// routes incoming messages through the CommandDispatcher, and displays
/// connection status in the UI.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly LogService _log;
    private readonly WebSocketClient _webSocket;
    private readonly CommandDispatcher _dispatcher;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _deviceId = string.Empty;

    /// <summary>
    /// Raised when logout completes so the app can transition back to login.
    /// </summary>
    public event Action? LogoutRequested;

    public MainViewModel(AuthService authService, LogService log, WebSocketClient webSocket, CommandDispatcher dispatcher)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        // Wire WebSocket events
        _webSocket.ConnectionStatusChanged += OnConnectionStatusChanged;
        _webSocket.StatusMessage += OnStatusMessage;
        _webSocket.MessageReceived += OnMessageReceived;

        // Display device ID
        DeviceId = _authService.GetDeviceId() ?? "Unknown";
    }

    /// <summary>
    /// Called after the view is loaded to initiate the WebSocket connection.
    /// </summary>
    public async Task InitializeAsync()
    {
        _log.Info("MainViewModel: initializing, auto-connecting WebSocket");
        ConnectionStatus = "Connecting...";
        await _webSocket.ConnectAsync();
    }

    private void OnConnectionStatusChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            if (connected)
            {
                ConnectionStatus = "Connected";
            }
            else if (ConnectionStatus != "Reconnecting...")
            {
                ConnectionStatus = "Disconnected";
            }
        });
    }

    private void OnStatusMessage(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (message.StartsWith("Reconnecting"))
            {
                ConnectionStatus = "Reconnecting...";
            }

            _log.Info($"MainViewModel.StatusMessage: {message}");
        });
    }

    private void OnMessageReceived(string text)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // ActionCable wraps channel data in a "message" property
                using var doc = JsonDocument.Parse(text);

                if (!doc.RootElement.TryGetProperty("message", out var message) ||
                    message.ValueKind != JsonValueKind.Object)
                {
                    _log.Info($"MainViewModel: non-command message: {text}");
                    return;
                }

                var command = GetStringProperty(message, "command", "");
                var id = GetIdProperty(message);
                var payload = message.TryGetProperty("payload", out var payloadProp)
                    ? payloadProp
                    : default;

                if (string.IsNullOrEmpty(command))
                {
                    _log.Warning("MainViewModel: message has no command field");
                    return;
                }

                var request = new BaseRequest
                {
                    Id = id,
                    Command = command,
                    Payload = payload
                };

                await _dispatcher.HandleRequestAsync(request);
            }
            catch (Exception ex)
            {
                _log.Error("MainViewModel.OnMessageReceived", ex);
            }
        });
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        _log.Info("Logout requested");

        // Disconnect WebSocket first
        _webSocket.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _webSocket.StatusMessage -= OnStatusMessage;
        _webSocket.MessageReceived -= OnMessageReceived;
        await _webSocket.DisconnectAsync();

        await _authService.LogoutAsync();
        ConnectionStatus = "Disconnected";
        IsConnected = false;
        LogoutRequested?.Invoke();
    }

    private static string GetStringProperty(JsonElement element, string propertyName, string defaultValue)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? defaultValue
            : defaultValue;
    }

    private static string? GetIdProperty(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idProp))
            return null;

        return idProp.ValueKind switch
        {
            JsonValueKind.String => idProp.GetString(),
            JsonValueKind.Number => idProp.GetRawText(),
            _ => null
        };
    }
}
