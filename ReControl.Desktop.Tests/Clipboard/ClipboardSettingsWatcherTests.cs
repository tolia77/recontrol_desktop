using System.IO;
using System.Text;
using System.Threading;
using FluentAssertions;
using ReControl.Desktop.Services.Clipboard;

namespace ReControl.Desktop.Tests.Clipboard;

public class ClipboardSettingsWatcherTests
{
    private static string NewTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "recontrol-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clipboard.json");
        File.WriteAllText(path, "{\"version\":1,\"master\":true,\"allowOutbound\":true,\"allowInbound\":true}", Encoding.UTF8);
        return path;
    }

    private static void Cleanup(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void WatcherFires_WithinOneSecond()
    {
        var path = NewTempFile();
        try
        {
            using var ev = new ManualResetEventSlim(false);
            using var watcher = new ClipboardSettingsWatcher(path, () => ev.Set());

            Thread.Sleep(100);
            File.WriteAllText(path, "{\"version\":1,\"master\":false,\"allowOutbound\":true,\"allowInbound\":true}", Encoding.UTF8);

            ev.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void RapidWrites_CoalesceWithDebounce()
    {
        var path = NewTempFile();
        try
        {
            var fireCount = 0;
            using var ev = new ManualResetEventSlim(false);
            using var watcher = new ClipboardSettingsWatcher(path, () =>
            {
                Interlocked.Increment(ref fireCount);
                ev.Set();
            });

            Thread.Sleep(100);
            for (int i = 0; i < 5; i++)
            {
                File.WriteAllText(path, $"{{\"version\":1,\"master\":true,\"allowOutbound\":{(i % 2 == 0 ? "true" : "false")},\"allowInbound\":true}}", Encoding.UTF8);
                Thread.Sleep(10);
            }

            ev.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            Thread.Sleep(500);
            fireCount.Should().Be(1);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Dispose_StopsCallbacks()
    {
        var path = NewTempFile();
        try
        {
            var fireCount = 0;
            var watcher = new ClipboardSettingsWatcher(path, () => Interlocked.Increment(ref fireCount));
            Thread.Sleep(100);
            watcher.Dispose();

            File.WriteAllText(path, "{\"version\":1,\"master\":false,\"allowOutbound\":false,\"allowInbound\":false}", Encoding.UTF8);
            Thread.Sleep(700);
            fireCount.Should().Be(0);
        }
        finally { Cleanup(path); }
    }
}
