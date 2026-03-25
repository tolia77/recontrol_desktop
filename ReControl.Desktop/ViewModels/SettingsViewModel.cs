using System;

namespace ReControl.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Settings view. Placeholder skeleton -- full implementation
/// with autostart toggle, logout button, and configuration comes in Plan 03.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    /// <summary>
    /// Raised when the user requests logout from the settings view.
    /// MainViewModel subscribes to this and handles the logout flow.
    /// </summary>
    public event Action? LogoutRequested;

    /// <summary>
    /// Triggers logout. Called by the logout button (Plan 03).
    /// </summary>
    public void RequestLogout() => LogoutRequested?.Invoke();
}
