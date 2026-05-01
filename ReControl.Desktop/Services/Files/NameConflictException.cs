namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Thrown by upload/move/copy when the destination already exists and the caller
/// requested NameConflictMode.Fail (or omitted the field, which defaults to Fail).
/// Mapped to FilesErrorCode.NAME_CONFLICT in FilesCtlChannel; the frontend uses
/// the surfaced { existingPath } payload to drive the conflict-resolution dialog
/// without re-listing the parent directory.
/// </summary>
public sealed class NameConflictException : System.Exception
{
    public string ExistingPath { get; }

    public NameConflictException(string existingPath)
        : base($"Destination already exists: {existingPath}")
    {
        ExistingPath = existingPath;
    }
}
