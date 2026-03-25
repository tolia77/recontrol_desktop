using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;
using ReControl.Desktop.ViewModels;
using ReControl.Desktop.Views;
using ReControl.Desktop.WebSocket;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.AppStart;

public class AppWindowManager : IDisposable
{
    private readonly Application _app;
    private readonly IServiceProvider _services;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _statusMenuItem;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private DateTime _lastTrayClick = DateTime.MinValue;
    private const int DoubleClickThresholdMs = 500;

    public AppWindowManager(Application app, IServiceProvider services, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _app = app;
        _services = services;
        _desktop = desktop;
        _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    public void Start(bool startMinimized)
    {
        SetupTrayIcon();

        var auth = _services.GetRequiredService<AuthService>();
        var logService = _services.GetRequiredService<LogService>();

        if (auth.HasStoredTokens())
        {
            if (startMinimized)
            {
                logService.Info("Started minimized to tray (stored tokens)");
                CreateMainWindowHidden();
            }
            else
            {
                logService.Info("Stored tokens found, showing main window");
                ShowMainWindow();
            }
        }
        else
        {
            if (startMinimized)
            {
                logService.Info("--minimized ignored: no stored tokens, login required");
            }
            else
            {
                logService.Info("No stored tokens, showing login window");
            }
            ShowLoginWindow();
        }
    }

    private void SetupTrayIcon()
    {
        _statusMenuItem = new NativeMenuItem("Status: Disconnected") { IsEnabled = false };

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(
                new Uri("avares://ReControl.Desktop/Assets/tray-disconnected.png"))),
            ToolTipText = "ReControl - Disconnected",
            IsVisible = true,
            Menu = new NativeMenu
            {
                new NativeMenuItem("Show/Hide") { Command = new RelayCommand(ToggleMainWindow) },
                new NativeMenuItemSeparator(),
                _statusMenuItem,
                new NativeMenuItemSeparator(),
                new NativeMenuItem("Quit") { Command = new RelayCommand(QuitApplication) },
            }
        };

        _trayIcon.Clicked += OnTrayIconClicked;

