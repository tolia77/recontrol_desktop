using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Token storage using Windows DPAPI (ProtectedData) for encryption.
/// Stores encrypted tokens in %APPDATA%/recontrol/tokens.dat.
/// </summary>
public class WindowsTokenStorageService : ITokenStorageService
{
    private readonly string _folderPath;
    private readonly string _filePath;
    private readonly LogService _log;
    private readonly object _cacheLock = new();
    private TokenData? _cachedData;
    private bool _cacheLoaded;

    public WindowsTokenStorageService(LogService log)
    {
        _log = log;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _folderPath = Path.Combine(appData, "recontrol");
        _filePath = Path.Combine(_folderPath, "tokens.dat");
    }

    public void Save(TokenData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Directory.CreateDirectory(_folderPath);

        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);

        File.WriteAllBytes(_filePath, protectedBytes);

        lock (_cacheLock)
        {
            _cachedData = data;
            _cacheLoaded = true;
        }
    }

    public TokenData? Load()
    {
        lock (_cacheLock)
        {
            if (_cacheLoaded)
                return _cachedData;
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                lock (_cacheLock)
                {
                    _cachedData = null;
                    _cacheLoaded = true;
                }
                return null;
            }

            var protectedBytes = File.ReadAllBytes(_filePath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(bytes);
            var data = JsonSerializer.Deserialize<TokenData>(json);

            lock (_cacheLock)
            {
                _cachedData = data;
                _cacheLoaded = true;
            }
            return data;
        }
        catch (Exception ex)
        {
            _log.Warning($"WindowsTokenStorageService.Load failed: {ex.Message}");
            lock (_cacheLock)
            {
                _cachedData = null;
                _cacheLoaded = true;
            }
            return null;
        }
    }

    public void Clear()
    {
        lock (_cacheLock)
        {
            _cachedData = null;
            _cacheLoaded = true;
        }

        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (Exception ex)
        {
            _log.Warning($"WindowsTokenStorageService.Clear failed: {ex.Message}");
        }
    }

    public string? GetAccessToken() => Load()?.AccessToken;
    public string? GetRefreshToken() => Load()?.RefreshToken;
    public string? GetDeviceId() => Load()?.DeviceId;
    public string? GetUserId() => Load()?.UserId;
}
