using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Interfaces;

namespace ReControl.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Settings view. Provides autostart toggle and logout functionality.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAutoStartService _autoStart;
    private readonly AuthService _auth;
    private readonly LogService _log;

    [ObservableProperty]
    private bool _isAutoStartEnabled;

    /// <summary>
    /// Raised when the user requests logout from the settings view.
    /// MainViewModel subscribes to this and handles the logout flow.
    /// </summary>
    public event Action? LogoutRequested;

    public SettingsViewModel(IAutoStartService autoStart, AuthService auth, LogService log)
    {
        _autoStart = autoStart ?? throw new ArgumentNullException(nameof(autoStart));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // Read current autostart state
        try
        {
            _isAutoStartEnabled = _autoStart.IsEnabled();
        }
        catch (Exception ex)
        {
            _log.Error("SettingsViewModel: failed to read autostart state", ex);
            _isAutoStartEnabled = false;
        }
    }

    partial void OnIsAutoStartEnabledChanged(bool value)
    {
        try
        {
            if (value)
                _autoStart.Enable();
            else
                _autoStart.Disable();

            _log.Info($"SettingsViewModel: autostart {(value ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            _log.Error("SettingsViewModel: failed to change autostart", ex);

            // Revert the property without re-triggering the changed handler
            SetProperty(ref _isAutoStartEnabled, !value, nameof(IsAutoStartEnabled));
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _log.Info("SettingsViewModel: logout requested");
        LogoutRequested?.Invoke();
    }
}
