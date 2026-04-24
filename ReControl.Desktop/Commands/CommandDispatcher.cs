using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ReControl.Desktop.Commands.Input;
using ReControl.Desktop.Commands.Power;
using ReControl.Desktop.Commands.Terminal;
using ReControl.Desktop.Commands.WebRtc;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Files;
using ReControl.Desktop.Services.Files.FilesProtocol;
using ReControl.Desktop.Services.Interfaces;
using ReControl.Desktop.WebSocket;

namespace ReControl.Desktop.Commands;

/// <summary>
/// Routes incoming command requests to the appropriate command handler.
/// All command groups (keyboard, mouse, terminal, power, webrtc) are wired to real implementations.
/// Ported from WPF CommandDispatcher.
/// </summary>
public class CommandDispatcher : IDisposable
{
    private readonly CommandJsonParser _jsonParser;
    private readonly LogService _log;
    private readonly Func<string, Task> _sender;
    private readonly ITerminalService _terminal;
    private readonly ProcessService _processService;
    private readonly IPowerService _power;
    private readonly IKeyboardService _keyboard;
    private readonly IMouseService _mouse;
    private readonly InputStateTracker _inputTracker;
    private readonly WebRtcService _webRtcService;

    // Phase 9 Plan 09-05: file-transfer control-plane services. Constructed here so the
    // AllowlistService FileSystemWatcher lives for the whole process; PathCanonicalizer
    // reads live roots from AllowlistService so hot-reload propagates automatically.
    // The command-handlers factory is invoked lazily per offer from WebRtcService.ondatachannel
    // (files-ctl branch), which guarantees a fresh dictionary if future code wants to
    // rebuild the handler set on reconfiguration.
    private readonly AllowlistService _allowlist;
    private readonly FileOperationsService _fileOps;

    private readonly Dictionary<string, Func<JsonElement, IAppCommand>> _commandFactories;

    private bool _disposed;

