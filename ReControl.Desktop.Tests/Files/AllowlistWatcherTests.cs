using System;
using System.IO;
using System.Text;
using System.Threading;
using FluentAssertions;
using ReControl.Desktop.Services.Files;

namespace ReControl.Desktop.Tests.Files;

public class AllowlistWatcherTests
{
    private static string NewTempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "recontrol-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "allowlist.json");
        File.WriteAllText(path, "{\"roots\": []}", Encoding.UTF8);
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
    public void WritingFile_TriggersCallback_WithinTimeout()
    {
        var path = NewTempFile();
        try
        {
            using var ev = new ManualResetEventSlim(initialState: false);
            using var watcher = new AllowlistWatcher(path, () => ev.Set());

            // Give the watcher a moment to wire up before we write.
            Thread.Sleep(100);
            File.WriteAllText(path, "{\"roots\": [\"/tmp/a\"]}", Encoding.UTF8);

            ev.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue("callback should fire after write");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void RapidMultipleWrites_CoalesceToSingleCallback()
    {
        var path = NewTempFile();
        try
        {
            var fireCount = 0;
            using var ev = new ManualResetEventSlim(initialState: false);
            using var watcher = new AllowlistWatcher(path, () =>
            {
                Interlocked.Increment(ref fireCount);
                ev.Set();
            });

            Thread.Sleep(100);

            // 5 rapid writes within ~50 ms should all land inside the 300 ms debounce window.
            for (int i = 0; i < 5; i++)
            {
                File.WriteAllText(path, $"{{\"roots\": [\"/tmp/{i}\"]}}", Encoding.UTF8);
                Thread.Sleep(10);
            }

            // Wait for the debounced callback to fire.
            ev.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            // Let any additional (unexpected) events arrive before asserting the count. The
            // debounce is 300 ms; we wait 500 ms to be safely past it.
            Thread.Sleep(500);

            fireCount.Should().Be(1, "debounce should coalesce rapid writes into one callback");
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
            var watcher = new AllowlistWatcher(path, () => Interlocked.Increment(ref fireCount));
            Thread.Sleep(100);
            watcher.Dispose();

            File.WriteAllText(path, "{\"roots\": [\"/tmp/after-dispose\"]}", Encoding.UTF8);
            // Wait well past the debounce window.
            Thread.Sleep(700);

            fireCount.Should().Be(0, "no callback should fire after Dispose");
        }
        finally { Cleanup(path); }
    }
}
