using System.IO;
using System.Text;
using FluentAssertions;
using ReControl.Desktop.Services.Clipboard;

namespace ReControl.Desktop.Tests.Clipboard;

public class ClipboardSettingsStoreTests
{
    private static string NewTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "recontrol-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "clipboard.json");
    }

    private static void Cleanup(string jsonPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(jsonPath);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsFields()
    {
        var path = NewTempFile();
        try
        {
            var store = new ClipboardSettingsStore(path);
            var settings = new ClipboardSettings
            {
                Version = 1,
                Master = true,
                AllowOutbound = false,
                AllowInbound = true
            };

            store.Save(settings);
            var loaded = store.Load();

            loaded.Version.Should().Be(1);
            loaded.Master.Should().BeTrue();
            loaded.AllowOutbound.Should().BeFalse();
            loaded.AllowInbound.Should().BeTrue();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var path = NewTempFile();
        try
        {
            var store = new ClipboardSettingsStore(path);
            var loaded = store.Load();

            loaded.Version.Should().Be(1);
            loaded.Master.Should().BeTrue();
            loaded.AllowOutbound.Should().BeTrue();
            loaded.AllowInbound.Should().BeTrue();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Load_MalformedJson_ReturnsLastGood()
    {
        var path = NewTempFile();
        try
        {
            var store = new ClipboardSettingsStore(path);
            var initial = new ClipboardSettings
            {
                Version = 1,
                Master = true,
                AllowOutbound = false,
                AllowInbound = false
            };
            store.Save(initial);
            store.Load();

            File.WriteAllText(path, "{ not-valid ", Encoding.UTF8);
            var loaded = store.Load();

            loaded.Version.Should().Be(1);
            loaded.Master.Should().BeTrue();
            loaded.AllowOutbound.Should().BeFalse();
            loaded.AllowInbound.Should().BeFalse();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Save_IsAtomic_FinalFileHasNewContent()
    {
        var path = NewTempFile();
        try
        {
            var store = new ClipboardSettingsStore(path);
            store.Save(new ClipboardSettings
            {
                Version = 1,
                Master = true,
                AllowOutbound = true,
                AllowInbound = true
            });

            store.Save(new ClipboardSettings
            {
                Version = 2,
                Master = false,
                AllowOutbound = true,
                AllowInbound = false
            });

            var loaded = store.Load();
            loaded.Version.Should().Be(2);
            loaded.Master.Should().BeFalse();
            loaded.AllowOutbound.Should().BeTrue();
            loaded.AllowInbound.Should().BeFalse();
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally { Cleanup(path); }
    }
}
