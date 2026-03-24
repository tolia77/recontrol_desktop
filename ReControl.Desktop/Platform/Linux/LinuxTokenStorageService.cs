using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Linux;

/// <summary>
/// Token storage using AES-256-CBC encryption with key derived from /etc/machine-id + username.
/// Stores encrypted tokens in ~/.local/share/recontrol/tokens.dat.
/// </summary>
public class LinuxTokenStorageService : ITokenStorageService
{
    private const int KeySize = 32; // AES-256
    private const int IvSize = 16;  // AES block size
    private const int Pbkdf2Iterations = 100_000;

    private readonly string _folderPath;
    private readonly string _filePath;
    private readonly LogService _log;
    private readonly object _cacheLock = new();
    private TokenData? _cachedData;
    private bool _cacheLoaded;

    public LinuxTokenStorageService(LogService log)
    {
        _log = log;
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        _folderPath = Path.Combine(dataHome, "recontrol");
        _filePath = Path.Combine(_folderPath, "tokens.dat");
    }

    public void Save(TokenData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Directory.CreateDirectory(_folderPath);

        var json = JsonSerializer.Serialize(data);
        var plainBytes = Encoding.UTF8.GetBytes(json);

        using var aes = Aes.Create();
        aes.KeySize = KeySize * 8;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = DeriveKey();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to encrypted data
        var output = new byte[IvSize + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, output, 0, IvSize);
        Buffer.BlockCopy(encryptedBytes, 0, output, IvSize, encryptedBytes.Length);

        File.WriteAllBytes(_filePath, output);

        // Set file permissions to owner-only (600)
        try
        {
            File.SetUnixFileMode(_filePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Ignore if not supported
        }

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

            var fileBytes = File.ReadAllBytes(_filePath);
            if (fileBytes.Length < IvSize)
            {
                _log.Warning("LinuxTokenStorageService.Load: file too small");
                lock (_cacheLock)
                {
                    _cachedData = null;
                    _cacheLoaded = true;
                }
                return null;
            }

            var iv = new byte[IvSize];
            Buffer.BlockCopy(fileBytes, 0, iv, 0, IvSize);

            var encryptedBytes = new byte[fileBytes.Length - IvSize];
            Buffer.BlockCopy(fileBytes, IvSize, encryptedBytes, 0, encryptedBytes.Length);

            using var aes = Aes.Create();
            aes.KeySize = KeySize * 8;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = DeriveKey();
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
            var json = Encoding.UTF8.GetString(plainBytes);
            var data = JsonSerializer.Deserialize<TokenData>(json);

            lock (_cacheLock)
            {
                _cachedData = data;
                _cacheLoaded = true;
            }
            return data;
        }
        catch (CryptographicException ex)
        {
            _log.Warning($"LinuxTokenStorageService.Load: decryption failed (machine-id may have changed): {ex.Message}");
            lock (_cacheLock)
            {
                _cachedData = null;
                _cacheLoaded = true;
            }
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning($"LinuxTokenStorageService.Load failed: {ex.Message}");
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
            _log.Warning($"LinuxTokenStorageService.Clear failed: {ex.Message}");
        }
    }

    public string? GetAccessToken() => Load()?.AccessToken;
    public string? GetRefreshToken() => Load()?.RefreshToken;
    public string? GetDeviceId() => Load()?.DeviceId;
    public string? GetUserId() => Load()?.UserId;

    /// <summary>
    /// Derives a 256-bit key from /etc/machine-id + current username using PBKDF2.
    /// This ties token encryption to the specific machine and user account.
    /// </summary>
    private static byte[] DeriveKey()
    {
        var machineId = string.Empty;
        try
        {
            machineId = File.ReadAllText("/etc/machine-id").Trim();
        }
        catch
        {
            // Fall back to hostname if machine-id not available
            machineId = Environment.MachineName;
        }

        var username = Environment.UserName;
        var salt = Encoding.UTF8.GetBytes($"recontrol:{machineId}:{username}");

        using var pbkdf2 = new Rfc2898DeriveBytes(
            machineId + username,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);

        return pbkdf2.GetBytes(KeySize);
    }
}
