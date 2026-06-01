namespace ReControl.Desktop.Models;

/// <summary>Outcome of an auth REST call, carrying a backend error message on failure.</summary>
public sealed record AuthResult(bool Success, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static AuthResult Ok() => new(true);
    public static AuthResult Fail(string? code, string? message) => new(false, code, message);
}
