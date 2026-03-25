using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Logs view. Displays in-memory log entries with live updates.
/// </summary>
public partial class LogsViewModel : ViewModelBase, IDisposable
{
    private readonly LogService _logService;

    public ObservableCollection<string> LogEntries { get; } = new();

    [ObservableProperty]
    private bool _hasEntries;

    /// <summary>
    /// Raised after adding a new entry so the view can scroll to bottom.
    /// </summary>
    public event Action? ScrollToBottomRequested;

    public LogsViewModel(LogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));

        // Populate from existing entries
        foreach (var entry in _logService.Snapshot())
        {
            LogEntries.Add(entry);
        }

        HasEntries = LogEntries.Count > 0;

        // Subscribe to new entries
        _logService.LogAdded += OnLogAdded;
    }

    private void OnLogAdded(string entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogEntries.Add(entry);
            HasEntries = true;
            ScrollToBottomRequested?.Invoke();
        });
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
        _logService.ClearMemory();
        HasEntries = false;
    }

    public void Dispose()
    {
        _logService.LogAdded -= OnLogAdded;
    }
}
