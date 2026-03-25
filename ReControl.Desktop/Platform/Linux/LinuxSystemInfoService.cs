using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Linux;

/// <summary>
/// Linux-specific system information provider.
/// </summary>
public class LinuxSystemInfoService : ISystemInfoService
{
    public string GetPlatformName() => "Linux";

    public string GetPlatformVersion()
    {
        // Try to get a friendly distro string from /etc/os-release
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                foreach (var line in File.ReadLines("/etc/os-release"))
                {
                    if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                    {
                        var value = line["PRETTY_NAME=".Length..].Trim('"');
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
            }
        }
        catch
        {
            // Fall through to OS description
        }

        return RuntimeInformation.OSDescription;
    }

    public string GetMachineName() => Environment.MachineName;

    public string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}
