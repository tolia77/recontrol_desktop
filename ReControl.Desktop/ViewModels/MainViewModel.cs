using System;
using System.Text.Json;
using System.Threading.Channels;
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
/// ViewModel for the main window. Manages sidebar navigation between
/// Dashboard/Settings/Logs views, WebSocket connection lifecycle, and
/// routes incoming messages through the CommandDispatcher.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly LogService _log;
    private readonly WebSocketClient _webSocket;
    private readonly CommandDispatcher _dispatcher;

    private readonly DashboardViewModel _dashboardViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly LogsViewModel _logsViewModel;

    // Input commands (mouse.*/keyboard.*) MUST execute in the exact order received.
    // The receive path enqueues them synchronously (preserving order); a single
    // consumer drains FIFO. This prevents a fast mouse.up from overtaking a slow
    // mouse.down on the thread pool, which left the button stuck down (drag-on-tap).
    private readonly Channel<BaseRequest> _inputQueue =
        Channel.CreateUnbounded<BaseRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    private bool _initialized;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private int _selectedNavIndex;

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

    public MainViewModel(
        AuthService authService,
        LogService log,
        WebSocketClient webSocket,
        CommandDispatcher dispatcher,
        DashboardViewModel dashboardViewModel,
        SettingsViewModel settingsViewModel,
        LogsViewModel logsViewModel)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _dashboardViewModel = dashboardViewModel ?? throw new ArgumentNullException(nameof(dashboardViewModel));
        _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        _logsViewModel = logsViewModel ?? throw new ArgumentNullException(nameof(logsViewModel));

        // Start the ordered input-command consumer (lives for the VM lifetime).
        _ = ConsumeInputQueueAsync();

        // Wire WebSocket events
        _webSocket.ConnectionStatusChanged += OnConnectionStatusChanged;
        _webSocket.StatusMessage += OnStatusMessage;
        _webSocket.MessageReceived += OnMessageReceived;

        // Wire SettingsViewModel logout
        _settingsViewModel.LogoutRequested += () => _ = LogoutAsync();

        // Display device ID
        DeviceId = _authService.GetDeviceId() ?? "Unknown";

        // Default to Dashboard
        _currentPage = _dashboardViewModel;
        _selectedNavIndex = 0;
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentPage = value switch
        {
            0 => _dashboardViewModel,
            1 => _settingsViewModel,
            2 => _logsViewModel,
            _ => _dashboardViewModel
        };
    }

    /// <summary>
    /// Called after the view is loaded to initiate the WebSocket connection.
    /// Guarded against double invocation so it is safe to call both from
    /// App.axaml.cs (hidden startup) and from the Opened event handler.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        _log.Info("MainViewModel: initializing, auto-connecting WebSocket");
        _dashboardViewModel.UpdateConnectionStatus(connected: false, connecting: true);
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
                _dashboardViewModel.UpdateConnectionStatus(connected: true, connecting: false);
            }
            else if (ConnectionStatus != "Reconnecting...")
            {
                ConnectionStatus = "Disconnected";
                _dashboardViewModel.UpdateConnectionStatus(connected: false, connecting: false);
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
                _dashboardViewModel.UpdateConnectionStatus(connected: false, connecting: true);
            }

            _log.Info($"MainViewModel.StatusMessage: {message}");
        });
    }

    private void OnMessageReceived(string text)
    {
        // Parse + route synchronously on the receive thread so input commands keep
        // their received order. The payload JsonElement is cloned because dispatch
        // now happens after the JsonDocument 'using' scope (queued or on a Task).
        BaseRequest request;
        string command;
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

            command = GetStringProperty(message, "command", "");
            var id = GetIdProperty(message);
            var payload = message.TryGetProperty("payload", out var payloadProp)
                ? payloadProp.Clone()
                : default;

            if (string.IsNullOrEmpty(command))
            {
                _log.Warning("MainViewModel: message has no command field");
                return;
            }

            if (command == "device.deleted")
            {
                _log.Warning("MainViewModel: device was deleted on server, logging out");
                _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(LogoutAsync);
                return;
            }

            request = new BaseRequest
            {
                Id = id,
                Command = command,
                Payload = payload
            };
        }
        catch (Exception ex)
        {
            _log.Error("MainViewModel.OnMessageReceived", ex);
            return;
        }

        // Input commands run in strict received order via the single-consumer queue;
        // everything else stays on the concurrent path (terminal/file ops can be
        // long-running and must not block input). Enqueue happens here, synchronously
        // on the receive thread, so FIFO order matches wire order.
        if (command.StartsWith("mouse.", StringComparison.Ordinal) ||
            command.StartsWith("keyboard.", StringComparison.Ordinal))
        {
            _inputQueue.Writer.TryWrite(request);
        }
        else
        {
            _ = Task.Run(() => _dispatcher.HandleRequestAsync(request));
        }
    }

    /// <summary>
    /// Drains the input-command queue one at a time, in order. Serializing
    /// mouse/keyboard dispatch guarantees down→move→up execute in the sequence
    /// they were received (a faster handler can no longer overtake a slower one).
    /// </summary>
    private async Task ConsumeInputQueueAsync()
    {
        await foreach (var request in _inputQueue.Reader.ReadAllAsync())
        {
            try
            {
                await _dispatcher.HandleRequestAsync(request);
            }
            catch (Exception ex)
            {
                _log.Error("MainViewModel: input command failed", ex);
            }
        }
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
