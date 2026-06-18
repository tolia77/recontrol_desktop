using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReControl.Desktop.Services;
using ReControl.Desktop.Services.Clipboard;
using ReControl.Desktop.Services.Files;
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
    private readonly AllowlistService _allowlist;
    private readonly ClipboardSettingsStore _clipboardStore;

    [ObservableProperty]
    private bool _isAutoStartEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAllowlistError))]
    private string _allowlistError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingRemoval))]
    [NotifyPropertyChangedFor(nameof(PendingRemovalPrompt))]
    private string _pendingRemovalRoot = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSharedFolders))]
    private bool _hasSharedFolders;

    [ObservableProperty]
    private bool _clipboardMaster;

    [ObservableProperty]
    private bool _clipboardAllowOutbound;

    [ObservableProperty]
    private bool _clipboardAllowInbound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboardSettingsError))]
    private string _clipboardSettingsError = string.Empty;

    public ObservableCollection<string> SharedFolders { get; } = new();
    public bool HasAllowlistError => !string.IsNullOrWhiteSpace(AllowlistError);
    public bool HasPendingRemoval => !string.IsNullOrWhiteSpace(PendingRemovalRoot);
    public bool HasNoSharedFolders => !HasSharedFolders;
    public bool HasClipboardSettingsError => !string.IsNullOrWhiteSpace(ClipboardSettingsError);
    public string PendingRemovalPrompt => HasPendingRemoval ? $"Stop sharing '{PendingRemovalRoot}'?" : string.Empty;

    /// <summary>
    /// Raised when the user requests logout from the settings view.
    /// MainViewModel subscribes to this and handles the logout flow.
    /// </summary>
    public event Action? LogoutRequested;

    public SettingsViewModel(IAutoStartService autoStart, AuthService auth, LogService log, AllowlistService allowlist, ClipboardSettingsStore clipboardStore)
    {
        _autoStart = autoStart ?? throw new ArgumentNullException(nameof(autoStart));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));
        _clipboardStore = clipboardStore ?? throw new ArgumentNullException(nameof(clipboardStore));

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

        // Read current clipboard settings (seed from store; mirror autostart try/catch).
        try
        {
            var current = _clipboardStore.Load();
            _clipboardMaster = current.Master;
            _clipboardAllowOutbound = current.AllowOutbound;
            _clipboardAllowInbound = current.AllowInbound;
        }
        catch (Exception ex)
        {
            _log.Error("SettingsViewModel: failed to read clipboard settings", ex);
            // ClipboardSettings.Defaults are all true (verified ClipboardSettings.cs:6-8); explicit fallback here.
            _clipboardMaster = true;
            _clipboardAllowOutbound = true;
            _clipboardAllowInbound = true;
        }

        RefreshSharedFolders(_allowlist.GetRoots());
        _allowlist.RootsChanged += OnAllowlistRootsChanged;
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

    partial void OnClipboardMasterChanged(bool value)
        => PersistClipboardSettings(nameof(ClipboardMaster), ref _clipboardMaster, value);

    partial void OnClipboardAllowOutboundChanged(bool value)
        => PersistClipboardSettings(nameof(ClipboardAllowOutbound), ref _clipboardAllowOutbound, value);

    partial void OnClipboardAllowInboundChanged(bool value)
        => PersistClipboardSettings(nameof(ClipboardAllowInbound), ref _clipboardAllowInbound, value);

    private void PersistClipboardSettings(string propertyName, ref bool field, bool newValue)
    {
        try
        {
            _clipboardStore.Save(new ClipboardSettings
            {
                Master = ClipboardMaster,
                AllowOutbound = ClipboardAllowOutbound,
                AllowInbound = ClipboardAllowInbound,
            });
            ClipboardSettingsError = string.Empty;
            _log.Info($"SettingsViewModel: clipboard {propertyName} = {newValue}");
        }
        catch (Exception ex)
        {
            _log.Error($"SettingsViewModel: failed to save clipboard settings ({propertyName})", ex);
            // Revert WITHOUT re-triggering the OnXChanged partial — the SetProperty third-arg pins the property name.
            SetProperty(ref field, !newValue, propertyName);
            ClipboardSettingsError = "Failed to save clipboard settings.";
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _log.Info("SettingsViewModel: logout requested");
        LogoutRequested?.Invoke();
    }

    [RelayCommand]
    private void AddRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AllowlistError = "path is invalid or inaccessible";
            return;
        }

        var result = _allowlist.AddRoot(path);
        if (!result.IsSuccess)
        {
            AllowlistError = result.Error ?? "path is invalid or inaccessible";
            return;
        }

        AllowlistError = string.Empty;
        PendingRemovalRoot = string.Empty;
        RefreshSharedFolders(_allowlist.GetRoots());
    }

    [RelayCommand]
    private void RequestRemoveRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        AllowlistError = string.Empty;
        PendingRemovalRoot = path;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmRemoveRoot))]
    private void ConfirmRemoveRoot()
    {
        if (string.IsNullOrWhiteSpace(PendingRemovalRoot)) return;
        RemoveRoot(PendingRemovalRoot);
        PendingRemovalRoot = string.Empty;
    }

    [RelayCommand]
    private void CancelRemoveRoot() => PendingRemovalRoot = string.Empty;

    [RelayCommand]
    private void RemoveRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AllowlistError = "path is invalid or inaccessible";
            return;
        }

        var result = _allowlist.RemoveRoot(path);
        if (!result.IsSuccess)
        {
            AllowlistError = result.Error ?? "path is invalid or inaccessible";
            return;
        }

        AllowlistError = string.Empty;
        RefreshSharedFolders(_allowlist.GetRoots());
    }

    public void SetAllowlistError(string message)
    {
        AllowlistError = message;
    }

    partial void OnPendingRemovalRootChanged(string value)
    {
        ConfirmRemoveRootCommand.NotifyCanExecuteChanged();
    }

    private bool CanConfirmRemoveRoot() => HasPendingRemoval;

    private void OnAllowlistRootsChanged()
    {
        var roots = _allowlist.GetRoots();
        Dispatcher.UIThread.Post(() => RefreshSharedFolders(roots));
    }

    private void RefreshSharedFolders(System.Collections.Generic.IReadOnlyList<string> roots)
    {
        SharedFolders.Clear();
        foreach (var root in roots)
            SharedFolders.Add(root);
        HasSharedFolders = SharedFolders.Count > 0;
    }
}
