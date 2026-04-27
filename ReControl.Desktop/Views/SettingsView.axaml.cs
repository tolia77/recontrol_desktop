using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReControl.Desktop.ViewModels;

namespace ReControl.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void AddSharedFolder_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as SettingsViewModel;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            vm?.SetAllowlistError("folder picker is unavailable on this platform");
            return;
        }

        if (!storageProvider.CanPickFolder)
        {
            vm?.SetAllowlistError("folder picker is unavailable on this platform");
            return;
        }

        var picked = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to share",
            AllowMultiple = false
        });

        if (picked.Count == 0) return;

        var uri = picked[0].Path;
        var localPath = uri.IsAbsoluteUri ? uri.LocalPath : uri.ToString();
        vm?.AddRootCommand.Execute(localPath);
    }
}
