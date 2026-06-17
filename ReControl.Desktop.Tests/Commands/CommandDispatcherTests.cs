// IMPORTANT: FFmpeg-avoidance constraint.
// This file constructs CommandDispatcher with screenCapture: null. FFmpeg native libs
// (avcodec-61.dll, etc.) are loaded ONLY when FFmpegVideoEncoder / FFmpegVideoSource are
// instantiated, which happens inside WebRtcService offer-handling methods (~line 358/516).
// The ctor itself does NOT trigger any FFmpeg native init.
//
// THEREFORE: NEVER dispatch "webrtc.offer" (nor "webrtc.set_resolution" / "webrtc.set_fps"
// restart paths) in any test in this file. Doing so would instantiate FFmpegVideoEncoder
// and crash on a CI box that has no FFmpeg DLLs under ReControl.Desktop/ffmpeg/.
// Routing and parsing coverage is achieved via all other command families (keyboard, mouse,
// power, terminal) which are entirely managed-code paths.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using ReControl.Desktop.Commands;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Files;
using ReControl.Desktop.Tests.Commands.Fakes;

namespace ReControl.Desktop.Tests.Commands;

public class CommandDispatcherTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// The dispatcher wraps every response in an ActionCable channel envelope:
    ///   {"command":"message","identifier":"{...}","data":"{...json...}"}
    /// This helper extracts the inner "data" string and parses it so tests
    /// can assert on the actual response fields (id, status, error, result).
    /// </summary>
    private static JsonDocument ExtractResponseData(string acEnvelope)
    {
        using var outer = JsonDocument.Parse(acEnvelope);
        var dataJson = outer.RootElement.GetProperty("data").GetString()
            ?? throw new InvalidOperationException("ActionCable envelope has no 'data' field");
        // Return a JsonDocument the caller must dispose.
        return JsonDocument.Parse(dataJson);
    }

    // -------------------------------------------------------------------------
    // Construction helper — shared across all tests in this file.
    // -------------------------------------------------------------------------

    private static (
        CommandDispatcher dispatcher,
        FakeKeyboardService keyboard,
        FakeMouseService mouse,
        FakePowerService power,
        FakeTerminalService terminal,
        List<string> sent
    ) BuildDispatcher()
    {
        var log = new LogService();
        var parser = new CommandJsonParser();
        var sent = new List<string>();
        var keyboard = new FakeKeyboardService();
        var mouse = new FakeMouseService();
        var power = new FakePowerService();
        var terminal = new FakeTerminalService();
        var processService = new ProcessService(log);
        var inputTracker = new InputStateTracker(log);
        // Pitfall 5: never touch the real user allowlist — use a temp file.
        var allowlist = new AllowlistService(log, jsonPathOverride: Path.GetTempFileName());

        var dispatcher = new CommandDispatcher(
            jsonParser: parser,
            log: log,
            sender: s => { sent.Add(s); return Task.CompletedTask; },
            terminal: terminal,
            processService: processService,
            power: power,
            keyboard: keyboard,
            mouse: mouse,
            inputTracker: inputTracker,
            allowlist: allowlist,
            screenCapture: null,   // FFmpeg avoidance — see file header
            clipboardSync: null,
            fetchIceServers: null
        );

        return (dispatcher, keyboard, mouse, power, terminal, sent);
    }

    // -------------------------------------------------------------------------
    // Task 1: Construct-smoke — proves FFmpeg-free constructability
    // -------------------------------------------------------------------------

    [Fact]
    public void Construct_with_null_screenCapture_does_not_throw_and_loads_no_ffmpeg()
    {
        // Act & Assert: construction must not throw even without FFmpeg DLLs present.
        var act = () =>
        {
            var (d, _, _, _, _, _) = BuildDispatcher();
            d.Dispose();
        };
        act.Should().NotThrow(
            "CommandDispatcher with screenCapture:null must construct on a box with no FFmpeg DLLs");
    }

    // -------------------------------------------------------------------------
    // Task 2: Routing tests — keyboard
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Routes_keyboard_typeText_and_passes_text_to_fake_keyboard()
    {
        var (dispatcher, keyboard, _, _, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"keyboard.typeText","payload":{"text":"hello"}}""");

        await dispatcher.HandleRequestAsync(req);

        keyboard.TypeTextCalls.Should().ContainSingle().Which.Should().Be("hello");
    }

    [Fact]
    public async Task Routes_keyboard_keyDown_and_passes_key_to_fake_keyboard()
    {
        var (dispatcher, keyboard, _, _, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"keyboard.keyDown","payload":{"key":65}}""");

        await dispatcher.HandleRequestAsync(req);

        keyboard.KeyDownCalls.Should().ContainSingle().Which.Should().Be(65);
    }

    [Fact]
    public async Task Routes_keyboard_keyUp_and_passes_key_to_fake_keyboard()
    {
        var (dispatcher, keyboard, _, _, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"keyboard.keyUp","payload":{"key":66}}""");

        await dispatcher.HandleRequestAsync(req);

        keyboard.KeyUpCalls.Should().ContainSingle().Which.Should().Be(66);
    }

    // -------------------------------------------------------------------------
    // Task 2: Routing tests — mouse
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Routes_mouse_click_and_records_in_fake_mouse()
    {
        var (dispatcher, _, mouse, _, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"mouse.click","payload":{"button":0,"delayMs":30}}""");

        await dispatcher.HandleRequestAsync(req);

        mouse.ClickCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Routes_mouse_scroll_and_records_in_fake_mouse()
    {
        var (dispatcher, _, mouse, _, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"mouse.scroll","payload":{"clicks":3}}""");

        await dispatcher.HandleRequestAsync(req);

        mouse.ScrollCalls.Should().ContainSingle().Which.Should().Be(3);
    }

    // -------------------------------------------------------------------------
    // Task 2: Routing tests — power
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Routes_power_shutdown_to_fake_power_shutdown()
    {
        var (dispatcher, _, _, power, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"power.shutdown","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        power.Calls.Should().ContainSingle().Which.Should().Be("shutdown");
    }

    [Fact]
    public async Task Routes_power_restart_to_fake_power_restart()
    {
        var (dispatcher, _, _, power, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"power.restart","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        power.Calls.Should().ContainSingle().Which.Should().Be("restart");
    }

    [Fact]
    public async Task Routes_power_sleep_to_fake_power_sleep()
    {
        var (dispatcher, _, _, power, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"power.sleep","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        power.Calls.Should().ContainSingle().Which.Should().Be("sleep");
    }

    [Fact]
    public async Task Routes_power_hibernate_to_fake_power_hibernate()
    {
        var (dispatcher, _, _, power, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"power.hibernate","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        power.Calls.Should().ContainSingle().Which.Should().Be("hibernate");
    }

    [Fact]
    public async Task Routes_power_logOff_to_fake_power_logOff()
    {
        var (dispatcher, _, _, power, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"power.logOff","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        power.Calls.Should().ContainSingle().Which.Should().Be("logOff");
    }

    [Fact]
    public async Task Routes_power_lock_to_fake_power_lock()
    {
        var (dispatcher, _, _, power, _, _) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"power.lock","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        power.Calls.Should().ContainSingle().Which.Should().Be("lock");
    }

    // -------------------------------------------------------------------------
    // Task 2: Unknown / malformed envelope tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Unknown_command_with_id_sends_error_response_containing_unsupported()
    {
        var (dispatcher, _, _, _, _, sent) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"id":"req-1","command":"bogus.cmd","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        sent.Should().ContainSingle(
            "an error response must be sent when id is present and command is unknown");
        using var data = ExtractResponseData(sent[0]);
        data.RootElement.GetProperty("status").GetString().Should().Be("error");
        data.RootElement.GetProperty("error").GetString().Should()
            .Contain("Unsupported command: bogus.cmd",
            "the error response must identify the unsupported command");
    }

    [Fact]
    public async Task Unknown_command_without_id_sends_no_response()
    {
        var (dispatcher, _, _, _, _, sent) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        // No "id" field — fire-and-forget: dispatcher must return early after warning, no send.
        var req = parser.ParseRequest("""{"command":"bogus.cmd","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        sent.Should().BeEmpty("no response must be sent when the request has no id");
    }

    [Fact]
    public async Task Null_request_does_not_throw_and_sends_no_response()
    {
        var (dispatcher, _, _, _, _, sent) = BuildDispatcher();
        using var _ = dispatcher;

        // Passing null — dispatcher must log a warning and return, not throw.
        var act = async () => await dispatcher.HandleRequestAsync(null!);

        await act.Should().NotThrowAsync("dispatcher must handle null request gracefully");
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_command_does_not_throw_and_sends_no_response()
    {
        var (dispatcher, _, _, _, _, sent) = BuildDispatcher();
        using var _ = dispatcher;

        var req = new BaseRequest { Id = null, Command = "" };

        var act = async () => await dispatcher.HandleRequestAsync(req);

        await act.Should().NotThrowAsync("dispatcher must handle empty command gracefully");
        sent.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Task 2: Success/error response gating on request id
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Command_with_id_produces_success_response()
    {
        var (dispatcher, _, _, power, _, sent) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"id":"req-42","command":"power.lock","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        sent.Should().ContainSingle("success response must be sent when id is present");
        using var data = ExtractResponseData(sent[0]);
        data.RootElement.GetProperty("status").GetString().Should().Be("success",
            "the response must be a success envelope");
    }

    [Fact]
    public async Task Command_without_id_produces_no_response()
    {
        var (dispatcher, _, _, power, _, sent) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        // No "id" field — fire-and-forget.
        var req = parser.ParseRequest("""{"command":"power.lock","payload":{}}""");

        await dispatcher.HandleRequestAsync(req);

        sent.Should().BeEmpty("no response must be sent when the request has no id (fire-and-forget)");
    }

    [Fact]
    public async Task Handler_throws_with_id_sends_error_response_and_does_not_crash()
    {
        var (dispatcher, _, _, _, terminal, sent) = BuildDispatcher();
        using var _ = dispatcher;

        // Make the fake terminal throw on the next ExecuteAsync call.
        terminal.ThrowOnExecute = new InvalidOperationException("fake terminal failure");

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"id":"err-1","command":"terminal.execute","payload":{"command":"echo hi","timeout":1000}}""");

        // Dispatch must NOT throw even when the handler throws.
        var act = async () => await dispatcher.HandleRequestAsync(req);
        await act.Should().NotThrowAsync("dispatcher must catch handler exceptions");

        sent.Should().ContainSingle("error response must be sent when handler throws and id is present");
        using var data = ExtractResponseData(sent[0]);
        data.RootElement.GetProperty("status").GetString().Should().Be("error");
        data.RootElement.GetProperty("error").GetString().Should().Contain("fake terminal failure");
    }

    [Fact]
    public async Task Handler_throws_without_id_does_not_crash_and_sends_no_response()
    {
        var (dispatcher, _, _, _, terminal, sent) = BuildDispatcher();
        using var _ = dispatcher;

        terminal.ThrowOnExecute = new InvalidOperationException("throw without id");

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"command":"terminal.execute","payload":{"command":"ls","timeout":1000}}""");

        var act = async () => await dispatcher.HandleRequestAsync(req);
        await act.Should().NotThrowAsync();

        sent.Should().BeEmpty("no response must be sent when id is absent, even on handler throw");
    }

    // -------------------------------------------------------------------------
    // Task 2: No-payload commands complete without throw (terminal.listProcesses)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Routes_terminal_listProcesses_without_throw()
    {
        var (dispatcher, _, _, _, _, sent) = BuildDispatcher();
        using var _ = dispatcher;

        var parser = new CommandJsonParser();
        var req = parser.ParseRequest("""{"id":"lp-1","command":"terminal.listProcesses","payload":{}}""");

        var act = async () => await dispatcher.HandleRequestAsync(req);
        await act.Should().NotThrowAsync();

        sent.Should().ContainSingle();
        using var data = ExtractResponseData(sent[0]);
        data.RootElement.GetProperty("status").GetString().Should().Be("success");
    }
}
