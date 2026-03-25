using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ReControl.Desktop.AppStart;
using ReControl.Desktop.Services;

namespace ReControl.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private AppWindowManager? _windowManager;

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

        // Build DI container via bootstrapper
        Services = ServiceCollectionExtensions.BuildApplicationServices(
            onAuthFailure: () =>
            {
                var log = Services.GetRequiredService<LogService>();
                log.Warning("WebSocket auth failure: transitioning to login");
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _windowManager?.HandleLogout());
            });

        // Log startup
        var logService = Services.GetRequiredService<LogService>();
        logService.Info("ReControl Desktop starting up");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _windowManager = new AppWindowManager(this, Services, desktop);
            
            var startMinimized = desktop.Args?.Contains("--minimized") ?? false;
            _windowManager.Start(startMinimized);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

