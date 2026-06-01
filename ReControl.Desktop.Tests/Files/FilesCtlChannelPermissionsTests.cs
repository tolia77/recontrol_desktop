using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Files.FilesProtocol;

namespace ReControl.Desktop.Tests.Files;

public class FilesCtlChannelPermissionsTests
{
    private static IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>> Handlers(string cmd) =>
        new Dictionary<string, Func<JsonElement, Task<object?>>>
        {
            [cmd] = _ => Task.FromResult<object?>(new { ok = true })
        };

    [Fact]
    public async Task Read_Command_WhenFilesReadFalse_SendsPermissionDeniedError()
    {
        var sent = new List<string>();
        await FilesCtlChannel.HandleAsync(
            raw: """{ "id": "r-1", "command": "files.list", "payload": {} }""",
            handlers: Handlers("files.list"),
            filesRead: () => false,
            filesWrite: () => true,
            send: s => sent.Add(s),
            log: new LogService());

        sent.Should().HaveCount(1);
        var doc = JsonDocument.Parse(sent[0]).RootElement;
        doc.GetProperty("status").GetString().Should().Be("error");
        doc.GetProperty("error").GetProperty("code").GetString().Should().Be("PERMISSION_DENIED");
        doc.GetProperty("error").GetProperty("data").GetProperty("permission").GetString().Should().Be("files_read");
    }

    [Fact]
    public async Task Write_Command_WhenFilesWriteFalse_SendsPermissionDeniedError()
    {
        var sent = new List<string>();
        await FilesCtlChannel.HandleAsync(
            raw: """{ "id": "w-1", "command": "files.mkdir", "payload": { "path": "x" } }""",
            handlers: Handlers("files.mkdir"),
            filesRead: () => true,
            filesWrite: () => false,
            send: s => sent.Add(s),
            log: new LogService());

        var doc = JsonDocument.Parse(sent[0]).RootElement;
        doc.GetProperty("error").GetProperty("code").GetString().Should().Be("PERMISSION_DENIED");
        doc.GetProperty("error").GetProperty("data").GetProperty("permission").GetString().Should().Be("files_write");
    }

    [Fact]
    public async Task Read_Command_WhenFilesReadTrue_DispatchesNormally()
    {
        var sent = new List<string>();
        await FilesCtlChannel.HandleAsync(
            raw: """{ "id": "r-2", "command": "files.list", "payload": {} }""",
            handlers: Handlers("files.list"),
            filesRead: () => true,
            filesWrite: () => false,
            send: s => sent.Add(s),
            log: new LogService());

        var doc = JsonDocument.Parse(sent[0]).RootElement;
        doc.GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task Unknown_Command_FallsThrough_ToUnknownCommandError()
    {
        var sent = new List<string>();
        await FilesCtlChannel.HandleAsync(
            raw: """{ "id": "u-1", "command": "files.bogus", "payload": {} }""",
            handlers: Handlers("files.list"),
            filesRead: () => false,
            filesWrite: () => false,
            send: s => sent.Add(s),
            log: new LogService());

        var doc = JsonDocument.Parse(sent[0]).RootElement;
        doc.GetProperty("error").GetProperty("code").GetString().Should().Be("UNKNOWN_COMMAND");
    }
}
