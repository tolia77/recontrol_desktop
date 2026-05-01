namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Thrown by mkdir/rename/move/copy/upload.begin when the resolved destination
/// PARENT directory no longer exists at the moment of the operation (raced with
/// a concurrent delete or unmount). Mapped to FilesErrorCode.DESTINATION_GONE in
/// FilesCtlChannel so the UI can render a "destination location no longer
/// available" message distinct from generic NOT_FOUND.
/// </summary>
public sealed class DestinationGoneException : System.Exception
{
    public string Path { get; }

    public DestinationGoneException(string path, System.Exception? inner = null)
        : base($"Destination no longer exists: {path}", inner)
    {
        Path = path;
    }
}
