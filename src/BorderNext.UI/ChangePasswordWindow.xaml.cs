using System.Windows;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

public partial class ChangePasswordWindow : Window
{
    private readonly AuthService _auth;

    public string UserId { get; set; } = string.Empty;

    public ChangePasswordWindow(AuthService auth)
    {
        _auth = auth;
        InitializeComponent();
        CurrentBox.Focus();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (NewBox.Password.Length < 8)
        {
            ShowError(Strings.Get("ChangePassword_Error_TooShort"));
            return;
        }
        if (NewBox.Password != ConfirmBox.Password)
        {
            ShowError(Strings.Get("ChangePassword_Error_Mismatch"));
            return;
        }

        var ok = await _auth.ChangePasswordAsync(UserId, CurrentBox.Password, NewBox.Password);
        if (!ok)
        {
            ShowError(Strings.Get("ChangePassword_Error_Failed"));
            return;
        }

        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
