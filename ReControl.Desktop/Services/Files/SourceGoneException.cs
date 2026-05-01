namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Thrown by move/copy/rename/delete/download when the resolved source path no
/// longer exists at the moment of the operation (raced with a concurrent delete).
/// Mapped to FilesErrorCode.SOURCE_GONE in FilesCtlChannel so the UI can render a
/// "source gone, refresh" message distinct from generic NOT_FOUND (which is also
/// used by list operations on missing parents).
/// </summary>
public sealed class SourceGoneException : System.Exception
{
    public string Path { get; }

    public SourceGoneException(string path, System.Exception? inner = null)
        : base($"Source no longer exists: {path}", inner)
    {
        Path = path;
    }
}
