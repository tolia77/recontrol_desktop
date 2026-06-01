using System.Collections.Generic;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

public enum FilesPermission
{
    Read,
    Write
}

/// <summary>
/// Read-vs-write classification for files.* commands. Unknown commands return
/// null; FilesCtlChannel treats null as "let the handler dictionary deal with
/// it" (which already returns UNKNOWN_COMMAND).
/// </summary>
public static class FilesPermissionMap
{
    private static readonly Dictionary<string, FilesPermission> Map = new()
    {
        // Read-side
        ["files.listRoots"]         = FilesPermission.Read,
        ["files.list"]              = FilesPermission.Read,
        ["files.download.begin"]    = FilesPermission.Read,
        ["files.transfer.cancel"]   = FilesPermission.Read,
        // Write-side
        ["files.mkdir"]             = FilesPermission.Write,
        ["files.rename"]            = FilesPermission.Write,
        ["files.delete"]            = FilesPermission.Write,
        ["files.move"]              = FilesPermission.Write,
        ["files.copy"]              = FilesPermission.Write,
        ["files.upload.begin"]      = FilesPermission.Write,
        ["files.upload.complete"]   = FilesPermission.Write,
    };

    public static FilesPermission? For(string command)
        => Map.TryGetValue(command, out var p) ? p : null;
}
