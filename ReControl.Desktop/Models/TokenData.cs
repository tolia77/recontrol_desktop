namespace ReControl.Desktop.Models;

/// <summary>
/// Holds token information for authentication persistence.
/// Ported from WPF TokenStore.TokenData.
/// </summary>
public sealed record TokenData(
    string UserId,
    string DeviceId,
    string AccessToken,
    string RefreshToken);