    public CommandDispatcher(CommandJsonParser jsonParser, LogService log, Func<string, Task> sender, ITerminalService terminal, ProcessService processService, IPowerService power, IKeyboardService keyboard, IMouseService mouse, InputStateTracker inputTracker, IScreenCaptureService? screenCapture = null)
    {
        _jsonParser = jsonParser ?? throw new ArgumentNullException(nameof(jsonParser));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _power = power ?? throw new ArgumentNullException(nameof(power));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _mouse = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _inputTracker = inputTracker ?? throw new ArgumentNullException(nameof(inputTracker));

        // Allowlist + canonicalizer + file-ops construction graph. AllowlistService seeds
        // Documents + Downloads on first run (per Plan 09-01) and watches the JSON file for
        // hot-reload changes. PathCanonicalizer consumes the live root set. FileOperationsService
        // routes every user-supplied path through the canonicalizer BEFORE touching disk, so
        // the command handlers inherit that guarantee transitively.
        _allowlist = new AllowlistService(log);
        var canonicalizer = new PathCanonicalizer(_allowlist);
        _fileOps = new FileOperationsService(canonicalizer, _allowlist, log);
        var fileOps = _fileOps;
        Func<IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>>> filesHandlersFactory =
            () => FilesCommandHandlers.Build(fileOps);

        _webRtcService = new WebRtcService(log, async msg =>
        {
            var channelMessage = ActionCableProtocol.CreateChannelMessage(
                JsonSerializer.Deserialize<JsonElement>(msg));
            await sender(channelMessage);
        }, screenCapture, fileOps, filesHandlersFactory);

        _commandFactories = new Dictionary<string, Func<JsonElement, IAppCommand>>
        {
            // Keyboard -- real implementations
            { "keyboard.keyDown", payload =>
            {
                var args = _jsonParser.DeserializePayload<KeyPayload>(payload);
                return new KeyDownCommand(_keyboard, _inputTracker, args);
            }},
            { "keyboard.keyUp", payload =>
            {
                var args = _jsonParser.DeserializePayload<KeyPayload>(payload);
                return new KeyUpCommand(_keyboard, _inputTracker, args);
            }},
            { "keyboard.press", payload =>
            {
                var args = _jsonParser.DeserializePayload<KeyPressPayload>(payload);
                return new KeyPressCommand(_keyboard, args);
            }},

            // Mouse -- real implementations
            { "mouse.move", payload =>
            {
                var args = _jsonParser.DeserializePayload<MouseMovePayload>(payload);
                var (sx, sy) = _webRtcService.GetCoordinateScale();
                args.X = (int)(args.X * sx);
                args.Y = (int)(args.Y * sy);
                return new MouseMoveCommand(_mouse, args);
            }},
            { "mouse.down", payload =>
            {
                var args = _jsonParser.DeserializePayload<MouseButtonPayload>(payload);
                return new MouseDownCommand(_mouse, _inputTracker, args);
            }},
            { "mouse.up", payload =>
            {
                var args = _jsonParser.DeserializePayload<MouseButtonPayload>(payload);
                return new MouseUpCommand(_mouse, _inputTracker, args);
            }},
            { "mouse.scroll", payload =>
            {
                var args = _jsonParser.DeserializePayload<MouseScrollPayload>(payload);
                return new MouseScrollCommand(_mouse, args);
            }},
            { "mouse.click", payload =>
            {
                var args = _jsonParser.DeserializePayload<MouseClickPayload>(payload);
                return new MouseClickCommand(_mouse, args);
            }},
            { "mouse.doubleClick", payload =>
            {
                var args = _jsonParser.DeserializePayload<MouseDoubleClickPayload>(payload);
                return new MouseDoubleClickCommand(_mouse, args);
            }},
            { "mouse.rightClick", _ => new MouseRightClickCommand(_mouse) },

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

            // Power -- real implementations
            { "power.shutdown", _ => new PowerShutdownCommand(_power) },
            { "power.restart", _ => new PowerRestartCommand(_power) },
            { "power.sleep", _ => new PowerSleepCommand(_power) },
            { "power.hibernate", _ => new PowerHibernateCommand(_power) },
            { "power.logOff", _ => new PowerLogOffCommand(_power) },
            { "power.lock", _ => new PowerLockCommand(_power) },

            // WebRTC -- real implementations
            { "webrtc.offer", payload =>
            {
                var sdp = payload.GetProperty("sdp").GetString() ?? "";
                return new WebRtcOfferCommand(_webRtcService, sdp);
            }},
            { "webrtc.ice_candidate", payload =>
            {
                var candidate = payload.GetProperty("candidate").GetString() ?? "";
                string? sdpMid = payload.TryGetProperty("sdpMid", out var midEl) ? midEl.GetString() : null;
                ushort? sdpMLineIndex = payload.TryGetProperty("sdpMLineIndex", out var idxEl) ? idxEl.GetUInt16() : null;
                return new WebRtcIceCandidateCommand(_webRtcService, candidate, sdpMid, sdpMLineIndex);
            }},
            { "webrtc.stop", _ => new WebRtcStopCommand(_webRtcService) },
            { "webrtc.set_fps", payload =>
            {
                var fps = payload.GetProperty("fps").GetInt32();
                return new WebRtcSetFpsCommand(_webRtcService, fps);
            }},
            { "webrtc.set_resolution", payload =>
            {
                var resolution = payload.GetProperty("resolution").GetInt32();
                return new WebRtcSetResolutionCommand(_webRtcService, resolution);
            }},
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

            // Send response only if the command had an id (fire-and-forget commands have no id)
            if (request.Id != null)
            {
                var responsePayload = _jsonParser.SerializeSuccess(request.Id, result);
                await SendResponseAsync(responsePayload);
            }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _webRtcService.Dispose(); } catch (Exception ex) { _log.Error("CommandDispatcher: webrtc dispose failed", ex); }
        // AllowlistService owns the FileSystemWatcher started for hot-reload; dispose it
        // so the watcher thread unblocks cleanly on shutdown.
        try { _allowlist.Dispose(); } catch (Exception ex) { _log.Error("CommandDispatcher: allowlist dispose failed", ex); }
    }
}
