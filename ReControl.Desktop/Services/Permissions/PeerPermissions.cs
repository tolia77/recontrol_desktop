namespace ReControl.Desktop.Services.Permissions;

/// <summary>
/// Immutable snapshot of the active peer's permissions. Produced from the
/// `permissions` field of a `webrtc.offer` envelope and updated by the
/// `permissions.update` command. Read by ClipboardCtlChannel and FilesCtlChannel
/// on every inbound message to decide whether to dispatch or refuse.
/// </summary>
public sealed record PeerPermissions(
    bool SeeScreen,
    bool AccessMouse,
    bool AccessKeyboard,
    bool AccessTerminal,
    bool ManagePower,
    bool AccessClipboard,
    bool FilesRead,
    bool FilesWrite)
{
    /// <summary>
    /// Backward-compat default. Pre-migration backends do not send a
    /// snapshot; treat the peer as owner-equivalent so unmigrated hosts
    /// retain their existing behavior (clipboard + files work). Migrated
    /// backends always overwrite this default on the first webrtc.offer.
    /// </summary>
    public static PeerPermissions OwnerEquivalent { get; } = new(
        SeeScreen: true, AccessMouse: true, AccessKeyboard: true,
        AccessTerminal: true, ManagePower: true,
        AccessClipboard: true, FilesRead: true, FilesWrite: true);
}
