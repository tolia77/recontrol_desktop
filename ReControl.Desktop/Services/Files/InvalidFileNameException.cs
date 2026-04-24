namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Thrown when a file or folder name fails validation. Reason is one of:
/// RESERVED, ILLEGAL_CHAR, TOO_LONG, EMPTY, DOT_ONLY, TRAILING_SPACE_OR_DOT.
/// </summary>
public sealed class InvalidFileNameException : System.Exception
{
    public string Reason { get; }
    public string Name { get; }

    public InvalidFileNameException(string name, string reason)
        : base($"Invalid file name ({reason}): {name}")
    {
        Name = name;
        Reason = reason;
    }
}
