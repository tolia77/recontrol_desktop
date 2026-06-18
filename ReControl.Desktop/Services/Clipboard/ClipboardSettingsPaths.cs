using System;
using System.IO;

namespace ReControl.Desktop.Services.Clipboard;

public static class ClipboardSettingsPaths
{
    public static string DefaultJsonPath()
    {
        // Per-OS settings location:
        //   macOS:   ~/Library/Application Support/<App>/...
        //   Linux:   XDG default ~/.config/<App>/...
        //   Windows: %APPDATA%\<App>\...
        string appData;
        if (OperatingSystem.IsWindows())
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support");
        }
        else
        {
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }
        return Path.Combine(appData, "ReControl", "clipboard", "clipboard.json");
    }
}
