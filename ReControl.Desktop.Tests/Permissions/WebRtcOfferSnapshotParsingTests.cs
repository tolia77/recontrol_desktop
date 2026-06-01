using System.Text.Json;
using FluentAssertions;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Permissions;

namespace ReControl.Desktop.Tests.Permissions;

public class WebRtcOfferSnapshotParsingTests
{
    [Fact]
    public void Parse_NullElement_ReturnsOwnerEquivalent()
    {
        var perms = WebRtcService.ParsePermissionsSnapshot(default);
        perms.Should().BeSameAs(PeerPermissions.OwnerEquivalent);
    }

    [Fact]
    public void Parse_PartialSnapshot_DefaultsMissingKeysToFalse()
    {
        var json = JsonDocument.Parse("""{ "see_screen": true, "access_clipboard": true }""");
        var perms = WebRtcService.ParsePermissionsSnapshot(json.RootElement);
        perms.SeeScreen.Should().BeTrue();
        perms.AccessClipboard.Should().BeTrue();
        perms.FilesRead.Should().BeFalse();
        perms.FilesWrite.Should().BeFalse();
        perms.AccessTerminal.Should().BeFalse();
    }

    [Fact]
    public void Parse_FullSnapshot_MapsAllKeys()
    {
        var json = JsonDocument.Parse("""
        {
            "see_screen": true, "access_mouse": false, "access_keyboard": true,
            "access_terminal": false, "manage_power": true,
            "access_clipboard": false, "files_read": true, "files_write": false
        }
        """);
        var perms = WebRtcService.ParsePermissionsSnapshot(json.RootElement);
        perms.SeeScreen.Should().BeTrue();
        perms.AccessMouse.Should().BeFalse();
        perms.AccessKeyboard.Should().BeTrue();
        perms.AccessTerminal.Should().BeFalse();
        perms.ManagePower.Should().BeTrue();
        perms.AccessClipboard.Should().BeFalse();
        perms.FilesRead.Should().BeTrue();
        perms.FilesWrite.Should().BeFalse();
    }
}
