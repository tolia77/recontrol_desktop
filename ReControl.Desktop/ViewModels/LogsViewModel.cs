using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReControl.Desktop.Services;

namespace ReControl.Desktop.ViewModels;

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

        foreach (var entry in _logService.Snapshot())
            LogEntries.Add(entry);

        HasEntries = LogEntries.Count > 0;
        _logService.LogAdded += OnLogAdded;
    }

    private void OnLogAdded(string entry, bool replacing)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (replacing && LogEntries.Count > 0)
            {
                LogEntries[LogEntries.Count - 1] = entry;
            }
            else
            {
                LogEntries.Add(entry);
                HasEntries = true;
                ScrollToBottomRequested?.Invoke();
            }
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
