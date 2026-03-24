using System;
using Avalonia.Controls;
using ReControl.Desktop.ViewModels;

namespace ReControl.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Trigger WebSocket auto-connect when the window is loaded
        Opened += OnOpened;
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
