using System;
using System.Collections.Generic;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Models;

/// <summary>
/// Tracks currently held keyboard keys and mouse buttons.
/// On WebSocket disconnect, releases all held inputs to prevent stuck keys/buttons.
/// Thread-safe: disconnect can occur on a different thread than command processing.
/// </summary>
public class InputStateTracker
{
    private readonly HashSet<ushort> _heldKeys = new();
    private readonly HashSet<int> _heldButtons = new();
    private readonly object _lock = new();
    private readonly LogService _log;

    public InputStateTracker(LogService log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void OnKeyDown(ushort vk)
    {
        lock (_lock)
        {
            _heldKeys.Add(vk);
        }
    }

    public void OnKeyUp(ushort vk)
    {
        lock (_lock)
        {
            _heldKeys.Remove(vk);
        }
    }

    public void OnMouseDown(int button)
    {
        lock (_lock)
        {
            _heldButtons.Add(button);
        }
    }

    public void OnMouseUp(int button)
    {
        lock (_lock)
        {
            _heldButtons.Remove(button);
        }
    }

    /// <summary>
    /// Release all held keys and mouse buttons. Called on WebSocket disconnect
    /// to prevent stuck inputs on the remote machine.
    /// </summary>
    public void ReleaseAll(IKeyboardService keyboard, IMouseService mouse)
    {
        lock (_lock)
        {
            var keyCount = _heldKeys.Count;
            var buttonCount = _heldButtons.Count;

            if (keyCount > 0 || buttonCount > 0)
            {
                _log.Info($"InputStateTracker: releasing {keyCount} held keys and {buttonCount} held mouse buttons");
            }

            foreach (var vk in _heldKeys)
            {
                try
                {
                    keyboard.KeyUp(vk);
                }
                catch (Exception ex)
                {
                    _log.Warning($"InputStateTracker: failed to release key 0x{vk:X2}: {ex.Message}");
                }
            }

            foreach (var button in _heldButtons)
            {
                try
                {
                    mouse.MouseUp(button);
                }
                catch (Exception ex)
                {
                    _log.Warning($"InputStateTracker: failed to release mouse button {button}: {ex.Message}");
                }
            }

            _heldKeys.Clear();
            _heldButtons.Clear();
        }
    }
}
