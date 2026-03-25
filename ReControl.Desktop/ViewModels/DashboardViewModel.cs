using System;
using System.Threading;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Dashboard view. Displays connection status badge,
/// device identity (name, OS, IP), and session info (user email, uptime).
/// </summary>
public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly ISystemInfoService _systemInfo;
    private readonly AuthService _auth;
    private readonly LogService _log;

    private DateTime? _connectedSince;
    private Timer? _uptimeTimer;

    [ObservableProperty]
    private IBrush _statusBrush = new SolidColorBrush(Color.Parse("#EF4444"));

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private string _osInfo = string.Empty;

    [ObservableProperty]
    private string _localIp = string.Empty;

    [ObservableProperty]
    private string _userEmail = string.Empty;

    [ObservableProperty]
    private string _uptimeText = "0m";

    public DashboardViewModel(ISystemInfoService systemInfo, AuthService auth, LogService log)
    {
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // Initialize device identity
        DeviceName = _systemInfo.GetMachineName();
        OsInfo = $"{_systemInfo.GetPlatformName()} {_systemInfo.GetPlatformVersion()}";
        LocalIp = _systemInfo.GetLocalIpAddress();

        // Initialize user email from auth (may be null if restored from tokens)
        var email = _auth.GetUserEmail();
        UserEmail = !string.IsNullOrWhiteSpace(email) ? email : "Session active";
    }

    /// <summary>
    /// Updates the connection status display. Called by MainViewModel
    /// when WebSocket connection state changes.
    /// </summary>
    public void UpdateConnectionStatus(bool connected, bool connecting)
    {
        if (connected)
        {
            StatusBrush = new SolidColorBrush(Color.Parse("#22C55E"));
            ConnectionStatusText = "Connected";
            StartUptimeTimer();
        }
        else if (connecting)
        {
            StatusBrush = new SolidColorBrush(Color.Parse("#EAB308"));
            ConnectionStatusText = "Connecting...";
        }
        else
        {
            StatusBrush = new SolidColorBrush(Color.Parse("#EF4444"));
            ConnectionStatusText = "Disconnected";
            StopUptimeTimer();
        }
    }

    private void StartUptimeTimer()
    {
        if (_connectedSince != null) return; // already running

        _connectedSince = DateTime.UtcNow;
        UpdateUptimeDisplay();

        _uptimeTimer = new Timer(
            _ => Dispatcher.UIThread.Post(UpdateUptimeDisplay),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    private void StopUptimeTimer()
    {
        _uptimeTimer?.Dispose();
        _uptimeTimer = null;
        _connectedSince = null;
        UptimeText = "0m";
    }

    private void UpdateUptimeDisplay()
    {
        if (_connectedSince == null)
        {
            UptimeText = "0m";
            return;
        }

        var elapsed = DateTime.UtcNow - _connectedSince.Value;
        UptimeText = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
            : $"{Math.Max(1, elapsed.Minutes)}m";
    }

    public void Dispose()
    {
        _uptimeTimer?.Dispose();
        _uptimeTimer = null;
    }
}
