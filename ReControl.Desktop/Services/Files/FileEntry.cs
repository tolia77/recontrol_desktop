namespace ReControl.Desktop.Services.Files;

public sealed record FileEntry(
    string Name,
    string Path,
    bool IsDirectory,
    long SizeBytes,
    System.DateTime ModifiedUtc);
