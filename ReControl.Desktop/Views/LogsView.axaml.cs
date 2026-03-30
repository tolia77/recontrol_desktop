using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ReControl.Desktop.ViewModels;

namespace ReControl.Desktop.Views;

public partial class LogsView : UserControl
{
    private bool _isAtBottom = true;

    public LogsView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is LogsViewModel vm)
                vm.ScrollToBottomRequested += OnScrollToBottomRequested;
        };

        // Track scroll position to know if user is at bottom
        var list = this.FindControl<ListBox>("LogList");
        list?.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var sv = LogList.FindDescendantOfType<ScrollViewer>();
        if (sv == null) return;
        _isAtBottom = sv.Offset.Y >= sv.Extent.Height - sv.Viewport.Height - 20;
    }

    private void OnScrollToBottomRequested()
    {
        if (!_isAtBottom) return;
        var list = this.FindControl<ListBox>("LogList");
        if (list?.ItemCount > 0)
            list.ScrollIntoView(list.ItemCount - 1);
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
