using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReControl.Desktop.AppStart;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.ViewModels;

/// <summary>
/// ViewModel for the login window. Uses CommunityToolkit.Mvvm source generators.
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly LogService _log;
    private readonly AppConfig _config;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoggingIn;

    /// <summary>
    /// Raised when login succeeds so the app can transition to the main window.
    /// </summary>
    public event Action? LoginSucceeded;

    public LoginViewModel(AuthService authService, LogService log, AppConfig config)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter email and password.";
            return;
        }

        IsLoggingIn = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _authService.LoginAsync(Email, Password);
            if (result.Success)
            {
                _log.Info("Login succeeded, transitioning to main window");
                LoginSucceeded?.Invoke();
            }
            else
            {
                ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Invalid email or password."
                    : result.ErrorMessage;
            }
        }
        catch (HttpRequestException ex)
        {
            _log.Error("Login request failed", ex);
            ErrorMessage = "Connection error. Please check your network.";
        }
        catch (Exception ex)
        {
            _log.Error("Unexpected login error", ex);
            ErrorMessage = "An unexpected error occurred.";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private void OpenSignUp()
    {
        var frontendUrl = _config.FrontendUrl
                          ?? (string.IsNullOrWhiteSpace(_config.ApiBaseUrl) ? null : _config.ApiBaseUrl)
                          ?? "http://localhost:5175";

        var signUpUrl = frontendUrl.TrimEnd('/') + "/register";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = signUpUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to open sign-up URL: {ex.Message}");
        }
    }
}
