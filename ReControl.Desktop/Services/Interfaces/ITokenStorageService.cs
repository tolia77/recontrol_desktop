using ReControl.Desktop.Models;

namespace ReControl.Desktop.Services.Interfaces;

/// <summary>
/// Platform-specific token persistence contract.
/// Windows: DPAPI encryption. Linux: file-based with permissions.
/// </summary>
public interface ITokenStorageService
{
    void Save(TokenData data);
    TokenData? Load();
    void Clear();
    string? GetAccessToken();
    string? GetRefreshToken();
    string? GetDeviceId();
    string? GetUserId();
}
