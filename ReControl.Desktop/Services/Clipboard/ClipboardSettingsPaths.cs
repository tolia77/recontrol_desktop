using System;
using System.IO;

namespace ReControl.Desktop.Services.Clipboard;

public static class ClipboardSettingsPaths
{
    public static string DefaultJsonPath()
    {
        var appData = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ReControl", "clipboard", "clipboard.json");
    }
}
