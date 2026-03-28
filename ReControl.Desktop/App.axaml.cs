using System;
using System.IO;
using System.Runtime.InteropServices;
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

    private static string? FindFFmpegLibPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return null;

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x86_64-linux-gnu",
            Architecture.Arm64 => "aarch64-linux-gnu",
            _ => null
        };

        string[] searchPaths = arch != null
            ? [$"/usr/lib/{arch}", "/usr/lib64", "/usr/lib"]
            : ["/usr/lib64", "/usr/lib"];

        foreach (var path in searchPaths)
        {
            if (Directory.Exists(path) &&
                Directory.GetFiles(path, "libavcodec.so*").Length > 0)
                return path;
        }

        return null;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load .env from the application base directory (not CWD)
        var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        if (File.Exists(envPath))
        {
            DotNetEnv.Env.Load(envPath, new DotNetEnv.LoadOptions(setEnvVars: true, clobberExistingVars: false));
        }

        // Build DI container via bootstrapper
        Services = ServiceCollectionExtensions.BuildApplicationServices(
            onAuthFailure: () =>
            {
                var log = Services.GetRequiredService<LogService>();
                log.Warning("WebSocket auth failure: transitioning to login");
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _windowManager?.HandleLogout());
            });

        // Initialize FFmpeg for video encoding (uses system FFmpeg libraries)
        SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(
            SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_WARNING,
            FindFFmpegLibPath());

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

