using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Écran des dix empreintes d'une personne (héritage : FingerPrintsForm,
/// FingerRecordList, FingerSelector) : capture par appareil, affichage,
/// suppression, état par doigt.
/// </summary>
public partial class FingerprintsWindow : Window
{
    private readonly int _personId;
    private readonly BiometricService _biometrics;
    private readonly IFingerprintScanner _scanner;
    private IReadOnlyList<FingerRow> _rows = Array.Empty<FingerRow>();

    public FingerprintsWindow(int personId, string personDisplay, BiometricService biometrics, IFingerprintScanner scanner)
    {
        _personId = personId;
        _biometrics = biometrics;
        _scanner = scanner;
        InitializeComponent();
        PersonText.Text = Strings.Format("Bio_ForPerson", personDisplay);
        DeviceText.Text = $"{Strings.Get("Bio_Device")} : {scanner.Name}" +
                          (scanner.IsSimulated ? " (simulateur)" : string.Empty);
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var status = await _biometrics.GetStatusAsync(_personId);
            _rows = status.Select(s => new FingerRow(s.FingerNumber,
                Strings.Get($"Bio_Finger_{s.FingerNumber}"), s.Captured, s.Quality, s.CaptureDate, s.Device)).ToList();
            var selected = (FingersGrid.SelectedItem as FingerRow)?.FingerNumber ?? 1;
            FingersGrid.ItemsSource = _rows;
            FingersGrid.SelectedItem = _rows.FirstOrDefault(r => r.FingerNumber == selected);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void Finger_SelectionChanged(object sender, SelectionChangedEventArgs e) => await ShowSelectedImageAsync();

    private async Task ShowSelectedImageAsync()
    {
        if (FingersGrid.SelectedItem is not FingerRow row) return;
        if (!row.Captured) { Preview.Source = null; StatusText.Text = Strings.Get("Bio_NoPrint"); return; }
        var image = await _biometrics.GetImageAsync(_personId, row.FingerNumber);
        Preview.Source = image is { Length: > 0 } ? ToImage(image) : null;
        StatusText.Text = string.Empty;
    }

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        // Sans sélection explicite, la capture vise le premier doigt (usage terrain).
        if (FingersGrid.SelectedItem is not FingerRow row)
        {
            if (_rows.Count == 0) return;
            row = _rows[0];
            FingersGrid.SelectedItem = row;
        }
        try
        {
            var dialog = new FingerprintScanDialog(_scanner, row.FingerNumber,
                $"{Strings.Get("Bio_Col_Finger")} : {row.Display}") { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Capture is null) return;

            await _biometrics.StoreAsync(_personId, dialog.Capture);
            StatusText.Text = Strings.Format("Bio_Enrolled", dialog.Capture.Quality);
            await ReloadAsync();
            await ShowSelectedImageAsync();
        }
        catch (DomainValidationException ex)
        {
            MessageBox.Show(this, Strings.Get(ex.ErrorKey), Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void Show_Click(object sender, RoutedEventArgs e) => await ShowSelectedImageAsync();

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (FingersGrid.SelectedItem is not FingerRow row || !row.Captured) return;
        try
        {
            await _biometrics.DeleteAsync(_personId, row.FingerNumber);
            Preview.Source = null;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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

    private static void ShowError(Exception ex)
    {
        Serilog.Log.Error(ex, "Erreur de l'écran empreintes");
        MessageBox.Show(ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record FingerRow(short FingerNumber, string Display, bool Captured, int? Quality,
        DateTime? CaptureDate, string? Device);
}
