using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.Platform.Windows;

/// <summary>
/// Windows-specific system information provider.
/// </summary>
public class WindowsSystemInfoService : ISystemInfoService
{
    public string GetPlatformName() => "Windows";

    public string GetPlatformVersion() => RuntimeInformation.OSDescription;

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
