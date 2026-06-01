using FluentAssertions;
using ReControl.Desktop.Services.Permissions;

namespace ReControl.Desktop.Tests.Permissions;

public class PeerPermissionsHolderTests
{
    [Fact]
    public void Default_BeforeAnyOffer_IsAllTrue()
    {
        var holder = new PeerPermissionsHolder();
        // Pre-migration backends do not send a snapshot; assume owner-equivalent
        // until told otherwise so unmigrated hosts keep working.
        holder.Current.AccessClipboard.Should().BeTrue();
        holder.Current.FilesRead.Should().BeTrue();
        holder.Current.FilesWrite.Should().BeTrue();
    }

    [Fact]
    public void Set_ReplacesSnapshotAtomically()
    {
        var holder = new PeerPermissionsHolder();
        var locked = new PeerPermissions(
            SeeScreen: true, AccessMouse: false, AccessKeyboard: false,
            AccessTerminal: false, ManagePower: false,
            AccessClipboard: false, FilesRead: false, FilesWrite: false);
        holder.Set(locked);
        holder.Current.AccessClipboard.Should().BeFalse();
        holder.Current.FilesRead.Should().BeFalse();
        holder.Current.FilesWrite.Should().BeFalse();
        holder.Current.SeeScreen.Should().BeTrue();
    }
}
