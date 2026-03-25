using System;
using Avalonia.Controls;
using ReControl.Desktop.ViewModels;

namespace ReControl.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _isQuitting;

    public MainWindow()
    {
        InitializeComponent();

        // Trigger WebSocket auto-connect when the window is loaded
        Opened += OnOpened;
    }

    /// <summary>
    /// Marks this window for actual closure. Without calling this,
    /// the X button will hide the window to the system tray instead.
    /// </summary>
    public void RequestQuit() => _isQuitting = true;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isQuitting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (DataContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
