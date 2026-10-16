using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Reporting;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;
using Microsoft.Win32;

namespace BorderNext.UI;

/// <summary>
/// Éditions du poste (héritage : formulaire d'édition) : catalogue des rapports par
/// groupe, critères (période, poste frontière, sens, nationalité, type de document,
/// vol, inspecteur, personne), aperçu des tableaux, synthèse et export PDF ou
/// impression via le moteur d'édition.
/// </summary>
public partial class ReportsWindow : Window
{
    private readonly ReportService _reports;
    private readonly IReportWriter _writer;
    private readonly GeoService _geo;
    private readonly ReferenceDataService _referenceData;
    private readonly SettingsService _settings;

    private ReportDefinition? _selected;
    private ReportTemplate? _preview;

    private string? _organisation;
    private string? _footer;

    public ReportsWindow(ReportService reports, IReportWriter writer, GeoService geo,
        ReferenceDataService referenceData, SettingsService settings)
    {
        _reports = reports;
        _writer = writer;
        _geo = geo;
        _referenceData = referenceData;
        _settings = settings;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    private async Task LoadAllAsync() => await GuardAsync(async () =>
    {
        // Catalogue par groupes.
        var groups = new List<TreeViewItem>();
        foreach (var groupKey in ReportCatalog.Groups)
        {
            var group = new TreeViewItem { Header = Strings.Get(groupKey), IsExpanded = true };
            foreach (var definition in ReportCatalog.All.Where(d => d.GroupKey == groupKey))
                group.Items.Add(new TreeViewItem { Header = Strings.Get(definition.NameKey), Tag = definition });
            groups.Add(group);
        }
        CatalogTree.ItemsSource = groups;

        // Critères.
        var borderPoints = await _geo.GetBorderPointsAsync();
        var borderPointItems = new List<object> { new BorderPointVm(null, "—") };
        borderPointItems.AddRange(borderPoints.Select(b => new BorderPointVm(b.Id, b.Fr)));
        BorderPointCombo.ItemsSource = borderPointItems;
        BorderPointCombo.SelectedIndex = 0;

        DirectionCombo.ItemsSource = new List<IdVm>
        {
            new(null, "—"),
            new(0, Strings.Get("PAL_Direction_Entry")),
            new(1, Strings.Get("PAL_Direction_Exit")),
        };
        DirectionCombo.SelectedIndex = 0;

        var countries = await _geo.GetCountriesAsync();
        var countryItems = new List<object> { new IdVm(null, "—") };
        countryItems.AddRange(countries.Select(c => new IdVm(c.Id, string.IsNullOrEmpty(c.Fr) ? c.En : c.Fr)));
        CountryCombo.ItemsSource = countryItems;
        CountryCombo.SelectedIndex = 0;

        var documentTypes = await _referenceData.GetItemsAsync("DOCUMENT_TYPE");
        var documentTypeItems = new List<object> { new IdVm(null, "—") };
        documentTypeItems.AddRange(documentTypes.Select(i => new IdVm(i.Id, i.Fr)));
        DocumentTypeCombo.ItemsSource = documentTypeItems;
        DocumentTypeCombo.SelectedIndex = 0;

        var visaTypes = await _referenceData.GetItemsAsync("VISA_TYPE");
        var visaTypeItems = new List<object> { new IdVm(null, "—") };
        visaTypeItems.AddRange(visaTypes.Select(i => new IdVm(i.Id, i.Fr)));
        VisaTypeCombo.ItemsSource = visaTypeItems;
        VisaTypeCombo.SelectedIndex = 0;

        // Options d'impression (héritage : SettingsCustomReportPrintOptions).
        _organisation = await _settings.GetStringAsync("ReportPrint_Organisation");
        _footer = await _settings.GetStringAsync("ReportPrint_Footer");

        var today = DateTime.Today;
        FromPicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
        ToPicker.SelectedDate = today;

        StatusText.Text = Strings.Get("RP_Status_Select");
    });

    // -----------------------------------------------------------------------
    // Catalogue et critères
    // -----------------------------------------------------------------------

    private void Catalog_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selected = (CatalogTree.SelectedItem as TreeViewItem)?.Tag as ReportDefinition;
        if (_selected is null) return;

        // Affichage des seuls critères exploités par l'édition choisie.
        var fields = _selected.Fields;
        PeriodPanel.Visibility = Show(fields, ReportFields.Period);
        BorderPointPanel.Visibility = Show(fields, ReportFields.BorderPoint);
        DirectionPanel.Visibility = Show(fields, ReportFields.Direction);
        CountryPanel.Visibility = Show(fields, ReportFields.Country);
        DocumentTypePanel.Visibility = Show(fields, ReportFields.DocumentType);
        VisaTypePanel.Visibility = Show(fields, ReportFields.VisaType);
        FlightPanel.Visibility = Show(fields, ReportFields.Flight);
        InspectorPanel.Visibility = Show(fields, ReportFields.Inspector);
        PersonPanel.Visibility = Show(fields, ReportFields.Person);
        ChildrenCheck.Visibility = Show(fields, ReportFields.Children);

        ReportTitleText.Text = Strings.Get(_selected.NameKey);
        ReportFiltersText.Text = string.Empty;
        PreviewTabs.Items.Clear();
        SummaryList.ItemsSource = null;
        _preview = null;
        StatusText.Text = Strings.Get("RP_Status_Criteria");
    }

    private static Visibility Show(ReportFields fields, ReportFields flag)
        => fields.HasFlag(flag) ? Visibility.Visible : Visibility.Collapsed;

