namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Thrown when the OS denies a READ on a path the user is otherwise allowed to address.
/// Mapped to FilesErrorCode.PERMISSION_READ in FilesCtlChannel so the UI can render
/// a "can't read source" dialog distinct from generic PERMISSION_DENIED. Raised at
/// list/download/copy-source call sites; never raised on writes.
/// </summary>
public sealed class PermissionReadException : System.Exception
{
    public string Path { get; }

    public PermissionReadException(string path, System.Exception? inner = null)
        : base($"Permission denied while reading: {path}", inner)
    {
        Path = path;
    }
}
