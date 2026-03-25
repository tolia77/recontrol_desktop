using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ReControl.Desktop.Commands;
using ReControl.Desktop.Platform;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;
using ReControl.Desktop.ViewModels;
using ReControl.Desktop.Views;
using ReControl.Desktop.WebSocket;

namespace ReControl.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _statusMenuItem;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private DateTime _lastTrayClick = DateTime.MinValue;
    private const int DoubleClickThresholdMs = 500;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load .env from the application base directory (not CWD)
        var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        if (File.Exists(envPath))
        {
            DotNetEnv.Env.Load(envPath);
        }

        // Build DI container
        var services = new ServiceCollection();

        // Register shared services
        services.AddSingleton<LogService>();

        // Register platform-specific services
        PlatformServices.Register(services);

        // Register API client -- getAccessToken and refreshTokens will be wired after AuthService is created
        AuthService? authService = null;
        services.AddSingleton<ApiClient>(sp =>
        {
            var baseUrl = Environment.GetEnvironmentVariable("API_BASE_URL")
                          ?? throw new InvalidOperationException("Environment variable 'API_BASE_URL' is not set.");
            var log = sp.GetRequiredService<LogService>();
            return new ApiClient(
                baseUrl,
                log,
                getAccessToken: () => authService?.GetAccessToken(),
                refreshTokens: () => authService?.RefreshTokensAsync() ?? System.Threading.Tasks.Task.FromResult(false));
        });

        // Register AuthService
        services.AddSingleton<AuthService>(sp =>
        {
            var apiClient = sp.GetRequiredService<ApiClient>();
            var tokenStorage = sp.GetRequiredService<ITokenStorageService>();
            var log = sp.GetRequiredService<LogService>();
            var systemInfo = sp.GetRequiredService<ISystemInfoService>();
            var svc = new AuthService(apiClient, tokenStorage, log, systemInfo);
            authService = svc;
            return svc;
        });

        // Register WebSocketClient -- uses AuthService closures for token access/refresh
        services.AddSingleton<WebSocketClient>(sp =>
        {
            var log = sp.GetRequiredService<LogService>();
            var auth = sp.GetRequiredService<AuthService>();
            return new WebSocketClient(
                log,
                getAccessToken: () => System.Threading.Tasks.Task.FromResult(auth.GetAccessToken()),
                refreshTokens: () => auth.RefreshTokensAsync(),
                onAuthFailure: () =>
                {
                    log.Warning("WebSocket auth failure: transitioning to login");
                    Dispatcher.UIThread.Post(() => HandleLogout());
                });
        });

        // Register CommandJsonParser
        services.AddSingleton<CommandJsonParser>();

        // Register CommandDispatcher -- uses WebSocketClient for sending responses
        WebSocketClient? wsClient = null;
        services.AddSingleton<CommandDispatcher>(sp =>
        {
            var jsonParser = sp.GetRequiredService<CommandJsonParser>();
            var log = sp.GetRequiredService<LogService>();
            var ws = sp.GetRequiredService<WebSocketClient>();
            wsClient = ws;
            return new CommandDispatcher(jsonParser, log, msg => ws.SendAsync(msg));
        });

        // Register ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LogsViewModel>();
        services.AddTransient<MainViewModel>();

        Services = services.BuildServiceProvider();

        // Eagerly resolve AuthService so the closure reference is set
        _ = Services.GetRequiredService<AuthService>();

        // Log startup
        var logService = Services.GetRequiredService<LogService>();
        logService.Info("ReControl Desktop starting up");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Set up system tray before any window
            SetupTrayIcon();

            // Parse --minimized flag for autostart-to-tray behavior
            var startMinimized = desktop.Args?.Contains("--minimized") ?? false;

            // Startup auth flow: check for stored tokens
            var auth = Services.GetRequiredService<AuthService>();
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
                // Must show login even if --minimized -- can't auto-auth without tokens
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

        base.OnFrameworkInitializationCompleted();
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

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
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

        _desktop?.Shutdown();
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

    private void ShowLoginWindow()
    {
        if (_desktop == null) return;

        var vm = Services.GetRequiredService<LoginViewModel>();
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
        if (_desktop == null) return;

        var vm = Services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = vm };

        _mainWindow = window;
        _mainViewModel = vm;

        vm.LogoutRequested += HandleLogout;

        // Subscribe to WebSocket connection status for tray icon updates
        var webSocket = Services.GetRequiredService<WebSocketClient>();
        webSocket.ConnectionStatusChanged += OnWebSocketConnectionChanged;
        webSocket.StatusMessage += OnWebSocketStatusMessage;

        _desktop.MainWindow = window;
        window.Show();
    }

    private void CreateMainWindowHidden()
    {
        if (_desktop == null) return;

        var vm = Services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = vm };

        _mainWindow = window;
        _mainViewModel = vm;

        vm.LogoutRequested += HandleLogout;

        // Subscribe to WebSocket connection status for tray icon updates
        var webSocket = Services.GetRequiredService<WebSocketClient>();
        webSocket.ConnectionStatusChanged += OnWebSocketConnectionChanged;
        webSocket.StatusMessage += OnWebSocketStatusMessage;

        _desktop.MainWindow = window;

        // Window is hidden, so Opened event won't fire. Initialize directly
        // to connect WebSocket in the background.
        _ = vm.InitializeAsync();
    }

    private void HandleLogout()
    {
        if (_mainWindow != null)
        {
            // Unsubscribe WebSocket events for tray updates
            var webSocket = Services.GetRequiredService<WebSocketClient>();
            webSocket.ConnectionStatusChanged -= OnWebSocketConnectionChanged;
            webSocket.StatusMessage -= OnWebSocketStatusMessage;

            _mainWindow.RequestQuit();
            _mainWindow.Close();
            _mainWindow = null;
            _mainViewModel = null;
        }

        // Reset tray to disconnected state
        UpdateTrayStatus(isConnected: false, isConnecting: false);

        ShowLoginWindow();
    }

    private void OnWebSocketConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateTrayStatus(isConnected: connected, isConnecting: false);
        });
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
}
