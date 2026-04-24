using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Files;

namespace ReControl.Desktop.Tests.Files;

public class FileOperationsServiceTests : IDisposable
{
    private readonly string _sandboxDir;
    private readonly string _allowedDir;
    private readonly string _outsideDir;
    private readonly string _safeTxt;
    private readonly string _allowlistJsonPath;
    private readonly AllowlistService _allowlist;
    private readonly PathCanonicalizer _canon;
    private readonly FileOperationsService _ops;

    public FileOperationsServiceTests()
    {
        _sandboxDir = Path.Combine(Path.GetTempPath(), "recontrol-tests-" + Path.GetRandomFileName());
        _allowedDir = Path.Combine(_sandboxDir, "allowed");
        _outsideDir = Path.Combine(_sandboxDir, "outside");
        Directory.CreateDirectory(_allowedDir);
        Directory.CreateDirectory(_outsideDir);
        _safeTxt = Path.Combine(_allowedDir, "safe.txt");
        File.WriteAllText(_safeTxt, "hello", Encoding.UTF8);

        var allowlistDir = Path.Combine(Path.GetTempPath(), "recontrol-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(allowlistDir);
        _allowlistJsonPath = Path.Combine(allowlistDir, "allowlist.json");
        File.WriteAllText(
            _allowlistJsonPath,
            "{\"roots\":[" + EncodeJsonString(_allowedDir) + "]}",
            Encoding.UTF8);

        var log = new LogService();
        _allowlist = new AllowlistService(log, _allowlistJsonPath);
        _canon = new PathCanonicalizer(_allowlist);
        _ops = new FileOperationsService(_canon, _allowlist, log);
    }

    private static string EncodeJsonString(string raw) =>
        "\"" + raw.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public void Dispose()
    {
        _allowlist.Dispose();
        try { Directory.Delete(_sandboxDir, recursive: true); } catch { }
        try { Directory.Delete(Path.GetDirectoryName(_allowlistJsonPath)!, recursive: true); } catch { }
    }

    [Fact]
    public async Task ListAsync_InsideRoot_ReturnsEntries()
    {
        var entries = await _ops.ListAsync(_allowedDir);

        entries.Should().ContainSingle(e => e.Name == "safe.txt");
        entries.First(e => e.Name == "safe.txt").IsDirectory.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_DotDotEscape_Throws()
    {
        var attack = Path.Combine(_allowedDir, "..", "outside");

        await Assert.ThrowsAsync<AllowlistViolationException>(() => _ops.ListAsync(attack));
    }

    [Fact]
    public async Task ListAsync_AbsoluteOutsideAllowlist_Throws()
    {
        await Assert.ThrowsAsync<AllowlistViolationException>(() => _ops.ListAsync(_outsideDir));
    }

    [Fact]
    public async Task ListAsync_SymlinkToOutside_Throws()
    {
        var link = Path.Combine(_allowedDir, "escape-link");
        try { Directory.CreateSymbolicLink(link, _outsideDir); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return; // no privilege to create symlink -- skip
        }

        await Assert.ThrowsAsync<AllowlistViolationException>(() => _ops.ListAsync(link));
    }

    [Fact]
    public async Task ListAsync_SymlinkedSubdirectory_NotFollowedInEnumeration()
    {
        // Create a symlink INSIDE the allowed dir that points outside. Enumeration of the
        // allowed dir must silently skip the reparse point (AttributesToSkip guards this).
        var link = Path.Combine(_allowedDir, "bad-link");
        try { Directory.CreateSymbolicLink(link, _outsideDir); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return; // skip on hosts without symlink privilege
        }

        var entries = await _ops.ListAsync(_allowedDir);

        // safe.txt is present; the symlink is skipped.
        entries.Should().Contain(e => e.Name == "safe.txt");
        entries.Should().NotContain(e => e.Name == "bad-link");
    }

    [Fact]
    public async Task MkdirAsync_InvalidName_Throws_Reserved()
    {
        var ex = await Assert.ThrowsAsync<InvalidFileNameException>(
            () => _ops.MkdirAsync(_allowedDir, "CON"));
        ex.Reason.Should().Be("RESERVED");
    }

    [Fact]
    public async Task MkdirAsync_IllegalChar_Throws()
    {
        var ex = await Assert.ThrowsAsync<InvalidFileNameException>(
            () => _ops.MkdirAsync(_allowedDir, "has\0null"));
        ex.Reason.Should().Be("ILLEGAL_CHAR");
    }

    [Fact]
    public async Task MkdirAsync_InsideRoot_Creates()
    {
        await _ops.MkdirAsync(_allowedDir, "newfolder");

        Directory.Exists(Path.Combine(_allowedDir, "newfolder")).Should().BeTrue();
    }

    [Fact]
    public async Task RenameAsync_InvalidName_Throws_Reserved()
    {
        var ex = await Assert.ThrowsAsync<InvalidFileNameException>(
            () => _ops.RenameAsync(_safeTxt, "NUL.txt"));
        ex.Reason.Should().Be("RESERVED");
    }

    [Fact]
    public async Task RenameAsync_ValidName_Renames()
    {
        await _ops.RenameAsync(_safeTxt, "renamed.txt");

        File.Exists(_safeTxt).Should().BeFalse();
        File.Exists(Path.Combine(_allowedDir, "renamed.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_OutsideRoot_Throws()
    {
        var target = Path.Combine(_outsideDir, "anything");

        await Assert.ThrowsAsync<AllowlistViolationException>(() => _ops.DeleteAsync(target));
    }

    [Fact]
    public async Task DeleteAsync_InsideRoot_Deletes()
    {
        await _ops.DeleteAsync(_safeTxt);

        File.Exists(_safeTxt).Should().BeFalse();
    }

    [Fact]
    public async Task MoveAsync_DstOutsideRoot_Throws()
    {
        var dst = Path.Combine(_outsideDir, "stolen.txt");

        await Assert.ThrowsAsync<AllowlistViolationException>(() => _ops.MoveAsync(_safeTxt, dst));
    }

    [Fact]
    public async Task MoveAsync_SrcOutsideRoot_Throws()
    {
        var src = Path.Combine(_outsideDir, "secret.txt");
        File.WriteAllText(src, "nope");
        var dst = Path.Combine(_allowedDir, "stolen.txt");

        await Assert.ThrowsAsync<AllowlistViolationException>(() => _ops.MoveAsync(src, dst));
    }

    [Fact]
    public async Task CopyAsync_FullyInsideRoot_Succeeds()
    {
        var dst = Path.Combine(_allowedDir, "safe2.txt");

        await _ops.CopyAsync(_safeTxt, dst);

        File.Exists(_safeTxt).Should().BeTrue();
        File.Exists(dst).Should().BeTrue();
        File.ReadAllText(dst).Should().Be("hello");
    }

    [Fact]
    public async Task CopyAsync_SrcOutsideRoot_Throws()
    {
        var src = Path.Combine(_outsideDir, "secret.txt");
        File.WriteAllText(src, "nope");
        var dst = Path.Combine(_allowedDir, "stolen.txt");

        await Assert.ThrowsAsync<AllowlistViolationException>(() => _ops.CopyAsync(src, dst));
    }

    [Fact]
    public async Task CopyAsync_DstOutsideRoot_Throws()
    {
        var dst = Path.Combine(_outsideDir, "stolen.txt");

        await Assert.ThrowsAsync<AllowlistViolationException>(() => _ops.CopyAsync(_safeTxt, dst));
    }

    [Fact]
    public async Task ListRootsAsync_SkipsNonExistentRoot()
    {
        // Replace allowlist file contents with one existing and one non-existent root,
        // and re-initialize AllowlistService so the new roots are loaded fresh.
        var fakeRoot = Path.Combine(_sandboxDir, "never-created");
        File.WriteAllText(
            _allowlistJsonPath,
            "{\"roots\":[" + EncodeJsonString(_allowedDir) + "," + EncodeJsonString(fakeRoot) + "]}",
            Encoding.UTF8);

        var log = new LogService();
        using var allowlist = new AllowlistService(log, _allowlistJsonPath);
        var canon = new PathCanonicalizer(allowlist);
        var ops = new FileOperationsService(canon, allowlist, log);

        var roots = await ops.ListRootsAsync();

        roots.Should().ContainSingle();
        roots[0].Path.Should().Be(_allowedDir);
        roots[0].IsDirectory.Should().BeTrue();
    }

    [Fact]
    public async Task CaseCollisionOnWindows_IsResolved()
    {
        if (!OperatingSystem.IsWindows()) return;

        var upper = _allowedDir.ToUpperInvariant();

        var entries = await _ops.ListAsync(upper);

        entries.Should().Contain(e => e.Name == "safe.txt");
    }

    [Fact]
    public async Task ListAsync_DotPrefixedFiles_AreMarkedHidden()
    {
        var hidden = Path.Combine(_allowedDir, ".hidden.txt");
        var visible = Path.Combine(_allowedDir, "visible.txt");
        File.WriteAllText(hidden, "h", Encoding.UTF8);
        File.WriteAllText(visible, "v", Encoding.UTF8);

        var entries = await _ops.ListAsync(_allowedDir);

        entries.First(e => e.Name == ".hidden.txt").IsHidden.Should().BeTrue();
        entries.First(e => e.Name == "visible.txt").IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_WindowsHiddenAttribute_MarksEntryHidden()
    {
        if (!OperatingSystem.IsWindows()) return; // FileAttributes.Hidden is Windows-only semantics

        var target = Path.Combine(_allowedDir, "thumbs.db");
        File.WriteAllText(target, "t", Encoding.UTF8);
        File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.Hidden);

        var entries = await _ops.ListAsync(_allowedDir);

        entries.First(e => e.Name == "thumbs.db").IsHidden.Should().BeTrue();
    }

    [Fact]
    public async Task ListRootsAsync_RootsAreNeverReportedHidden()
    {
        var roots = await _ops.ListRootsAsync();

        roots.Should().NotBeEmpty();
        roots.Should().OnlyContain(e => e.IsHidden == false);
    }
}
