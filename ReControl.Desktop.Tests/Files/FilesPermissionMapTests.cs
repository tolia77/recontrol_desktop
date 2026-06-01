using FluentAssertions;
using ReControl.Desktop.Services.Files.FilesProtocol;

namespace ReControl.Desktop.Tests.Files;

public class FilesPermissionMapTests
{
    [Theory]
    [InlineData("files.listRoots",         FilesPermission.Read)]
    [InlineData("files.list",              FilesPermission.Read)]
    [InlineData("files.download.begin",    FilesPermission.Read)]
    [InlineData("files.transfer.cancel",   FilesPermission.Read)]
    [InlineData("files.mkdir",             FilesPermission.Write)]
    [InlineData("files.rename",            FilesPermission.Write)]
    [InlineData("files.delete",            FilesPermission.Write)]
    [InlineData("files.move",              FilesPermission.Write)]
    [InlineData("files.copy",              FilesPermission.Write)]
    [InlineData("files.upload.begin",      FilesPermission.Write)]
    [InlineData("files.upload.complete",   FilesPermission.Write)]
    public void Classifies(string command, FilesPermission expected)
    {
        FilesPermissionMap.For(command).Should().Be(expected);
    }

    [Fact]
    public void UnknownCommand_ReturnsNull()
    {
        FilesPermissionMap.For("files.unknown").Should().BeNull();
    }
}