        TrayIcon.SetIcons(_app, new TrayIcons { _trayIcon });
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTrayClick).TotalMilliseconds <= DoubleClickThresholdMs)
        {
            RestoreMainWindow();
            _lastTrayClick = DateTime.MinValue;
        }
        else
        {
            _lastTrayClick = now;
        }
    }

    private void ToggleMainWindow()
    {
        if (_mainWindow == null || !_mainWindow.IsVisible)
        {
            RestoreMainWindow();
        }
        else
        {
            _mainWindow.Hide();
        }
    }

    private void RestoreMainWindow()
    {
        if (_mainWindow == null) return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void QuitApplication()
    {
        if (_mainWindow != null)
        {
            _mainWindow.RequestQuit();
            _mainWindow.Close();
            _mainWindow = null;
            _mainViewModel = null;
        }

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _desktop.Shutdown();
    }

    public void UpdateTrayStatus(bool isConnected, bool isConnecting)
    {
        if (_trayIcon == null) return;

        string iconPath;
        string statusText;

        if (isConnected)
        {
            iconPath = "avares://ReControl.Desktop/Assets/tray-connected.png";
            statusText = "Connected";
        }
        else if (isConnecting)
        {
            iconPath = "avares://ReControl.Desktop/Assets/tray-connecting.png";
            statusText = "Connecting...";
        }
        else
        {
            iconPath = "avares://ReControl.Desktop/Assets/tray-disconnected.png";
            statusText = "Disconnected";
        }

        _trayIcon.Icon = new WindowIcon(AssetLoader.Open(new Uri(iconPath)));
        _trayIcon.ToolTipText = $"ReControl - {statusText}";
        if (_statusMenuItem != null)
            _statusMenuItem.Header = $"Status: {statusText}";
    }

    public void HandleLogout()
    {
        if (_mainWindow != null)
        {
            var webSocket = _services.GetRequiredService<WebSocketClient>();
            webSocket.ConnectionStatusChanged -= OnWebSocketConnectionChanged;
            webSocket.ConnectionStatusChanged -= OnWebSocketDisconnectCleanup;
            webSocket.StatusMessage -= OnWebSocketStatusMessage;

            _mainWindow.RequestQuit();
            _mainWindow.Close();
            _mainWindow = null;
            _mainViewModel = null;
        }

        UpdateTrayStatus(isConnected: false, isConnecting: false);
        ShowLoginWindow();
    }

    private void ShowLoginWindow()
    {
        var vm = _services.GetRequiredService<LoginViewModel>();
        var window = new LoginWindow { DataContext = vm };

        vm.LoginSucceeded += () =>
        {
            window.Close();
            ShowMainWindow();
        };

        _desktop.MainWindow = window;
        window.Show();
    }

    private void ShowMainWindow()
    {
        var vm = _services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = vm };

        _mainWindow = window;
        _mainViewModel = vm;

        vm.LogoutRequested += HandleLogout;

        var webSocket = _services.GetRequiredService<WebSocketClient>();
        webSocket.ConnectionStatusChanged += OnWebSocketConnectionChanged;
        webSocket.ConnectionStatusChanged += OnWebSocketDisconnectCleanup;
        webSocket.StatusMessage += OnWebSocketStatusMessage;

        _desktop.MainWindow = window;
        window.Show();
    }

    private void CreateMainWindowHidden()
    {
        var vm = _services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = vm };

        _mainWindow = window;
        _mainViewModel = vm;

        vm.LogoutRequested += HandleLogout;

        var webSocket = _services.GetRequiredService<WebSocketClient>();
        webSocket.ConnectionStatusChanged += OnWebSocketConnectionChanged;
        webSocket.ConnectionStatusChanged += OnWebSocketDisconnectCleanup;
        webSocket.StatusMessage += OnWebSocketStatusMessage;

        _desktop.MainWindow = window;

        _ = vm.InitializeAsync();
    }

    private void OnWebSocketConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateTrayStatus(isConnected: connected, isConnecting: false);
        });

        if (connected)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var keyboard = _services.GetRequiredService<IKeyboardService>();
                    var ws = _services.GetRequiredService<WebSocketClient>();
                    var capabilityData = new
                    {
                        action = "capability",
                        xtest_available = keyboard.IsXtestAvailable
                    };
                    var message = ActionCableProtocol.CreateChannelMessage(capabilityData);
                    await ws.SendAsync(message);
                }
                catch (Exception ex)
                {
                    var log = _services.GetRequiredService<LogService>();
                    log.Warning($"Failed to send XTEST capability: {ex.Message}");
                }
            });
        }
    }

    private void OnWebSocketDisconnectCleanup(bool connected)
    {
        if (!connected)
        {
            try
            {
                var terminal = _services.GetRequiredService<ITerminalService>();
                terminal.DisposeAllSessions();
            }
            catch (Exception ex)
            {
                var log = _services.GetRequiredService<LogService>();
                log.Warning($"Terminal cleanup on disconnect failed: {ex.Message}");
            }

            try
            {
                var inputTracker = _services.GetRequiredService<InputStateTracker>();
                var keyboard = _services.GetRequiredService<IKeyboardService>();
                var mouse = _services.GetRequiredService<IMouseService>();
                inputTracker.ReleaseAll(keyboard, mouse);
            }
            catch (Exception ex)
            {
                var log = _services.GetRequiredService<LogService>();
                log.Warning($"Input cleanup on disconnect failed: {ex.Message}");
            }
        }
    }

    private void OnWebSocketStatusMessage(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (message.StartsWith("Reconnecting"))
            {
                UpdateTrayStatus(isConnected: false, isConnecting: true);
            }
        });
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }
}