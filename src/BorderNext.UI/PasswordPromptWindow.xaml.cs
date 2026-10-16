using System.Windows;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Saisie d'un mot de passe (création de compte, réinitialisation) — dialogue
/// autonome, sans dépendances de services.
/// </summary>
public partial class PasswordPromptWindow : Window
{
    /// <summary>Mot de passe validé (saisi deux fois à l'identique).</summary>
    public string? Password { get; private set; }

    public PasswordPromptWindow(string infoText)
    {
        InitializeComponent();
        InfoText.Text = infoText;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Password != ConfirmInput.Password)
        {
            ShowError(Strings.Get("ChangePassword_Error_Mismatch"));
            return;
        }
        if (PasswordInput.Password.Length < UserAdminService.MinimumPasswordLength)
        {
            ShowError(Strings.Get("Admin_Error_PasswordTooShort"));
            return;
        }

        Password = PasswordInput.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
