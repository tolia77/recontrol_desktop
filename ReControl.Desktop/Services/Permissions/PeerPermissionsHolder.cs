using System.Threading;

namespace ReControl.Desktop.Services.Permissions;

/// <summary>
/// Thread-safe holder for the active peer's permission snapshot. Writes from
/// the signaling thread (HandleOfferAsync / CommandDispatcher) atomically
/// replace the record reference; reads from the WebRTC data-channel threads
/// see either the old or new snapshot but never a torn read.
/// </summary>
public sealed class PeerPermissionsHolder
{
    private PeerPermissions _current = PeerPermissions.OwnerEquivalent;

    public PeerPermissions Current => Volatile.Read(ref _current);

    public void Set(PeerPermissions next) => Volatile.Write(ref _current, next);
}
