using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Dashboard view. Displays connection status,
/// device identity, and session information.
/// Stub for Task 1 -- full implementation in Task 2.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
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

    private readonly ISystemInfoService _systemInfo;
    private readonly AuthService _auth;
    private readonly LogService _log;

    public DashboardViewModel(ISystemInfoService systemInfo, AuthService auth, LogService log)
    {
        _systemInfo = systemInfo;
        _auth = auth;
        _log = log;

        DeviceName = _systemInfo.GetMachineName();
        OsInfo = $"{_systemInfo.GetPlatformName()} {_systemInfo.GetPlatformVersion()}";
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
        }
    }
}
