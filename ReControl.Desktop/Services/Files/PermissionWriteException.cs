namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Thrown when the OS denies a WRITE (create/rename/delete/move/copy-dest/upload-write)
/// on a path the user is otherwise allowed to address. Mapped to
/// FilesErrorCode.PERMISSION_WRITE in FilesCtlChannel so the UI can render a
/// "can't write to destination" dialog distinct from generic PERMISSION_DENIED.
/// Raised at mkdir/rename/delete/move/copy/upload-write call sites; never raised on reads.
/// </summary>
public sealed class PermissionWriteException : System.Exception
{
    public string Path { get; }

    public PermissionWriteException(string path, System.Exception? inner = null)
        : base($"Permission denied while writing: {path}", inner)
    {
        Path = path;
    }
}
