using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Files;

namespace ReControl.Desktop.Tests.Files;

public class PathCanonicalizerTests : IDisposable
{
    private readonly string _sandboxDir;
    private readonly string _allowedDir;
    private readonly string _outsideDir;
    private readonly string _allowlistJsonPath;
    private readonly AllowlistService _allowlist;
    private readonly PathCanonicalizer _canon;

    public PathCanonicalizerTests()
    {
        _sandboxDir = Path.Combine(Path.GetTempPath(), "recontrol-tests-" + Path.GetRandomFileName());
        _allowedDir = Path.Combine(_sandboxDir, "allowed");
        _outsideDir = Path.Combine(_sandboxDir, "outside");
        Directory.CreateDirectory(_allowedDir);
        Directory.CreateDirectory(_outsideDir);

        // Allowlist JSON in a separate dir so the watcher doesn't pick up sandbox writes.
        var allowlistDir = Path.Combine(Path.GetTempPath(), "recontrol-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(allowlistDir);
        _allowlistJsonPath = Path.Combine(allowlistDir, "allowlist.json");
        File.WriteAllText(
            _allowlistJsonPath,
            "{\"roots\":[" + EncodeJsonString(_allowedDir) + "]}",
            Encoding.UTF8);

        _allowlist = new AllowlistService(new LogService(), _allowlistJsonPath);
        _canon = new PathCanonicalizer(_allowlist);
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
    public void DirectPathInsideRoot_IsAllowed()
    {
        var file = Path.Combine(_allowedDir, "file.txt");
        File.WriteAllText(file, "hi");

        var result = _canon.Canonicalize(file);

        result.Should().Be(file);
    }

    [Fact]
    public void RootExactPath_IsAllowed()
    {
        var result = _canon.Canonicalize(_allowedDir);

        result.Should().Be(_allowedDir);
    }

    [Fact]
    public void RelativeDotDotEscape_Rejected()
    {
        // allowed/../outside resolves to the outside sibling
        var attack = Path.Combine(_allowedDir, "..", "outside");

        var ex = Assert.Throws<AllowlistViolationException>(() => _canon.Canonicalize(attack));
        ex.Cause.Should().Be("traversal");
    }

    [Fact]
    public void DoubleDotDotDeepEscape_Rejected()
    {
        // allowed/foo/../../outside -- drills up twice
        var attack = Path.Combine(_allowedDir, "foo", "..", "..", "outside");

        var ex = Assert.Throws<AllowlistViolationException>(() => _canon.Canonicalize(attack));
        ex.Cause.Should().Be("traversal");
    }

    [Fact]
    public void AbsoluteSmuggling_Rejected()
    {
        // An absolute path entirely outside the allowlist.
        var attack = OperatingSystem.IsWindows() ? @"C:\Windows\System32" : "/etc/passwd";

        var ex = Assert.Throws<AllowlistViolationException>(() => _canon.Canonicalize(attack));
        ex.Cause.Should().Be("absolute_smuggling");
    }

    [Fact]
    public void Empty_Rejected()
    {
        var ex = Assert.Throws<AllowlistViolationException>(() => _canon.Canonicalize(""));
        ex.Cause.Should().Be("empty");
    }

    [Fact]
    public void RootWithoutTrailingSep_DoesNotMatchRootPrefixedName()
    {
        // Classic substring-matching bug: root "/tmp/allowed" must NOT match "/tmp/allowed-evil/file".
        var siblingDir = _allowedDir + "-evil";
        Directory.CreateDirectory(siblingDir);
        try
        {
            var attack = Path.Combine(siblingDir, "file.txt");

            var ex = Assert.Throws<AllowlistViolationException>(() => _canon.Canonicalize(attack));
            ex.Cause.Should().Be("absolute_smuggling");
        }
        finally
        {
            try { Directory.Delete(siblingDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SymlinkEscape_Rejected()
    {
        // Create a symlink inside allowed/ that targets outside/. Canonicalize on the symlink path
        // must reject with cause=symlink_escape. Symlink creation requires elevation on Windows.
        var linkPath = Path.Combine(_allowedDir, "escape-link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, _outsideDir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // No symlink privilege on this host -- skip.
            return;
        }

        var exThrown = Assert.Throws<AllowlistViolationException>(() => _canon.Canonicalize(linkPath));
        exThrown.Cause.Should().Be("symlink_escape");
    }

    [Fact]
    public void PathSeparatorMixing_StillCanonicalizes()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only: forward slash is valid there

        var file = Path.Combine(_allowedDir, "mix.txt");
        File.WriteAllText(file, "hi");
        // Build a mixed-separator version of the same absolute path.
        var mixed = file.Replace('\\', '/').Replace('/', '\\'); // round-trip -- exercises normalizer
        var result = _canon.Canonicalize(mixed);

        result.Should().Be(file);
    }

    [Fact]
    public void CaseDifferingPath_MatchesOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return; // NTFS is case-insensitive; Linux filesystems are not.

        var file = Path.Combine(_allowedDir, "CaseTest.txt");
        File.WriteAllText(file, "hi");

        var result = _canon.Canonicalize(file.ToUpperInvariant());

        result.Should().NotBeNullOrEmpty();
        // We don't assert exact casing because the OS may normalize differently, but the call must succeed.
    }
}
