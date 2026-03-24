using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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
                    Dispatcher.UIThread.Post(() => ShowLoginWindow());
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

            // Startup auth flow: check for stored tokens
            var auth = Services.GetRequiredService<AuthService>();
            if (auth.HasStoredTokens())
            {
                logService.Info("Stored tokens found, showing main window");
                ShowMainWindow();
            }
            else
            {
                logService.Info("No stored tokens, showing login window");
                ShowLoginWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
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

        vm.LogoutRequested += () =>
        {
            window.Close();
            ShowLoginWindow();
        };

        _desktop.MainWindow = window;
        window.Show();
    }
}
