using System.Windows;
using System.Windows.Input;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Recherche multicritère de documents de voyage (héritage : écran de recherche
/// du dossier frontalier). Renvoie le document sélectionné à l'appelant.
/// </summary>
public partial class DocumentSearchWindow : Window
{
    private readonly DocumentService _documents;
    private readonly GeoService _geo;

    /// <summary>Identifiant du document choisi (0 si aucun).</summary>
    public int SelectedDocumentId { get; private set; }

    public DocumentSearchWindow(DocumentService documents, GeoService geo)
    {
        _documents = documents;
        _geo = geo;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await LoadCitizenshipsAsync();
            NumberBox.Focus();
        };
    }

    private async Task LoadCitizenshipsAsync()
    {
        var countries = await _geo.GetCountriesAsync();
        var combo = new List<CitizenshipVm> { new(null, "—") };
        combo.AddRange(countries.Select(c => new CitizenshipVm(c.Id, c.Fr)));
        CitizenshipCombo.ItemsSource = combo;
        CitizenshipCombo.SelectedIndex = 0;
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        try
        {
            var items = await _documents.SearchAsync(NumberBox.Text, SurnameBox.Text, GivenNameBox.Text,
                BirthPicker.SelectedDate, (CitizenshipCombo.SelectedItem as CitizenshipVm)?.Id);

            ResultsGrid.ItemsSource = items
                .Select(d => new ResultRow(d.Id, d.DocumentNumber, d.DocumentTypeFr,
                    $"{d.Surname} {d.GivenName}".Trim(), d.DateOfBirth, d.CitizenshipFr, d.ExpireDate,
                    FormatLastMovement(d.LastMovementDate, d.LastWasEntry)))
                .ToList();
            StatusText.Text = $"{items.Count}";
        }
        catch (DomainValidationException ex)
        {
            MessageBox.Show(this, Strings.Get(ex.ErrorKey), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string FormatLastMovement(DateTime? date, bool? wasEntry)
        => date is null ? string.Empty
            : $"{date:dd/MM/yyyy} {(wasEntry == true ? "→" : "←")}";

    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Select();

    private void Select_Click(object sender, RoutedEventArgs e) => Select();

    private void Select()
    {
        if (ResultsGrid.SelectedItem is not ResultRow row) return;
        SelectedDocumentId = row.Id;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed record CitizenshipVm(int? Id, string Display);
    private sealed record ResultRow(int Id, string DocumentNumber, string DocumentTypeFr, string Holder,
        DateTime? DateOfBirth, string? CitizenshipFr, DateTime? ExpireDate, string LastMovement);
}