    private ReportParameters BuildParameters()
    {
        var borderPoint = BorderPointCombo.SelectedItem as BorderPointVm;
        var direction = (DirectionCombo.SelectedItem as IdVm)?.Id switch
        {
            0 => MovementService.EntryCode,
            1 => MovementService.ExitCode,
            _ => null,
        };

        return new ReportParameters(
            From: FromPicker.SelectedDate,
            To: ToPicker.SelectedDate,
            BorderPointId: borderPoint?.Id,
            BorderPointName: borderPoint?.Id is null ? null : borderPoint.Display,
            DirectionCode: direction,
            CountryId: (CountryCombo.SelectedItem as IdVm)?.Id,
            DocumentTypeId: (DocumentTypeCombo.SelectedItem as IdVm)?.Id,
            VisaTypeId: (VisaTypeCombo.SelectedItem as IdVm)?.Id,
            FlightNumber: FlightBox.Text,
            Inspector: InspectorBox.Text,
            DocumentNumber: DocumentNumberBox.Text,
            Surname: SurnameBox.Text,
            GivenName: GivenNameBox.Text,
            IncludeChildren: ChildrenCheck.IsChecked == true);
    }

    // -----------------------------------------------------------------------
    // Aperçu, export et impression
    // -----------------------------------------------------------------------

    private LocalizedReport ToLocalized(ReportTemplate template)
    {
        // Les éditions d'analyse sont paysage (largeur des tableaux), les fiches portrait.
        var landscape = _selected?.GroupKey != ReportCatalog.GroupDocuments;
        return ReportLocalizer.Localize(template, Strings.Get, _organisation, _footer, landscape);
    }

    private async void Preview_Click(object sender, RoutedEventArgs e) => await GuardAsync(RefreshPreviewAsync);

    private async Task RefreshPreviewAsync()
    {
        if (_selected is null)
        {
            StatusText.Text = Strings.Get("RP_Status_Select");
            return;
        }

        var template = await _reports.BuildAsync(_selected.Id, BuildParameters());
        _preview = template;

        var localized = ToLocalized(template);
        ReportTitleText.Text = localized.Title;
        ReportFiltersText.Text = string.Join("  ·  ", localized.FilterLines);
        SummaryList.ItemsSource = localized.SummaryLines.Select(line => "• " + line).ToList();

        PreviewTabs.Items.Clear();
        foreach (var table in localized.Tables)
        {
            // Aperçu des tableaux via DataTable (colonnes dynamiques titrées en français).
            var dataTable = new System.Data.DataTable();
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var header = table.Columns[i].Header;
                if (string.IsNullOrWhiteSpace(header)) header = $"Col{i + 1}";
                var name = header;
                var suffix = 2;
                while (dataTable.Columns.Contains(name)) name = $"{header} ({suffix++})";
                dataTable.Columns.Add(name);
            }

            foreach (var row in table.Rows)
                dataTable.Rows.Add(table.Columns.Select((_, i) => i < row.Length ? (object?)row[i] : null).ToArray());

            var grid = new DataGrid
            {
                AutoGenerateColumns = true,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false,
                ItemsSource = dataTable.DefaultView,
            };

            var tab = new TabItem
            {
                Header = table.Title ?? localized.Title,
                Content = grid,
            };
            PreviewTabs.Items.Add(tab);
        }

        // Sans sélection explicite le premier onglet ne matérialise pas son contenu :
        // la grille resterait absente de l'aperçu.
        if (PreviewTabs.Items.Count > 0) PreviewTabs.SelectedIndex = 0;

        var recordCount = localized.Tables.Sum(t => t.Rows.Count);
        RecordCountText.Text = Strings.Format("RP_RecordsFound", recordCount);
        StatusText.Text = Strings.Get("RP_Status_Previewed");
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var report = await EnsureRecentPreviewAsync();
        if (report is null) return;

        var dialog = new SaveFileDialog
        {
            Title = Strings.Get("RP_ExportPdf"),
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{_selected!.Id}_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
        };
        if (dialog.ShowDialog(this) != true) return;

        var bytes = _writer.RenderPdf(report);
        await File.WriteAllBytesAsync(dialog.FileName, bytes);
        StatusText.Text = Strings.Format("RP_ExportDone", dialog.FileName);
        MessageBox.Show(this, Strings.Format("RP_ExportDone", dialog.FileName), Strings.Get("RP_Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    });

    private async void Print_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var report = await EnsureRecentPreviewAsync();
        if (report is null) return;

        // Impression via le lecteur PDF par défaut (l'édition est toujours produite en PDF).
        var path = Path.Combine(Path.GetTempPath(), $"BorderNext_{_selected!.Id}_{DateTime.Now:HHmmss}.pdf");
        await File.WriteAllBytesAsync(path, _writer.RenderPdf(report));
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        StatusText.Text = Strings.Format("RP_PrintStarted", path);
    });

    private async Task<LocalizedReport?> EnsureRecentPreviewAsync()
    {
        if (_selected is null)
        {
            MessageBox.Show(this, Strings.Get("RP_Status_Select"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        // L'aperçu est recalculé si les critères ont changé depuis le dernier aperçu.
        var current = await _reports.BuildAsync(_selected.Id, BuildParameters());
        _preview = current;
        return ToLocalized(current);
    }

    // -----------------------------------------------------------------------
    // Infrastructure d'écran
    // -----------------------------------------------------------------------

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (DomainValidationException ex)
        {
            MessageBox.Show(this, Strings.Get(ex.ErrorKey), Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed record IdVm(int? Id, string Display) { public override string ToString() => Display; }

    private sealed record BorderPointVm(short? Id, string Display) { public override string ToString() => Display; }
}
