using System;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Threading;
using ReControl.Desktop.Commands;
using ReControl.Desktop.Models;
using ReControl.Desktop.Platform;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Files;
using ReControl.Desktop.Services.Interfaces;
using ReControl.Desktop.ViewModels;
using ReControl.Desktop.WebSocket;

namespace ReControl.Desktop.AppStart;

public static class ServiceCollectionExtensions
{
    public static IServiceProvider BuildApplicationServices(Action onAuthFailure)
    {
        var services = new ServiceCollection();

        // Register shared services
        services.AddSingleton<LogService>();
        services.AddSingleton<AllowlistService>();

        // Register platform-specific services
        PlatformServices.Register(services);

        // Register API client
        AuthService? authService = null;
        services.AddSingleton<ApiClient>(sp =>
        {
            var baseUrl = Environment.GetEnvironmentVariable("API_BASE_URL")
                          ?? throw new InvalidOperationException("Environment variable 'API_BASE_URL' is not set.");
            var log = sp.GetRequiredService<LogService>();
            
            // ReSharper disable AccessToModifiedClosure
            return new ApiClient(
                baseUrl,
                log,
                getAccessToken: () => authService?.GetAccessToken(),
                refreshTokens: () => authService?.RefreshTokensAsync() ?? System.Threading.Tasks.Task.FromResult(false));
            // ReSharper restore AccessToModifiedClosure
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

        // Register WebSocketClient
        services.AddSingleton<WebSocketClient>(sp =>
        {
            var log = sp.GetRequiredService<LogService>();
            var auth = sp.GetRequiredService<AuthService>();
            return new WebSocketClient(
                log,
                getAccessToken: () => System.Threading.Tasks.Task.FromResult(auth.GetAccessToken()),
                refreshTokens: () => auth.RefreshTokensAsync(),
                onAuthFailure: onAuthFailure);
        });

        // Register CommandJsonParser
        services.AddSingleton<CommandJsonParser>();

        // Register TerminalService
        services.AddSingleton<ITerminalService>(sp =>
        {
            var log = sp.GetRequiredService<LogService>();
            return new TerminalService(log);
        });

        // Register ProcessService
        services.AddSingleton<ProcessService>();

        // Register InputStateTracker
        services.AddSingleton<InputStateTracker>();

        // Register CommandDispatcher
        services.AddSingleton<CommandDispatcher>(sp =>
        {
            var jsonParser = sp.GetRequiredService<CommandJsonParser>();
            var log = sp.GetRequiredService<LogService>();
            var ws = sp.GetRequiredService<WebSocketClient>();
            var terminal = sp.GetRequiredService<ITerminalService>();
            var processService = sp.GetRequiredService<ProcessService>();
            var power = sp.GetRequiredService<IPowerService>();
            var keyboard = sp.GetRequiredService<IKeyboardService>();
            var mouse = sp.GetRequiredService<IMouseService>();
            var inputTracker = sp.GetRequiredService<InputStateTracker>();
            var screenCapture = sp.GetService<IScreenCaptureService>();
            var allowlist = sp.GetRequiredService<AllowlistService>();

            return new CommandDispatcher(jsonParser, log, msg => ws.SendAsync(msg), terminal, processService, power, keyboard, mouse, inputTracker, allowlist, screenCapture);
        });

        // Register ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LogsViewModel>();
        services.AddTransient<MainViewModel>();

        var serviceProvider = services.BuildServiceProvider();

        // Eagerly resolve AuthService so the closure reference is set
        _ = serviceProvider.GetRequiredService<AuthService>();

        return serviceProvider;
    }
}
