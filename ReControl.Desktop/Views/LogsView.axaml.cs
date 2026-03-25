using Avalonia.Controls;
using ReControl.Desktop.ViewModels;

namespace ReControl.Desktop.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is LogsViewModel vm)
            {
                vm.ScrollToBottomRequested += OnScrollToBottomRequested;
                // Scroll to bottom on initial load
                ScrollToBottom();
            }
        };
    }

    private void OnScrollToBottomRequested()
    {
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        var list = this.FindControl<ListBox>("LogList");
        if (list?.ItemCount > 0)
        {
            list.ScrollIntoView(list.ItemCount - 1);
        }
    }
}
