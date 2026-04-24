using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using ReControl.Desktop.Services.Files;

namespace ReControl.Desktop.Tests.Files;

public class AllowlistStoreTests
{
    private static string NewTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "recontrol-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "allowlist.json");
    }

    private static void Cleanup(string jsonPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(jsonPath);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsRoots()
    {
        var path = NewTempFile();
        try
        {
            var store = new AllowlistStore(path);
            var roots = new List<string> { "/tmp/alpha", "/tmp/beta" };

            store.Save(roots);
            var loaded = store.Load();

            loaded.Should().BeEquivalentTo(roots);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyList()
    {
        var path = NewTempFile();
        try
        {
            // Never saved -- file does not exist.
            var store = new AllowlistStore(path);

            var loaded = store.Load();

            loaded.Should().BeEmpty();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Load_MalformedJson_ReturnsEmptyList_NoThrow()
    {
        var path = NewTempFile();
        try
        {
            File.WriteAllText(path, "{ this is not valid json ", Encoding.UTF8);
            var store = new AllowlistStore(path);

            var loaded = store.Load();

            loaded.Should().BeEmpty();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Save_CreatesMissingParentDirectory()
    {
        var parent = Path.Combine(Path.GetTempPath(), "recontrol-tests-" + Path.GetRandomFileName());
        var nestedDir = Path.Combine(parent, "nested", "deeper");
        var path = Path.Combine(nestedDir, "allowlist.json");
        try
        {
            Directory.Exists(nestedDir).Should().BeFalse("precondition");
            var store = new AllowlistStore(path);

            store.Save(new List<string> { "/tmp/foo" });

            File.Exists(path).Should().BeTrue();
            store.Load().Should().ContainSingle().Which.Should().Be("/tmp/foo");
        }
        finally
        {
            try { Directory.Delete(parent, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Save_IsAtomic_FinalFileHasNewContent()
    {
        var path = NewTempFile();
        try
        {
            var store = new AllowlistStore(path);
            store.Save(new List<string> { "/tmp/old" });

            store.Save(new List<string> { "/tmp/new1", "/tmp/new2" });

            var loaded = store.Load();
            loaded.Should().BeEquivalentTo(new[] { "/tmp/new1", "/tmp/new2" });
            // Temp file must not linger.
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Load_EmptyRootsArray_ReturnsEmptyList()
    {
        var path = NewTempFile();
        try
        {
            File.WriteAllText(path, "{\"roots\": []}", Encoding.UTF8);
            var store = new AllowlistStore(path);

            store.Load().Should().BeEmpty();
        }
        finally { Cleanup(path); }
    }
}
