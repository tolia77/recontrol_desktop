using Avalonia.Controls;
using Avalonia.Interactivity;
using ReControl.Desktop.ViewModels;

namespace ReControl.Desktop.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wire the password TextBox text to the ViewModel, since PasswordChar
    /// TextBoxes cannot be bound via compiled bindings in Avalonia.
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var passwordBox = this.FindControl<TextBox>("PasswordBox");
        if (passwordBox != null && DataContext is LoginViewModel vm)
        {
            // Push initial value
            vm.Password = passwordBox.Text ?? string.Empty;

            passwordBox.TextChanged += (_, _) =>
            {
                vm.Password = passwordBox.Text ?? string.Empty;
            };
        }
    }
}
