namespace ReControl.Desktop.Models;

// ==================== RESPONSE WRAPPERS ====================

/// <summary>
/// Base class for all responses sent to the server.
/// </summary>
public abstract class BaseResponse
{
    public string Id { get; set; }
    public string Status { get; set; }

    protected BaseResponse(string id, string status)
    {
        Id = id;
        Status = status;
    }
}

/// <summary>
/// A successful response containing the result of the operation.
/// </summary>
public class SuccessResponse : BaseResponse
{
    public object? Result { get; set; }

    public SuccessResponse(string id, object? result) : base(id, "success")
    {
        Result = result;
    }
}

/// <summary>
/// An error response containing a description of what went wrong.
/// </summary>
public class ErrorResponse : BaseResponse
{
    public string Error { get; set; }

    public ErrorResponse(string id, string error) : base(id, "error")
    {
        Error = error;
    }
}

// ==================== COMMAND-SPECIFIC PAYLOADS (DTOs) ====================
// Ported from WPF ResponseModels. Platform-specific enums replaced with
// portable types (int for keys, string for mouse buttons).

// Keyboard Payloads

public class KeyPayload
{
    /// <summary>
    /// Platform-neutral key code. Mapped to platform-specific key in later phases.
    /// </summary>
    public int Key { get; set; }
}

public class KeyPressPayload
{
    public int Key { get; set; }
    public int HoldMs { get; set; } = 30;
}

// Mouse Payloads

public class MouseMovePayload
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class MouseButtonPayload
{
    public string Button { get; set; } = "left";
}

public class MouseScrollPayload
{
    public int Clicks { get; set; }
}

public class MouseClickPayload
{
    public string Button { get; set; } = "left";
    public int DelayMs { get; set; } = 30;
}

public class MouseDoubleClickPayload
{
    public int DelayMs { get; set; } = 120;
}

// Terminal Payloads

public class TerminalCommandPayload
{
    public string Command { get; set; } = string.Empty;
    public int Timeout { get; set; } = 30000;
    /// <summary>
    /// User-selected shell (e.g. "cmd.exe", "/bin/bash"). Null = platform default.
    /// </summary>
    public string? Shell { get; set; }
}

public class TerminalKillPayload
{
    public int Pid { get; set; }
    public bool Force { get; set; }
}

public class TerminalStartPayload
{
    public string FileName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public bool RedirectOutput { get; set; }
}

public class TerminalSetCwdPayload
{
    public string Path { get; set; } = string.Empty;
}

// Screen Payloads

public class ScreenCapturePayload
{
    public int Display { get; set; }
    public string Format { get; set; } = "png";
}

public class ScreenStopPayload
{
    public int Display { get; set; }
}
