using System;
using Microsoft.Extensions.Configuration;

namespace ReControl.Desktop.AppStart;

/// <summary>
/// Application configuration resolved once at startup and injected as a singleton.
/// Replaces the former .env / DotNetEnv setup: values come from appsettings.json
/// (production defaults, always shipped), appsettings.Development.json (copied to the
/// output directory on Debug builds only), and finally environment variables.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Backend REST API base, e.g. https://example.com — required for the app to work.</summary>
    public string ApiBaseUrl { get; init; } = "";

    /// <summary>ActionCable endpoint, e.g. wss://example.com/cable.</summary>
    public string WsUrl { get; init; } = "";

    /// <summary>Web UI base for the "sign up" link; falls back to <see cref="ApiBaseUrl"/> when unset.</summary>
    public string? FrontendUrl { get; init; }

    /// <summary>Minimum recorded log level: debug | info | warn | error. Unset = debug.</summary>
    public string? LogLevel { get; init; }

    /// <summary>
    /// Builds configuration from the application base directory (not the CWD, which
    /// differs when the app is launched from a shortcut or an installed location).
    /// Both JSON files are optional so an environment-variable-only launch — e.g.
    /// run_guest.sh pointing a published build at another host — still works.
    /// </summary>
    public static AppConfig Load()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return new AppConfig
        {
            ApiBaseUrl = Resolve(config, "API_BASE_URL", "ReControl:ApiBaseUrl") ?? "",
            WsUrl = Resolve(config, "WS_URL", "ReControl:WsUrl") ?? "",
            FrontendUrl = Resolve(config, "FRONTEND_URL", "ReControl:FrontendUrl"),
            LogLevel = Resolve(config, "LOG_LEVEL", "ReControl:LogLevel"),
        };
    }

    /// <summary>
    /// Environment variables keep the flat legacy names (API_BASE_URL, WS_URL, ...) and win
    /// over both JSON files — checked first rather than relying on provider order, because
    /// the JSON keys are nested under "ReControl" and would not otherwise be overridden.
    /// </summary>
    private static string? Resolve(IConfiguration config, string envKey, string jsonKey)
    {
        var value = config[envKey];
        if (string.IsNullOrWhiteSpace(value))
            value = config[jsonKey];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
