using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Dialogue de capture d'empreinte, utilisé pour l'enrôlement des voyageurs,
/// la biométrie opérateur et la connexion par empreinte (héritage :
/// EnrollFromScanner / CheckFingerAgainstUsers). Le capteur est résolu par le
/// registre d'appareils (simulateur en déploiement actuel).
/// </summary>
public partial class FingerprintScanDialog : Window
{
    private readonly IFingerprintScanner _scanner;
    private readonly short _fingerNumber;
    private readonly UserBiometricsService? _biometrics;

    /// <summary>Capture effectuée (mode capture).</summary>
    public FingerprintCapture? Capture { get; private set; }

    /// <summary>Opérateur identifié (mode connexion).</summary>
    public string? IdentifiedUserId { get; private set; }

    /// <summary>Mode capture : empreinte d'un doigt donné (voyageur ou opérateur).</summary>
    public FingerprintScanDialog(IFingerprintScanner scanner, short fingerNumber, string info)
    {
        _scanner = scanner;
        _fingerNumber = fingerNumber;
        InitializeComponent();
        InfoText.Text = info;
        DeviceText.Text = $"{Strings.Get("Bio_Device")} : {scanner.Name}" + (scanner.IsSimulated ? " (simulateur)" : string.Empty);
        SetupSimulatedSelector();
        Loaded += (_, _) => CaptureBtn.Focus();
    }

    /// <summary>Mode identification : connexion par empreinte.</summary>
    public FingerprintScanDialog(IFingerprintScanner scanner, UserBiometricsService biometrics)
    {
        _scanner = scanner;
        _fingerNumber = 1;
        _biometrics = biometrics;
        InitializeComponent();
        InfoText.Text = Strings.Get("Bio_Identify_Scan");
        DeviceText.Text = $"{Strings.Get("Bio_Device")} : {scanner.Name}" + (scanner.IsSimulated ? " (simulateur)" : string.Empty);
        SetupSimulatedSelector();
    }

    private void SetupSimulatedSelector()
    {
        if (_scanner is not ISimulatedIdentitySource source) { IdentityPanel.Visibility = Visibility.Collapsed; return; }
        var items = new List<string> { "—" };
        items.AddRange(source.Identities);
        IdentityCombo.ItemsSource = items;
        IdentityCombo.SelectedIndex = 0;
    }

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        CaptureBtn.IsEnabled = false;
        ResultText.Text = Strings.Get("Bio_Capturing");

        try
        {
            if (_scanner is ISimulatedIdentitySource source && IdentityCombo.SelectedItem is string identity && identity != "—")
                source.SelectedIdentity = identity;

            var capture = await _scanner.CaptureAsync(_fingerNumber);
            Preview.Source = ToImage(capture.Image);
            ResultText.Text = Strings.Format("Bio_Quality", capture.Quality);

            if (_biometrics is null)
            {
                Capture = capture;
                OkBtn.IsEnabled = true;
                return;
            }

            // Mode identification : match immédiat après capture.
            var userId = await _biometrics.IdentifyUserAsync(capture.Template);
            if (userId is null)
            {
                ResultText.Text = Strings.Get("Bio_Identify_Unknown");
                ErrorText.Text = Strings.Get("Bio_Identify_Unknown");
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            IdentifiedUserId = userId;
            ResultText.Text = Strings.Format("Bio_Identify_Welcome", userId);
            OkBtn.IsEnabled = true;
            OkBtn.Focus();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Échec de capture d'empreinte");
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            CaptureBtn.IsEnabled = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static BitmapImage ToImage(byte[] bytes)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
