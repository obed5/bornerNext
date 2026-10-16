using System.Windows;
using System.Windows.Controls;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;
using Microsoft.Extensions.DependencyInjection;

namespace BorderNext.UI;

public partial class LoginWindow : Window
{
    private readonly AuthService _auth;
    private readonly SessionScopes _scopes;
    private readonly ICurrentUser _currentUser;
    private readonly UserBiometricsService _userBiometrics;
    private readonly IFingerprintScanner _fingerprintScanner;

    public LoginWindow(AuthService auth, SessionScopes scopes, ICurrentUser currentUser,
        UserBiometricsService userBiometrics, IFingerprintScanner fingerprintScanner)
    {
        _auth = auth;
        _scopes = scopes;
        _currentUser = currentUser;
        _userBiometrics = userBiometrics;
        _fingerprintScanner = fingerprintScanner;
        InitializeComponent();
        UserNameBox.Focus();
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string culture)
        {
            App.SetCulture(culture);
            // Recharge les libellés de cette fenêtre avec la nouvelle culture.
            Title = Strings.Login_Title;
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var result = await _auth.LoginAsync(UserNameBox.Text, PasswordBox.Password);
        if (!result.Success)
        {
            ErrorText.Text = Strings.Get(result.ErrorCode ?? "Login_Error_InvalidCredentials");
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Clear();
            PasswordBox.Focus();
            return;
        }

        await CompleteLoginAsync(result);
    }

    /// <summary>
    /// Connexion par empreinte (héritage : UserLoginForm.CheckFingerAgainstUsers) :
    /// capture simulée, identification de l'opérateur, puis mêmes contrôles que
    /// la connexion par mot de passe (compte actif, changement obligatoire).
    /// </summary>
    private async void Biometrics_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var dialog = new FingerprintScanDialog(_fingerprintScanner, _userBiometrics) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.IdentifiedUserId is null) return;

        var result = await _auth.LoginWithBiometricsAsync(dialog.IdentifiedUserId);
        if (!result.Success)
        {
            ErrorText.Text = Strings.Get(result.ErrorCode ?? "Login_Error_InvalidCredentials");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        await CompleteLoginAsync(result);
    }

    /// <summary>Suite commune de connexion : poste, changement obligatoire, shell.</summary>
    private async Task CompleteLoginAsync(LoginResult result)
    {
        _currentUser.SetPcNumber(Environment.GetEnvironmentVariable("BORDERNEXT_PC") ?? Environment.MachineName);

        // Politique : changement obligatoire du mot de passe à la première connexion.
        if (result.User!.MustChangePassword)
        {
            var change = _scopes.Current.GetRequiredService<ChangePasswordWindow>();
            change.Owner = this;
            change.UserId = result.User.Id;
            if (change.ShowDialog() != true)
            {
                await _auth.LogoutAsync();
                PasswordBox.Clear();
                return;
            }
        }

        // Même session (même portée de services) pour l'écran principal.
        var main = _scopes.Current.GetRequiredService<MainWindow>();
        System.Windows.Application.Current.MainWindow = main;
        main.Show();
        Close();
    }
}
