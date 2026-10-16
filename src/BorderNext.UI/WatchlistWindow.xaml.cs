using System.Windows;
using System.Windows.Controls;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Listes de surveillance et personnes douteuses (héritage : PersonsStopListForm,
/// DocumentStopListForm, DoubtablePersonManagement, AlertListSimulatorForm,
/// ExternalAlertListMatchForm) : gestion des entrées personnes/documents, empreintes
/// de liste d'alerte, file de la seconde inspection avec décision du superviseur,
/// simulateur de correspondance par type et contrôle inverse contre le registre.
/// </summary>
public partial class WatchlistWindow : Window
{
    private readonly WatchlistService _watchlists;
    private readonly StopListMatcher _matcher;
    private readonly DoubtablePersonService _doubts;
    private readonly ReferenceDataService _referenceData;
    private readonly GeoService _geo;
    private readonly IFingerprintScanner _scanner;

    private bool _loadingAutoCheck;
    private PersonStopList? _person;
    private DocumentStopList? _document;
    private DoubtablePersonListItem? _doubt;

    private List<IdVm> _sources = new();
    private List<IdVm> _actions = new();
    private List<IdVm> _matchActions = new();
    private List<IdVm> _reasons = new();
    private List<IdVm> _documentTypes = new();
    private List<IdVm> _sexes = new();
    private List<IdVm> _justifications = new();
    private List<CountryVm> _countries = new();

    public WatchlistWindow(WatchlistService watchlists, StopListMatcher matcher, DoubtablePersonService doubts,
        ReferenceDataService referenceData, GeoService geo, IFingerprintScanner scanner)
    {
        _watchlists = watchlists;
        _matcher = matcher;
        _doubts = doubts;
        _referenceData = referenceData;
        _geo = geo;
        _scanner = scanner;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        try
        {
            _sources = ToIds(await _referenceData.GetItemsAsync("STOP_LIST_SOURCE"));
            _matchActions = ToIds(await _referenceData.GetItemsAsync("MATCH_STOPLIST_ACTION"));
            _reasons = ToIds(await _referenceData.GetItemsAsync("CHECKING_REASON"));
            _documentTypes = ToIds(await _referenceData.GetItemsAsync("DOCUMENT_TYPE"));
            _sexes = ToIds(await _referenceData.GetItemsAsync("SEX"));
            _justifications = ToIds(await _referenceData.GetItemsAsync("REFUSAL_JUSTIFICATION"));

            _actions = (await _referenceData.GetItemsAsync("ACTION"))
                .Select(i => new IdVm(i.Id, $"{i.Code} — {i.Fr}")).ToList();

            var countries = await _geo.GetCountriesAsync();
            _countries = countries.Select(c => new CountryVm(c.Id, string.IsNullOrEmpty(c.Fr) ? c.En : c.Fr)).ToList();

            var sourceItems = WithEmpty(_sources);
            var matchItems = WithEmpty(_matchActions);
            var reasonItems = WithEmpty(_reasons);
            var documentTypeItems = WithEmpty(_documentTypes);
            var sexItems = WithEmpty(_sexes);
            var actionChoices = WithEmpty(_actions);
            var justificationChoices = WithEmpty(_justifications);
            var countryItems = new List<CountryVm> { new(null, "—") };
            countryItems.AddRange(_countries);

            PersonSearchSource.ItemsSource = WithEmpty(_sources);
            PersonSourceCombo.ItemsSource = sourceItems;
            PersonActionCombo.ItemsSource = matchItems;
            PersonDocumentTypeCombo.ItemsSource = documentTypeItems;
            PersonSexCombo.ItemsSource = sexItems;
            PersonCitizenshipCombo.ItemsSource = countryItems;

            DocSearchCountry.ItemsSource = WithEmpty(_countries);
            DocCountryEdit.ItemsSource = countryItems;
            DocSourceCombo.ItemsSource = WithEmpty(_sources);
            DocActionCombo.ItemsSource = WithEmpty(_matchActions);
            DocTypeEdit.ItemsSource = documentTypeItems;

            DoubtReasonCombo.ItemsSource = reasonItems;
            DoubtActionCombo.ItemsSource = actionChoices;
            DoubtJustificationCombo.ItemsSource = justificationChoices;
            DoubtStatusCombo.ItemsSource = new List<StatusVm>
            {
                new(null, Strings.Get("WL_Status_All")),
                new(DoubtfulWorkflowStatus.Pending, Strings.Get("WL_Status_Pending")),
                new(DoubtfulWorkflowStatus.Processed, Strings.Get("WL_Status_Processed")),
                new(DoubtfulWorkflowStatus.Cancelled, Strings.Get("WL_Status_Cancelled"))
            };
            DoubtStatusCombo.SelectedIndex = 0;

            SimCitizenship.ItemsSource = countryItems;
            SimKindCombo.ItemsSource = new List<IdVm>
            {
                new(1, Strings.Get("WL_Tab_Persons")),
                new(2, Strings.Get("WL_Tab_Documents"))
            };
            SimKindCombo.SelectedIndex = 0;
            SimTypeCombo.ItemsSource = Enumerable.Range(1, 8)
                .Select(i => new IdVm(i, Strings.Get($"WL_SimType_{i}"))).ToList();
            SimTypeCombo.SelectedIndex = 0;

            _loadingAutoCheck = true;
            AutoCheckBox.IsChecked = await _doubts.IsAutoCheckEnabledAsync();
            _loadingAutoCheck = false;

            await LoadPersonsAsync();
            await LoadDocumentsAsync();
            await LoadDoubtsAsync();
            await RefreshCounterAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ---- Personnes -----------------------------------------------------------

    private async void SearchPersons_Click(object sender, RoutedEventArgs e) => await LoadPersonsAsync();

    private async Task LoadPersonsAsync()
    {
        var previousId = _person?.Id;
        var items = await _watchlists.SearchPersonsAsync(new PersonStopListSearch(
            Surname: PersonSearchSurname.Text,
            GivenName: PersonSearchGivenName.Text,
            DocumentNumber: PersonSearchDocument.Text,
            StopListSourceId: (PersonSearchSource.SelectedItem as IdVm)?.Id,
            ShowCancelled: PersonShowCancelled.IsChecked == true));
        PersonsGrid.ItemsSource = items;
        if (previousId is { } id) PersonsGrid.SelectedItem = items.FirstOrDefault(p => p.Id == id);
    }

    private void Person_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PersonsGrid.SelectedItem is not PersonStopList person) return;
        _person = person;
        PersonSurnameEdit.Text = person.Surname ?? string.Empty;
        PersonGivenNameEdit.Text = person.GivenName ?? string.Empty;
        PersonFathersNameEdit.Text = person.FathersName ?? string.Empty;
        PersonSurnameAltEdit.Text = person.SurnameAlt ?? string.Empty;
        PersonGivenNameAltEdit.Text = person.GivenNameAlt ?? string.Empty;
        PersonBirthDateEdit.SelectedDate = person.DateOfBirth;
        PersonDocumentNumberEdit.Text = person.DocumentNumber ?? string.Empty;
        PersonPersonalNumberEdit.Text = person.PersonalNumber ?? string.Empty;
        PersonSourceDocumentEdit.Text = person.SourceDocument ?? string.Empty;
        PersonActionRequiredEdit.Text = person.ActionRequired ?? string.Empty;
        PersonInfoEdit.Text = person.Info ?? string.Empty;
        SelectId(PersonSexCombo, person.SexId);
        SelectId(PersonDocumentTypeCombo, person.DocumentTypeId);
        SelectId(PersonSourceCombo, person.StopListSourceId);
        SelectId(PersonActionCombo, person.MatchInStopListActionId);
        SelectCountry(PersonCitizenshipCombo, person.CitizenshipId);
        AuthorizePersonBtn.Content = Strings.Get(person.IsAuthorized == true ? "WL_WithdrawAuthorized" : "WL_Authorize");
        _ = RefreshPrintsAsync(person.Id);
    }

    private void NewPerson_Click(object sender, RoutedEventArgs e)
    {
        _person = null;
        PersonsGrid.SelectedItem = null;
        PersonSurnameEdit.Text = PersonGivenNameEdit.Text = PersonFathersNameEdit.Text = string.Empty;
        PersonSurnameAltEdit.Text = PersonGivenNameAltEdit.Text = string.Empty;
        PersonBirthDateEdit.SelectedDate = null;
        PersonDocumentNumberEdit.Text = PersonPersonalNumberEdit.Text = string.Empty;
        PersonSourceDocumentEdit.Text = PersonActionRequiredEdit.Text = PersonInfoEdit.Text = string.Empty;
        PersonSexCombo.SelectedIndex = 0;
        PersonDocumentTypeCombo.SelectedIndex = 0;
        PersonSourceCombo.SelectedIndex = 0;
        PersonActionCombo.SelectedIndex = 0;
        PersonCitizenshipCombo.SelectedIndex = 0;
        PersonPrintsText.Text = string.Empty;
        StatusText.Text = Strings.Get("WL_NewEntryHint");
    }

    private async void SavePerson_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var request = new PersonStopListRequest(
            Surname: PersonSurnameEdit.Text,
            GivenName: PersonGivenNameEdit.Text,
            FathersName: PersonFathersNameEdit.Text,
            SurnameAlt: PersonSurnameAltEdit.Text,
            GivenNameAlt: PersonGivenNameAltEdit.Text,
            DateOfBirth: PersonBirthDateEdit.SelectedDate,
            SexId: (PersonSexCombo.SelectedItem as IdVm)?.Id,
            CitizenshipId: (PersonCitizenshipCombo.SelectedItem as CountryVm)?.Id,
            StopListSourceId: (PersonSourceCombo.SelectedItem as IdVm)?.Id,
            SourceDocument: PersonSourceDocumentEdit.Text,
            Info: PersonInfoEdit.Text,
            DocumentTypeId: (PersonDocumentTypeCombo.SelectedItem as IdVm)?.Id,
            DocumentNumber: PersonDocumentNumberEdit.Text,
            PersonalNumber: PersonPersonalNumberEdit.Text,
            ActionRequired: PersonActionRequiredEdit.Text,
            MatchInStopListActionId: (PersonActionCombo.SelectedItem as IdVm)?.Id);

        if (_person is null)
        {
            var id = await _watchlists.CreatePersonAsync(request);
            StatusText.Text = Strings.Format("WL_PersonSaved", id);
        }
        else
        {
            await _watchlists.UpdatePersonAsync(_person.Id, request);
            StatusText.Text = Strings.Format("WL_PersonUpdated", _person.Id);
        }
        await LoadPersonsAsync();
    });

    private async void CancelPerson_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_person is null) return;
        var cancelDocument = TextInputDialog.Ask(this, Strings.Get("WL_CancelAlert"), Strings.Get("WL_CancelDocument"), null);
        if (cancelDocument is null) return;

        await _watchlists.CancelPersonAsync(_person.Id, (PersonSourceCombo.SelectedItem as IdVm)?.Id, cancelDocument);
        StatusText.Text = Strings.Get("WL_AlertCancelled");
        await LoadPersonsAsync();
    });

    private async void ToggleAuthorized_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_person is null) return;
        var target = _person.IsAuthorized != true;
        await _watchlists.SetPersonAuthorizedAsync(_person.Id, target);
        StatusText.Text = Strings.Get(target ? "WL_Authorized" : "WL_AuthorizationWithdrawn");
        await LoadPersonsAsync();
    });

    private async void CaptureFingerprint_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_person is null) return;
        var fingerValue = TextInputDialog.Ask(this, Strings.Get("WL_CaptureFingerprint"), Strings.Get("WL_FingerNumber"), "1");
        if (fingerValue is null) return;
        if (!short.TryParse(fingerValue, out var fingerNumber) || fingerNumber is < 1 or > 10)
        {
            StatusText.Text = Strings.Get("WL_Error_FingerNumberRange");
            return;
        }

        var scan = new FingerprintScanDialog(_scanner, fingerNumber, Strings.Get("WL_CaptureFingerprint"))
        {
            Owner = this
        };
        if (scan.ShowDialog() != true || scan.Capture is not { } capture) return;

        await _watchlists.AddFingerprintAsync(_person.Id, fingerNumber, capture.Image, capture.Template);
        StatusText.Text = Strings.Format("WL_FingerprintSaved", fingerNumber);
        await RefreshPrintsAsync(_person.Id);
    });

    private async void IdentifyFingerprint_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var scan = new FingerprintScanDialog(_scanner, 1, Strings.Get("WL_IdentifyFingerprint"))
        {
            Owner = this
        };
        if (scan.ShowDialog() != true || scan.Capture is not { } capture) return;

        var matches = await _watchlists.IdentifyFingerprintAsync(capture.Template);
        if (matches.Count == 0)
        {
            StatusText.Text = Strings.Get("WL_Identify_NoMatch");
            return;
        }

        var person = await _watchlists.GetPersonAsync(matches[0].PersonStopListId);
        if (person != null)
        {
            PersonsGrid.SelectedItem = (PersonsGrid.ItemsSource as IEnumerable<PersonStopList>)?.FirstOrDefault(p => p.Id == person.Id);
        }
        StatusText.Text = Strings.Format("WL_Identify_Match", matches.Count, matches[0].Score ?? 0);
    });

    private async void ReverseCheck_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_person is null) return;
        var documents = await _matcher.MatchEntryAgainstRegisterAsync(_person.Id);
        SimResultsGrid.ItemsSource = documents;
        Tabs.SelectedIndex = 3;
        StatusText.Text = Strings.Format("WL_ReverseCheck_Result", documents.Count);
    });

    private async Task RefreshPrintsAsync(int personStopListId)
    {
        var prints = await _watchlists.GetFingerprintsAsync(personStopListId);
        PersonPrintsText.Text = prints.Count == 0
            ? Strings.Get("WL_NoFingerprints")
            : Strings.Format("WL_Fingerprints", string.Join(", ", prints.Select(p => p.FingerNumber)));
    }

    // ---- Documents -----------------------------------------------------------

    private async void SearchDocuments_Click(object sender, RoutedEventArgs e) => await LoadDocumentsAsync();

    private async Task LoadDocumentsAsync()
    {
        var previousId = _document?.Id;
        var items = await _watchlists.SearchDocumentsAsync(new DocumentStopListSearch(
            DocumentNumber: DocSearchNumber.Text,
            Surname: DocSearchSurname.Text,
            CountryId: (DocSearchCountry.SelectedItem as CountryVm)?.Id,
            ShowCancelled: DocShowCancelled.IsChecked == true));
        DocumentsGrid.ItemsSource = items;
        if (previousId is { } id) DocumentsGrid.SelectedItem = items.FirstOrDefault(d => d.Id == id);
    }

    private void Document_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentsGrid.SelectedItem is not DocumentStopList document) return;
        _document = document;
        DocNumberEdit.Text = document.DocumentNumber ?? string.Empty;
        DocPersonalNumberEdit.Text = document.PersonalNumber ?? string.Empty;
        DocSurnameEdit.Text = document.Surname ?? string.Empty;
        DocGivenNameEdit.Text = document.GivenName ?? string.Empty;
        DocBirthDateEdit.SelectedDate = document.DateOfBirth;
        DocSourceDocumentEdit.Text = document.SourceDocument ?? string.Empty;
        DocActionRequiredEdit.Text = document.ActionRequired ?? string.Empty;
        DocInfoEdit.Text = document.Info ?? string.Empty;
        SelectId(DocTypeEdit, document.DocumentTypeId);
        SelectId(DocSourceCombo, document.StopListSourceId);
        SelectId(DocActionCombo, document.MatchInStopListActionId);
        SelectCountry(DocCountryEdit, document.CountryId);
    }

    private void NewDocument_Click(object sender, RoutedEventArgs e)
    {
        _document = null;
        DocumentsGrid.SelectedItem = null;
        DocNumberEdit.Text = DocPersonalNumberEdit.Text = DocSurnameEdit.Text = DocGivenNameEdit.Text = string.Empty;
        DocBirthDateEdit.SelectedDate = null;
        DocSourceDocumentEdit.Text = DocActionRequiredEdit.Text = DocInfoEdit.Text = string.Empty;
        DocTypeEdit.SelectedIndex = 0;
        DocSourceCombo.SelectedIndex = 0;
        DocActionCombo.SelectedIndex = 0;
        DocCountryEdit.SelectedIndex = 0;
        StatusText.Text = Strings.Get("WL_NewEntryHint");
    }

    private async void SaveDocument_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var request = new DocumentStopListRequest(
            DocumentTypeId: (DocTypeEdit.SelectedItem as IdVm)?.Id,
            DocumentNumber: DocNumberEdit.Text,
            PersonalNumber: DocPersonalNumberEdit.Text,
            CountryId: (DocCountryEdit.SelectedItem as CountryVm)?.Id,
            Surname: DocSurnameEdit.Text,
            GivenName: DocGivenNameEdit.Text,
            DateOfBirth: DocBirthDateEdit.SelectedDate,
            StopListSourceId: (DocSourceCombo.SelectedItem as IdVm)?.Id,
            SourceDocument: DocSourceDocumentEdit.Text,
            Info: DocInfoEdit.Text,
            ActionRequired: DocActionRequiredEdit.Text,
            MatchInStopListActionId: (DocActionCombo.SelectedItem as IdVm)?.Id);

        if (_document is null)
        {
            var id = await _watchlists.CreateDocumentAsync(request);
            StatusText.Text = Strings.Format("WL_DocumentSaved", id);
        }
        else
        {
            await _watchlists.UpdateDocumentAsync(_document.Id, request);
            StatusText.Text = Strings.Format("WL_DocumentUpdated", _document.Id);
        }
        await LoadDocumentsAsync();
    });

    private async void CancelDocument_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_document is null) return;
        var cancelDocument = TextInputDialog.Ask(this, Strings.Get("WL_CancelAlert"), Strings.Get("WL_CancelDocument"), null);
        if (cancelDocument is null) return;

        await _watchlists.CancelDocumentAsync(_document.Id, (DocSourceCombo.SelectedItem as IdVm)?.Id, cancelDocument);
        StatusText.Text = Strings.Get("WL_AlertCancelled");
        await LoadDocumentsAsync();
    });

    // ---- Douteux -------------------------------------------------------------

    private async void SearchDoubts_Click(object sender, RoutedEventArgs e) => await LoadDoubtsAsync();

    private async void PendingDoubts_Click(object sender, RoutedEventArgs e)
    {
        DoubtStatusCombo.SelectedIndex = 1; // en attente
        await LoadDoubtsAsync();
    }

    private async Task LoadDoubtsAsync()
    {
        var previousId = _doubt?.Id;
        var status = (DoubtStatusCombo.SelectedItem as StatusVm)?.Value;
        var items = await _doubts.SearchAsync(status, DoubtFromPicker.SelectedDate,
            DoubtToPicker.SelectedDate?.AddDays(1).AddSeconds(-1));
        DoubtsGrid.ItemsSource = items;
        if (previousId is { } id) DoubtsGrid.SelectedItem = items.FirstOrDefault(d => d.Id == id);
        await RefreshCounterAsync();
    }

    private void Doubt_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DoubtsGrid.SelectedItem is not DoubtablePersonListItem doubt) return;
        _doubt = doubt;
        SupervisorNoteBox.Text = doubt.SupervisorNote ?? string.Empty;
        SelectId(DoubtActionCombo, doubt.PerformedActionId);
        SelectId(DoubtReasonCombo, null);
    }

    private async void ProcessDoubt_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_doubt is null) return;
        await _doubts.ProcessAsync(_doubt.Id, new DoubtableProcessRequest(
            SupervisorNote: SupervisorNoteBox.Text,
            PerformedActionId: (DoubtActionCombo.SelectedItem as IdVm)?.Id,
            RefusalOfficerName: RefusalOfficerBox.Text,
            RefusalOfficerRank: RefusalRankBox.Text,
            RefusalDocNumber: RefusalDocBox.Text,
            RefusalJustificationId: (DoubtJustificationCombo.SelectedItem as IdVm)?.Id));
        StatusText.Text = Strings.Get("WL_DoubtProcessed");
        await LoadDoubtsAsync();
    });

    private async void CancelDoubt_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_doubt is null) return;
        await _doubts.CancelAsync(_doubt.Id, SupervisorNoteBox.Text);
        StatusText.Text = Strings.Get("WL_DoubtCancelled");
        await LoadDoubtsAsync();
    });

    private async void AutoCheck_Changed(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_loadingAutoCheck) return;
        await _doubts.SetAutoCheckAsync(AutoCheckBox.IsChecked == true);
        StatusText.Text = Strings.Get(AutoCheckBox.IsChecked == true ? "WL_AutoCheck_On" : "WL_AutoCheck_Off");
    });

    private async Task RefreshCounterAsync()
    {
        var open = await _doubts.GetOpenCountAsync();
        OpenDoubtsText.Text = open == 0 ? string.Empty : Strings.Format("WL_OpenDoubts", open);
    }

    // ---- Simulateur ----------------------------------------------------------

    private async void Simulate_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var criteria = new StopListCriteria(
            Surname: SimSurname.Text,
            GivenName: SimGivenName.Text,
            DocumentNumber: SimDocument.Text,
            PersonalNumber: SimPersonalNumber.Text,
            DateOfBirth: SimBirthDate.SelectedDate,
            CitizenshipId: (SimCitizenship.SelectedItem as CountryVm)?.Id);

        var type = (SimTypeCombo.SelectedItem as IdVm)?.Id ?? 1;
        var kind = (SimKindCombo.SelectedItem as IdVm)?.Id ?? 1;

        if (kind == 1)
        {
            var results = await _matcher.SearchPersonsAsync(type, criteria);
            SimResultsGrid.ItemsSource = results;
            StatusText.Text = Strings.Format("WL_Simulate_Result", type, results.Count);
        }
        else
        {
            var results = await _matcher.SearchDocumentsAsync(type, criteria);
            SimResultsGrid.ItemsSource = results;
            StatusText.Text = Strings.Format("WL_Simulate_Result", type, results.Count);
        }
    });

    // ---- Aides ---------------------------------------------------------------

    private static List<IdVm> ToIds(IReadOnlyList<LookupItemDto> items)
        => items.Select(i => new IdVm(i.Id, string.IsNullOrEmpty(i.Fr) ? i.Code ?? i.Id.ToString() : i.Fr)).ToList();

    private static List<IdVm> WithEmpty(IEnumerable<IdVm> items)
    {
        var list = new List<IdVm> { new(null, "—") };
        list.AddRange(items);
        return list;
    }

    private static List<object> WithEmpty(List<CountryVm> countries)
    {
        var list = new List<object> { new CountryVm(null, "—") };
        list.AddRange(countries);
        return list;
    }

    private static void SelectId(ComboBox combo, int? id)
    {
        combo.SelectedItem = (combo.ItemsSource as IEnumerable<IdVm>)?.FirstOrDefault(i => i.Id == id)
                             ?? (combo.ItemsSource as IEnumerable<IdVm>)?.FirstOrDefault();
    }

    private static void SelectCountry(ComboBox combo, int? id)
    {
        combo.SelectedItem = (combo.ItemsSource as IEnumerable<CountryVm>)?.FirstOrDefault(c => c.Id == id)
                             ?? (combo.ItemsSource as IEnumerable<CountryVm>)?.FirstOrDefault();
    }

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
            ShowError(ex);
        }
    }

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);

    private sealed record IdVm(int? Id, string Display) { public override string ToString() => Display; }
    private sealed record CountryVm(int? Id, string Display) { public override string ToString() => Display; }
    private sealed record StatusVm(short? Value, string Display) { public override string ToString() => Display; }
}
