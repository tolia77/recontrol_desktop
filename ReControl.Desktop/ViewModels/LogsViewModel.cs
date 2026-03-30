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

    private void OnLogAdded(string entry, bool replacing)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (replacing && LogEntries.Count > 0)
            {
                // Find and replace the last matching collapsed entry
                for (int i = LogEntries.Count - 1; i >= 0; i--)
                {
                    // Match by checking the message portion between ] and (x
                    // Both old and new entries share the same collapsible prefix
                    if (MatchesCollapsedEntry(LogEntries[i], entry))
                    {
                        LogEntries[i] = entry;
                        return;
                    }
                }
            }
            LogEntries.Add(entry);
            HasEntries = true;
            ScrollToBottomRequested?.Invoke();
        });
    }

    private static bool MatchesCollapsedEntry(string existing, string incoming)
    {
        // Extract the prefix after the level tag, e.g. "CommandDispatcher: executing 'mouse.move'"
        // Format: [timestamp] [LEVEL] message (xN)
        var existingMsg = ExtractMessagePrefix(existing);
        var incomingMsg = ExtractMessagePrefix(incoming);
        return existingMsg != null && existingMsg == incomingMsg;
    }

    private static string? ExtractMessagePrefix(string line)
    {
        // Find end of level tag "] "
        var idx = line.IndexOf("] ", line.IndexOf("] ") + 1);
        if (idx < 0) return null;
        var msg = line.Substring(idx + 2);
        // Strip trailing " (xN)" if present
        var xIdx = msg.LastIndexOf(" (x");
        if (xIdx > 0) msg = msg.Substring(0, xIdx);
        return msg;
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
