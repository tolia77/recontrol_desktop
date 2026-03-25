using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ReControl.Desktop.Commands.Terminal;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;
using ReControl.Desktop.WebSocket;

namespace ReControl.Desktop.Commands;

/// <summary>
/// Routes incoming command requests to the appropriate command handler.
/// Terminal commands are wired to real implementations. Other command groups
/// (keyboard, mouse, power, webrtc) remain stubs until their phases.
/// Ported from WPF CommandDispatcher.
/// </summary>
public class CommandDispatcher
{
    private readonly CommandJsonParser _jsonParser;
    private readonly LogService _log;
    private readonly Func<string, Task> _sender;
    private readonly ITerminalService _terminal;
    private readonly ProcessService _processService;

    private readonly Dictionary<string, Func<JsonElement, IAppCommand>> _commandFactories;

    public CommandDispatcher(CommandJsonParser jsonParser, LogService log, Func<string, Task> sender, ITerminalService terminal, ProcessService processService)
    {
        _jsonParser = jsonParser ?? throw new ArgumentNullException(nameof(jsonParser));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));

        _commandFactories = new Dictionary<string, Func<JsonElement, IAppCommand>>
        {
            // Keyboard
            { "keyboard.keyDown", _ => new StubCommand("keyboard.keyDown", _log) },
            { "keyboard.keyUp", _ => new StubCommand("keyboard.keyUp", _log) },
            { "keyboard.press", _ => new StubCommand("keyboard.press", _log) },

            // Mouse
            { "mouse.move", _ => new StubCommand("mouse.move", _log) },
            { "mouse.down", _ => new StubCommand("mouse.down", _log) },
            { "mouse.up", _ => new StubCommand("mouse.up", _log) },
            { "mouse.scroll", _ => new StubCommand("mouse.scroll", _log) },
            { "mouse.click", _ => new StubCommand("mouse.click", _log) },
            { "mouse.doubleClick", _ => new StubCommand("mouse.doubleClick", _log) },
            { "mouse.rightClick", _ => new StubCommand("mouse.rightClick", _log) },

            // Terminal -- real implementations
            { "terminal.execute", payload =>
            {
                var args = _jsonParser.DeserializePayload<TerminalCommandPayload>(payload);
                return new TerminalExecuteCommand(_terminal, args, _sender);
            }},
            { "terminal.powershell", payload =>
            {
                var args = _jsonParser.DeserializePayload<TerminalCommandPayload>(payload);
                return new TerminalPowerShellCommand(_terminal, args, _sender);
            }},
            { "terminal.listProcesses", _ => new TerminalListProcessesCommand(_processService) },
            { "terminal.killProcess", payload =>
            {
                var args = _jsonParser.DeserializePayload<TerminalKillPayload>(payload);
                return new TerminalKillProcessCommand(_processService, args);
            }},
            { "terminal.startProcess", payload =>
            {
                var args = _jsonParser.DeserializePayload<TerminalStartPayload>(payload);
                return new TerminalStartProcessCommand(_processService, args);
            }},
            { "terminal.getCwd", payload =>
            {
                string? shell = null;
                try { shell = _jsonParser.DeserializePayload<TerminalCommandPayload>(payload).Shell; } catch { }
                return new TerminalGetCwdCommand(_terminal, shell);
            }},
            { "terminal.setCwd", payload =>
            {
                var args = _jsonParser.DeserializePayload<TerminalSetCwdPayload>(payload);
                string? shell = null;
                try { shell = payload.GetProperty("shell").GetString(); } catch { }
                return new TerminalSetCwdCommand(_terminal, args, shell);
            }},
            { "terminal.whoAmI", _ => new TerminalWhoAmICommand(_terminal) },
            { "terminal.getUptime", _ => new TerminalGetUptimeCommand(_terminal) },
            { "terminal.abort", payload =>
            {
                string? shell = null;
                try { shell = payload.GetProperty("shell").GetString(); } catch { }
                return new TerminalAbortCommand(_terminal, shell);
            }},
            { "terminal.getShells", _ => new TerminalGetShellsCommand(_terminal) },

            // Power
            { "power.shutdown", _ => new StubCommand("power.shutdown", _log) },
            { "power.restart", _ => new StubCommand("power.restart", _log) },
            { "power.sleep", _ => new StubCommand("power.sleep", _log) },
            { "power.hibernate", _ => new StubCommand("power.hibernate", _log) },
            { "power.logOff", _ => new StubCommand("power.logOff", _log) },
            { "power.lock", _ => new StubCommand("power.lock", _log) },

            // WebRTC
            { "webrtc.offer", _ => new StubCommand("webrtc.offer", _log) },
            { "webrtc.ice_candidate", _ => new StubCommand("webrtc.ice_candidate", _log) },
            { "webrtc.stop", _ => new StubCommand("webrtc.stop", _log) },
        };
    }

    /// <summary>
    /// Handle a parsed request by creating and executing the corresponding command.
    /// Sends the response back through the WebSocket channel.
    /// </summary>
    public async Task HandleRequestAsync(BaseRequest request)
    {
        try
        {
            if (request == null || string.IsNullOrEmpty(request.Command))
            {
                _log.Warning("CommandDispatcher: invalid request or missing command");
                return;
            }

            if (!_commandFactories.TryGetValue(request.Command, out var factory))
            {
                _log.Warning($"CommandDispatcher: unsupported command '{request.Command}'");
                if (request.Id != null)
                {
                    var errorResponse = _jsonParser.SerializeError(request.Id, $"Unsupported command: {request.Command}");
                    await SendResponseAsync(errorResponse);
                }
                return;
            }

            _log.Info($"CommandDispatcher: executing '{request.Command}'");
            var command = factory(request.Payload);
            var result = await command.ExecuteAsync();

            // Send response
            string? responsePayload;
            if (request.Id != null)
            {
                responsePayload = _jsonParser.SerializeSuccess(request.Id, result);
            }
            else
            {
                responsePayload = JsonSerializer.Serialize(new
                {
                    command = "result",
                    id = request.Id,
                    request = request.Command,
                    payload = result
                });
            }

            await SendResponseAsync(responsePayload);
        }
        catch (Exception ex)
        {
            _log.Error($"CommandDispatcher: error processing '{request?.Command}'", ex);
            if (request?.Id != null)
            {
                var errorResponse = _jsonParser.SerializeError(request.Id, ex.Message);
                await SendResponseAsync(errorResponse);
            }
        }
    }

    private async Task SendResponseAsync(string responsePayload)
    {
        try
        {
            var channelMessage = ActionCableProtocol.CreateChannelMessage(
                JsonSerializer.Deserialize<JsonElement>(responsePayload));
            await _sender(channelMessage);
        }
        catch (Exception ex)
        {
            _log.Error("CommandDispatcher: failed to send response", ex);
        }
    }
}

/// <summary>
/// Stub command that logs the command name and returns a not_implemented status.
/// Will be replaced with real implementations in later phases.
/// </summary>
internal class StubCommand : IAppCommand
{
    private readonly string _commandName;
    private readonly LogService _log;

    public StubCommand(string commandName, LogService log)
    {
        _commandName = commandName;
        _log = log;
    }

    public Task<object?> ExecuteAsync()
    {
        _log.Info($"StubCommand: '{_commandName}' invoked (not_implemented)");
        return Task.FromResult<object?>(new { status = "not_implemented", command = _commandName });
    }
}
