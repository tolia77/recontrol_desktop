namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Thrown when a caller-supplied path resolves outside every allowlisted root.
/// Cause is one of: "traversal", "absolute_smuggling", "symlink_escape",
/// "outside_roots", "empty", "malformed".
/// </summary>
public sealed class AllowlistViolationException : System.Exception
{
    public string AttemptedPath { get; }
    public string Cause { get; }

    public AllowlistViolationException(string attemptedPath, string cause)
        : base($"Path outside allowlist ({cause}): {attemptedPath}")
    {
        AttemptedPath = attemptedPath;
        Cause = cause;
    }
}
