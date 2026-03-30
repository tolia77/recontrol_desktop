using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReControl.Desktop.ViewModels;

namespace ReControl.Desktop.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            CopySelectedToClipboard();
            e.Handled = true;
        }
    }

    private async void CopySelected_Click(object? sender, RoutedEventArgs e)
    {
        CopySelectedToClipboard();
    }

    private async void CopySelectedToClipboard()
    {
        var list = this.FindControl<ListBox>("LogList");
        if (list?.SelectedItems == null || list.SelectedItems.Count == 0) return;

        var text = string.Join("\n", list.SelectedItems.Cast<string>());
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
