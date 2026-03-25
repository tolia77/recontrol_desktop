using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ReControl.Desktop.Services.Terminal;

public static class ShellLocator
{
    public static List<string> GetAvailableShells()
    {
        var shells = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            shells.Add("cmd.exe");
            shells.Add("powershell.exe");
            if (IsInPath("pwsh.exe"))
                shells.Add("pwsh.exe");
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                var lines = File.ReadAllLines("/etc/shells");
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                        continue;
                    if (File.Exists(trimmed))
                        shells.Add(trimmed);
                }
            }
            catch
            {
                shells.Add("/bin/bash");
                shells.Add("/bin/sh");
            }

            if (IsInPath("pwsh"))
                shells.Add("pwsh");
        }

        return shells;
    }

    public static string NormalizeShellType(string? shellType)
    {
        if (string.IsNullOrWhiteSpace(shellType))
            return GetDefaultShell();

        return shellType.Trim().ToLowerInvariant();
    }

    private static string GetDefaultShell()
    {
        if (OperatingSystem.IsWindows())
            return "cmd.exe";

        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
    }

    public static bool CheckAdminStatus()
    {
        if (OperatingSystem.IsWindows())
            return CheckWindowsAdmin();
        
        if (OperatingSystem.IsLinux())
            return CheckLinuxAdmin();
            
        return false;
    }

    private static bool CheckWindowsAdmin()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (identity == null) return false;
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckLinuxAdmin()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "id",
                Arguments = "-u",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            return output == "0";
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInPath(string executable)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
