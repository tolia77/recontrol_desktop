using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using ReControl.Desktop.Commands.Input;
using ReControl.Desktop.Commands.Permissions;
using ReControl.Desktop.Commands.Power;
using ReControl.Desktop.Commands.Terminal;
using ReControl.Desktop.Commands.WebRtc;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Clipboard;
using ReControl.Desktop.Services.Files;
using ReControl.Desktop.Services.Files.FilesProtocol;
using ReControl.Desktop.Services.Interfaces;
using ReControl.Desktop.WebSocket;
using SIPSorcery.Net;

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
    // Phase 11: process-wide registry of in-flight file transfers. Kept
    // as a CommandDispatcher field so its u32 id counter survives WebRTC
    // reconnects; CleanupPeerConnection invokes CancelAll to wipe state.
    private readonly TransferRegistry _transferRegistry;

    private readonly Dictionary<string, Func<BaseRequest, IAppCommand>> _commandFactories;

    private bool _disposed;

    public CommandDispatcher(CommandJsonParser jsonParser, LogService log, Func<string, Task> sender, ITerminalService terminal, ProcessService processService, IPowerService power, IKeyboardService keyboard, IMouseService mouse, InputStateTracker inputTracker, AllowlistService allowlist, IScreenCaptureService? screenCapture = null, ClipboardSyncService? clipboardSync = null, Func<Task<List<RTCIceServer>>>? fetchIceServers = null)
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
        _allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));

        // Allowlist + canonicalizer + file-ops construction graph. AllowlistService seeds
        // Documents + Downloads on first run (per Plan 09-01) and watches the JSON file for
        // hot-reload changes. PathCanonicalizer consumes the live root set. FileOperationsService
        // routes every user-supplied path through the canonicalizer BEFORE touching disk, so
        // the command handlers inherit that guarantee transitively.
        var canonicalizer = new PathCanonicalizer(_allowlist, log);
        _fileOps = new FileOperationsService(canonicalizer, _allowlist, log);
        _transferRegistry = new TransferRegistry();
        var fileOps = _fileOps;
        var registry = _transferRegistry;

        // Plan 11-02: the FilesCommandHandlers factory needs access to the
        // raw files-data RTCDataChannel (for DownloadSender's send loop)
        // and the FilesCtlChannel wrapper (for PushEventAsync). Both are
        // created INSIDE WebRtcService.HandleOfferAsync when the SDP offer
        // arrives -- AFTER this constructor runs. Solve the chicken-and-egg
        // with deferred-accessor closures: the lambdas resolve through
        // _webRtcService's public properties, which are populated when the
        // ondatachannel callback fires. The closures see the live channel
        // on every handler invocation; reconnects automatically pick up
        // the new channels.
        Func<RTCDataChannel?> getFilesData = () => _webRtcService?.FilesDataChannel;
        Func<FilesCtlChannel?> getFilesCtl = () => _webRtcService?.FilesCtlChannel;
        Func<IReadOnlyDictionary<string, Func<JsonElement, Task<object?>>>> filesHandlersFactory =
            () => FilesCommandHandlers.Build(
                fileOps, registry, canonicalizer, _allowlist,
                getFilesData, getFilesCtl, log);

        _webRtcService = new WebRtcService(log, async msg =>
        {
            var channelMessage = ActionCableProtocol.CreateChannelMessage(
                JsonSerializer.Deserialize<JsonElement>(msg));
            await sender(channelMessage);
        }, screenCapture, fileOps, filesHandlersFactory, registry, clipboardSync, fetchIceServers);

        _commandFactories = new Dictionary<string, Func<BaseRequest, IAppCommand>>
        {
            // Keyboard -- real implementations
            { "keyboard.keyDown", req =>
            {
                var args = _jsonParser.DeserializePayload<KeyPayload>(req.Payload);
                return new KeyDownCommand(_keyboard, _inputTracker, args);
            }},
            { "keyboard.keyUp", req =>
            {
                var args = _jsonParser.DeserializePayload<KeyPayload>(req.Payload);
                return new KeyUpCommand(_keyboard, _inputTracker, args);
            }},
            { "keyboard.press", req =>
            {
                var args = _jsonParser.DeserializePayload<KeyPressPayload>(req.Payload);
                return new KeyPressCommand(_keyboard, args);
            }},
            { "keyboard.typeText", req =>
            {
                var args = _jsonParser.DeserializePayload<TypeTextPayload>(req.Payload);
                return new TypeTextCommand(_keyboard, args);
            }},

            // Mouse -- real implementations
            { "mouse.move", req =>
            {
                var args = _jsonParser.DeserializePayload<MouseMovePayload>(req.Payload);
                var (sx, sy) = _webRtcService.GetCoordinateScale();
                args.X = (int)(args.X * sx);
                args.Y = (int)(args.Y * sy);
                return new MouseMoveCommand(_mouse, args);
            }},
            { "mouse.down", req =>
            {
                var args = _jsonParser.DeserializePayload<MouseButtonPayload>(req.Payload);
                return new MouseDownCommand(_mouse, _inputTracker, args);
            }},
            { "mouse.up", req =>
            {
                var args = _jsonParser.DeserializePayload<MouseButtonPayload>(req.Payload);
                return new MouseUpCommand(_mouse, _inputTracker, args);
            }},
            { "mouse.scroll", req =>
            {
                var args = _jsonParser.DeserializePayload<MouseScrollPayload>(req.Payload);
                return new MouseScrollCommand(_mouse, args);
            }},
            { "mouse.click", req =>
            {
                var args = _jsonParser.DeserializePayload<MouseClickPayload>(req.Payload);
                return new MouseClickCommand(_mouse, args);
            }},
            { "mouse.doubleClick", req =>
            {
                var args = _jsonParser.DeserializePayload<MouseDoubleClickPayload>(req.Payload);
                return new MouseDoubleClickCommand(_mouse, args);
            }},
            { "mouse.rightClick", _ => new MouseRightClickCommand(_mouse) },

            // Terminal -- real implementations
            { "terminal.execute", req =>
            {
                var args = _jsonParser.DeserializePayload<TerminalCommandPayload>(req.Payload);
                return new TerminalExecuteCommand(_terminal, args, _sender);
            }},
            { "terminal.powershell", req =>
            {
                var args = _jsonParser.DeserializePayload<TerminalCommandPayload>(req.Payload);
                return new TerminalPowerShellCommand(_terminal, args, _sender);
            }},
            { "terminal.listProcesses", _ => new TerminalListProcessesCommand(_processService) },
            { "terminal.runCommand", req =>
            {
                var args = _jsonParser.DeserializePayload<TerminalRunCommandPayload>(req.Payload);
                return new TerminalRunCommandCommand(args, _log);
            }},
            { "terminal.killProcess", req =>
            {
                var args = _jsonParser.DeserializePayload<TerminalKillPayload>(req.Payload);
                return new TerminalKillProcessCommand(_processService, args);
            }},
            { "terminal.startProcess", req =>
            {
                var args = _jsonParser.DeserializePayload<TerminalStartPayload>(req.Payload);
                return new TerminalStartProcessCommand(_processService, args);
            }},
            { "terminal.getCwd", req =>
            {
                string? shell = null;
                try { shell = _jsonParser.DeserializePayload<TerminalCommandPayload>(req.Payload).Shell; } catch { }
                return new TerminalGetCwdCommand(_terminal, shell);
            }},
            { "terminal.setCwd", req =>
            {
                var args = _jsonParser.DeserializePayload<TerminalSetCwdPayload>(req.Payload);
                string? shell = null;
                try { shell = req.Payload.GetProperty("shell").GetString(); } catch { }
                return new TerminalSetCwdCommand(_terminal, args, shell);
            }},
            { "terminal.whoAmI", _ => new TerminalWhoAmICommand(_terminal) },
            { "terminal.getUptime", _ => new TerminalGetUptimeCommand(_terminal) },
            { "terminal.abort", req =>
            {
                string? shell = null;
                try { shell = req.Payload.GetProperty("shell").GetString(); } catch { }
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
            { "webrtc.offer", req =>
            {
                var sdp = req.Payload.GetProperty("sdp").GetString() ?? "";
                return new WebRtcOfferCommand(_webRtcService, sdp, req.Permissions);
            }},
            { "permissions.update", req =>
            {
                JsonElement perms = default;
                if (req.Payload.ValueKind == JsonValueKind.Object &&
                    req.Payload.TryGetProperty("permissions", out var p) &&
                    p.ValueKind == JsonValueKind.Object)
                {
                    perms = p;
                }
                return new PermissionsUpdateCommand(_webRtcService, perms);
            }},
            { "webrtc.ice_candidate", req =>
            {
                var candidate = req.Payload.GetProperty("candidate").GetString() ?? "";
                string? sdpMid = req.Payload.TryGetProperty("sdpMid", out var midEl) ? midEl.GetString() : null;
                ushort? sdpMLineIndex = req.Payload.TryGetProperty("sdpMLineIndex", out var idxEl) ? idxEl.GetUInt16() : null;
                return new WebRtcIceCandidateCommand(_webRtcService, candidate, sdpMid, sdpMLineIndex);
            }},
            { "webrtc.stop", _ => new WebRtcStopCommand(_webRtcService) },
            { "webrtc.set_fps", req =>
            {
                var fps = req.Payload.GetProperty("fps").GetInt32();
                return new WebRtcSetFpsCommand(_webRtcService, fps);
            }},
            { "webrtc.set_resolution", req =>
            {
                var resolution = req.Payload.GetProperty("resolution").GetInt32();
                return new WebRtcSetResolutionCommand(_webRtcService, resolution);
            }},
            { "webrtc.request_keyframe", _ => new WebRtcRequestKeyframeCommand(_webRtcService) },
        };
    }

    /// <summary>
    /// Handle a parsed request by creating and executing the corresponding command.
    /// Sends the response back through the WebSocket channel.
    /// Every dispatched command produces one structured timing log line (D-06).
    /// </summary>
    public async Task HandleRequestAsync(BaseRequest request)
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

        var sw = Stopwatch.StartNew();
        string? outcome = null;
        try
        {
            var command = factory(request);
            var result = await command.ExecuteAsync();
            outcome = "ok";

            // Send response only if the command had an id (fire-and-forget commands have no id)
            if (request.Id != null)
            {
                var responsePayload = _jsonParser.SerializeSuccess(request.Id, result);
                await SendResponseAsync(responsePayload);
            }
        }
        catch (Exception ex)
        {
            outcome = "error";
            _log.Error($"CommandDispatcher: error processing '{request.Command}'", ex);
            if (request.Id != null)
            {
                var errorResponse = _jsonParser.SerializeError(request.Id, ex.Message);
                await SendResponseAsync(errorResponse);
            }
        }
        finally
        {
            sw.Stop();
            int durationUs = (int)(sw.Elapsed.TotalMilliseconds * 1000);
            string redacted = RedactParams(request.Command, request.Payload);
            _log.Info($"command dispatch cmd={request.Command} outcome={outcome} durationUs={durationUs}{(redacted.Length > 0 ? " " + redacted : "")}");
        }
    }

    /// <summary>
    /// Returns a whitelist-only redacted param summary for logging.
    /// SECURITY: MUST NOT include terminal command bodies, clipboard text, file contents, or file names.
    /// Only command type is already in the caller log line; this helper adds scalar safe identifiers.
    /// </summary>
    private static string RedactParams(string commandType, JsonElement payload)
    {
        try
        {
            // Keyboard: log key name/code only (not text typed — typeText body is PII-adjacent)
            if (commandType is "keyboard.keyDown" or "keyboard.keyUp" or "keyboard.press")
            {
                if (payload.ValueKind == JsonValueKind.Object &&
                    payload.TryGetProperty("key", out var keyEl))
                    return $"key={keyEl.GetString()}";
                return string.Empty;
            }

            // keyboard.typeText: log only the byte length, never the text content
            if (commandType == "keyboard.typeText")
            {
                if (payload.ValueKind == JsonValueKind.Object &&
                    payload.TryGetProperty("text", out var textEl))
                {
                    var text = textEl.GetString();
                    return $"textBytes={System.Text.Encoding.UTF8.GetByteCount(text ?? string.Empty)}";
                }
                return string.Empty;
            }

            // Mouse: log button/scroll delta (safe scalars)
            if (commandType is "mouse.down" or "mouse.up" or "mouse.click")
            {
                if (payload.ValueKind == JsonValueKind.Object &&
                    payload.TryGetProperty("button", out var btnEl))
                    return $"button={btnEl.GetString()}";
                return string.Empty;
            }
            if (commandType == "mouse.scroll")
            {
                if (payload.ValueKind == JsonValueKind.Object &&
                    payload.TryGetProperty("delta", out var deltaEl))
                    return $"delta={deltaEl.GetInt32()}";
                return string.Empty;
            }

            // Terminal commands: log ONLY the command verb/type — never the command body or arguments
            if (commandType.StartsWith("terminal.", StringComparison.Ordinal))
                return string.Empty;

            // Power: no params needed — command type is the identifier
            if (commandType.StartsWith("power.", StringComparison.Ordinal))
                return string.Empty;

            // WebRTC: log safe operation identifiers
            if (commandType == "webrtc.set_fps")
            {
                if (payload.ValueKind == JsonValueKind.Object &&
                    payload.TryGetProperty("fps", out var fpsEl))
                    return $"fps={fpsEl.GetInt32()}";
                return string.Empty;
            }
            if (commandType == "webrtc.set_resolution")
            {
                if (payload.ValueKind == JsonValueKind.Object &&
                    payload.TryGetProperty("resolution", out var resEl))
                    return $"resolution={resEl.GetInt32()}";
                return string.Empty;
            }

            return string.Empty;
        }
        catch
        {
            // Never let redaction failure propagate — return empty
            return string.Empty;
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
    }
}
