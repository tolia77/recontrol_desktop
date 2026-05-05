namespace ReControl.Desktop.Services.Clipboard;

public sealed class ClipboardSettings
{
    public int Version { get; set; } = 1;
    public bool Master { get; set; } = true;
    public bool AllowOutbound { get; set; } = true;
    public bool AllowInbound { get; set; } = true;

    public static ClipboardSettings Defaults => new();
}
