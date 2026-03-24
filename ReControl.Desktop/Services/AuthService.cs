using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Services;

/// <summary>
/// Handles login, token refresh, and logout against the backend auth API.
/// Ported from WPF AuthService with platform info additions.
/// </summary>
public class AuthService : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ITokenStorageService _tokenStorage;
    private readonly LogService _log;
    private readonly ISystemInfoService _systemInfo;

    public ApiClient ApiClient => _apiClient;

    public AuthService(ApiClient apiClient, ITokenStorageService tokenStorage, LogService log, ISystemInfoService systemInfo)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        _log.Info($"AuthService.LoginAsync called: email={email}");

        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentNullException(nameof(email));
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentNullException(nameof(password));

        var storedDeviceId = _tokenStorage.GetDeviceId();

        object payload = !string.IsNullOrWhiteSpace(storedDeviceId)
            ? new
            {
                email,
                password,
                device_id = storedDeviceId,
                client_type = "desktop",
                platform_name = _systemInfo.GetPlatformName(),
                platform_version = _systemInfo.GetPlatformVersion()
            }
            : new
            {
                email,
                password,
                device_name = _systemInfo.GetMachineName(),
                client_type = "desktop",
                platform_name = _systemInfo.GetPlatformName(),
                platform_version = _systemInfo.GetPlatformVersion()
            };

        var content = JsonContent.Create(payload, options: new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var response = await _apiClient.PostAsync("/auth/login", content);

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var userId = root.GetProperty("user_id").GetString() ?? string.Empty;
            var deviceId = root.GetProperty("device_id").GetString() ?? string.Empty;
            var accessToken = StripBearerPrefix(root.GetProperty("access_token").GetString() ?? string.Empty);
            var refreshToken = StripBearerPrefix(root.GetProperty("refresh_token").GetString() ?? string.Empty);

            _tokenStorage.Save(new TokenData(userId, deviceId, accessToken, refreshToken));
            _log.Info($"AuthService.LoginAsync success: userId={userId}, deviceId={deviceId}");
            return true;
        }

        _log.Warning($"AuthService.LoginAsync failed: status={response.StatusCode}");
        return false;
    }

    public async Task<bool> RefreshTokensAsync()
    {
        _log.Info("AuthService.RefreshTokensAsync called");

        var refreshToken = _tokenStorage.GetRefreshToken();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            _log.Warning("AuthService.RefreshTokensAsync: no refresh token");
            return false;
        }

        var headers = new Dictionary<string, string> { { "Refresh-Token", refreshToken } };
        var empty = new StringContent(string.Empty);

        var response = await _apiClient.PostAsync("/auth/refresh", empty, headers);
        if (!response.IsSuccessStatusCode)
        {
            _log.Warning($"AuthService.RefreshTokensAsync failed: status={response.StatusCode}");
            return false;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var newAccess = StripBearerPrefix(root.GetProperty("access_token").GetString() ?? string.Empty);
        var newRefresh = root.TryGetProperty("refresh_token", out var rt)
            ? StripBearerPrefix(rt.GetString() ?? string.Empty)
            : refreshToken;

        if (string.IsNullOrWhiteSpace(newAccess))
        {
            _log.Warning("AuthService.RefreshTokensAsync: no new access token");
            return false;
        }

        var current = _tokenStorage.Load();
        if (current != null)
        {
            var updated = new TokenData(current.UserId, current.DeviceId, newAccess, newRefresh ?? current.RefreshToken);
            _tokenStorage.Save(updated);
        }

        _log.Info("AuthService.RefreshTokensAsync succeeded");
        return true;
    }

    public async Task LogoutAsync()
    {
        _log.Info("AuthService.LogoutAsync called");

        try
        {
            var accessToken = _tokenStorage.GetAccessToken();
            var refreshToken = _tokenStorage.GetRefreshToken();

            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(refreshToken))
                headers["Refresh-Token"] = refreshToken;

            var empty = new StringContent(string.Empty);
            await _apiClient.PostAsync("/auth/logout", empty, headers);
        }
        catch (Exception ex)
        {
            _log.Warning($"AuthService.LogoutAsync request failed: {ex.Message}");
        }
        finally
        {
            _tokenStorage.Clear();
            _log.Info("AuthService.LogoutAsync: tokens cleared");
        }
    }

    public string? GetAccessToken() => _tokenStorage.GetAccessToken();
    public string? GetRefreshToken() => _tokenStorage.GetRefreshToken();
    public string? GetDeviceId() => _tokenStorage.GetDeviceId();
    public string? GetUserId() => _tokenStorage.GetUserId();
    public TokenData? GetTokenData() => _tokenStorage.Load();
    public bool HasStoredTokens() => _tokenStorage.Load() != null;

    private static string StripBearerPrefix(string token)
    {
        const string prefix = "Bearer ";
        return token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? token[prefix.Length..]
            : token;
    }

    public void Dispose() => _apiClient.Dispose();
}
