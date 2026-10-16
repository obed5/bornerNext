using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;
using Microsoft.Win32;

namespace BorderNext.UI;

/// <summary>
/// Enregistrement passeport (héritage : IndividualPassportRegistrationForm) :
/// document de voyage, état civil, mouvement frontière (entrée/sortie),
/// photo et document scanné, historique des mouvements.
/// </summary>
public partial class PassportWindow : Window
{
    private readonly DocumentService _documents;
    private readonly MovementService _movements;
    private readonly VisaService _visas;
    private readonly ReferenceDataService _referenceData;
    private readonly GeoService _geo;
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentReader _documentReader;
    private readonly ICameraDevice _camera;
    private readonly IDeviceRegistry _devices;
    private readonly BiometricService _biometrics;
    private readonly IFingerprintScanner _fingerprintScanner;
    private readonly StopListMatcher _stopListMatcher;
    private readonly DoubtablePersonService _doubtfulPersons;
    private readonly TripService _trips;
    private readonly QuestionnaireService _questionnaires;
    private readonly ReportService _reports;
    private readonly IReportWriter _reportWriter;

    private int _documentId;                 // 0 = nouveau document
    private byte[]? _pendingPhoto;           // photo chargée à enregistrer avec le mouvement
    private byte[]? _pendingScan;

    public PassportWindow(DocumentService documents, MovementService movements, VisaService visas,
        ReferenceDataService referenceData, GeoService geo, ICurrentUser currentUser,
        IDocumentReader documentReader, ICameraDevice camera, IDeviceRegistry devices,
        BiometricService biometrics, IFingerprintScanner fingerprintScanner,
        StopListMatcher stopListMatcher, DoubtablePersonService doubtfulPersons, TripService trips,
        QuestionnaireService questionnaires, ReportService reports, IReportWriter reportWriter)
    {
        _documents = documents;
        _movements = movements;
        _visas = visas;
        _referenceData = referenceData;
        _geo = geo;
        _currentUser = currentUser;
        _documentReader = documentReader;
        _camera = camera;
        _devices = devices;
        _biometrics = biometrics;
        _fingerprintScanner = fingerprintScanner;
        _stopListMatcher = stopListMatcher;
        _doubtfulPersons = doubtfulPersons;
        _trips = trips;
        _questionnaires = questionnaires;
        _reports = reports;
        _reportWriter = reportWriter;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    // ---- Chargement ----------------------------------------------------------

    private async Task LoadAllAsync()
    {
        try
        {
            await FillLookupComboAsync(DocTypeCombo, "DOCUMENT_TYPE");
            await FillLookupComboAsync(SexCombo, "SEX");
            await FillLookupComboAsync(DirectionCombo, "MOVEMENT_DIRECTION");
            await FillLookupComboAsync(TravelReasonCombo, "TRAVEL_REASON");
            await FillLookupComboAsync(TransportCombo, "TRANSPORT_TYPE");
            await FillLookupComboAsync(AccommodationCombo, "ACCOMODATION_TYPE");
            await FillLookupComboAsync(DeportationReasonCombo, "DEPORTATION_REASON");
            await FillCountryComboAsync(CountryOfIssueCombo);
            await FillCountryComboAsync(CitizenshipCombo);
            await FillCountryComboAsync(ResidentCountryCombo);
            await FillCountryComboAsync(DestinationCountryCombo);
            await LoadTripsAsync();
            TravelDatePicker.SelectedDate = DateTime.Today;
            DeviceStatus.Text = string.Join(" · ", _devices.GetStatus()
                .Select(s => $"{s.Name}{(s.IsSimulated ? " (sim.)" : "")}"));
            ResetToNewDocument();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task FillLookupComboAsync(ComboBox combo, string typeCode)
    {
        var items = await _referenceData.GetItemsAsync(typeCode);
        combo.ItemsSource = items.Select(i => new LookupVm(i.Id, i.Code, string.IsNullOrEmpty(i.Fr) ? i.En : i.Fr)).ToList();
    }

    private async Task FillCountryComboAsync(ComboBox combo)
    {
        var countries = await _geo.GetCountriesAsync();
        var items = new List<CountryVm> { new(null, "—") };
        items.AddRange(countries.Select(c => new CountryVm(c.Id, c.Fr)));
        combo.ItemsSource = items;
        combo.SelectedIndex = 0;
    }

    // ---- État du formulaire --------------------------------------------------

    private void ResetToNewDocument()
    {
        _documentId = 0;
        _pendingPhoto = null;
        _pendingScan = null;
        PhotoPreview.Source = null;
        ScanStatus.Text = string.Empty;
        ModeText.Text = Strings.Get("PAL_Mode_New");

        DocNumberBox.Text = string.Empty;
        PersonalNumberBox.Text = string.Empty;
        SurnameBox.Text = string.Empty;
        GivenNameBox.Text = string.Empty;
        FatherNameBox.Text = string.Empty;
        IssueDatePicker.SelectedDate = null;
        ExpireDatePicker.SelectedDate = null;
        BirthDatePicker.SelectedDate = null;
        DocTypeCombo.SelectedIndex = 0;
        SexCombo.SelectedIndex = 0;
        CountryOfIssueCombo.SelectedIndex = 0;
        CitizenshipCombo.SelectedIndex = 0;

        DirectionCombo.SelectedIndex = 0;
        TravelDatePicker.SelectedDate = DateTime.Today;
        TravelReasonCombo.SelectedIndex = 0;
        TransportCombo.SelectedIndex = 0;
        ResidentCountryCombo.SelectedIndex = 0;
        DestinationCountryCombo.SelectedIndex = 0;
        AccommodationCombo.SelectedIndex = 0;
        DeportationReasonCombo.SelectedIndex = 0;
        AccommodationAddressBox.Text = string.Empty;
        OccupationBox.Text = string.Empty;
        DurationBox.Text = string.Empty;
        ChildrenBox.Text = "0";
        NoteBox.Text = string.Empty;
        DriverCheck.IsChecked = false;
        TransitCheck.IsChecked = false;
        HasVisaCheck.IsChecked = false;
        GrantedCheck.IsChecked = true;
        VisaCombo.ItemsSource = null;

        HistoryGrid.ItemsSource = null;
        StatusText.Text = string.Empty;
    }

    private async Task LoadDocumentAsync(int documentId)
    {
        var detail = await _documents.GetAsync(documentId);
        if (detail is null) return;

        _documentId = documentId;
        _pendingPhoto = null;
        _pendingScan = null;
        ModeText.Text = Strings.Format("PAL_Mode_Existing", detail.DocumentNumber);

        DocNumberBox.Text = detail.DocumentNumber;
        SelectById(DocTypeCombo, detail.DocumentTypeId);
        SelectById(CountryOfIssueCombo, detail.CountryOfIssueId);
        IssueDatePicker.SelectedDate = detail.IssueDate;
        ExpireDatePicker.SelectedDate = detail.ExpireDate;
        SurnameBox.Text = detail.Surname;
        GivenNameBox.Text = detail.GivenName;
        FatherNameBox.Text = detail.FatherName;
        BirthDatePicker.SelectedDate = detail.DateOfBirth;
        SelectById(SexCombo, detail.SexId);
        SelectById(CitizenshipCombo, detail.CitizenshipId);
        PersonalNumberBox.Text = detail.PersonalNumber;

        await RefreshImagesAsync(documentId);
        await RefreshVisasAsync(documentId);
        await RefreshHistoryAsync(documentId);
    }

    private async Task RefreshImagesAsync(int documentId)
    {
        var (photo, scan) = await _documents.GetImagesAsync(documentId);
        PhotoPreview.Source = photo is { Length: > 0 } ? ToImage(photo) : null;
        ScanStatus.Text = scan is { Length: > 0 } ? Strings.Get("PAL_LoadScan") + " ✓" : string.Empty;
    }

    private async Task RefreshVisasAsync(int documentId)
    {
        var visas = await _visas.ListByDocumentAsync(documentId);
        var items = new List<VisaVm> { new(null, "—") };
        items.AddRange(visas.Select(v => new VisaVm(v.Id, $"{v.Number} ({v.TypeFr}{(v.IsValid ? "" : " ⚠")})")));
        VisaCombo.ItemsSource = items;
        VisaCombo.SelectedIndex = 0;
    }

    private async Task RefreshHistoryAsync(int documentId)
    {
        HistoryGrid.ItemsSource = await _movements.GetHistoryAsync(documentId);
    }

    private static void SelectById(ComboBox combo, int? id)
    {
        if (combo.ItemsSource is not System.Collections.IEnumerable items) return;
        foreach (var item in items)
        {
            if (item is LookupVm l && l.Id == id) { combo.SelectedItem = item; return; }
            if (item is CountryVm c && c.Id == id) { combo.SelectedItem = item; return; }
        }
    }

    // ---- Actions -------------------------------------------------------------

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var search = new DocumentSearchWindow(_documents, _geo) { Owner = this };
        if (search.ShowDialog() == true && search.SelectedDocumentId > 0)
        {
            try
            {
                await LoadDocumentAsync(search.SelectedDocumentId);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) => ResetToNewDocument();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var documentRequest = new DocumentSaveRequest(
                DocNumberBox.Text,
                (DocTypeCombo.SelectedItem as LookupVm)?.Id ?? 0,
                (CountryOfIssueCombo.SelectedItem as CountryVm)?.Id,
                IssueDatePicker.SelectedDate, ExpireDatePicker.SelectedDate,
                SurnameBox.Text, GivenNameBox.Text, FatherNameBox.Text, BirthDatePicker.SelectedDate,
                (SexCombo.SelectedItem as LookupVm)?.Id, (CitizenshipCombo.SelectedItem as CountryVm)?.Id,
                PersonalNumberBox.Text);

            // Nouveau document : recherche par (type, numéro) puis création — un même
            // passeport reste une même personne (comportement hérité) ; sinon mise à jour.
            if (_documentId == 0)
                _documentId = await _documents.FindOrCreateAsync(documentRequest);
            else
                await _documents.UpdateAsync(_documentId, documentRequest);

            if (_pendingPhoto is not null) await _documents.SetPhotoAsync(_documentId, _pendingPhoto);
            if (_pendingScan is not null) await _documents.SetScannedDocumentAsync(_documentId, _pendingScan);
            _pendingPhoto = null;
            _pendingScan = null;

            var direction = (DirectionCombo.SelectedItem as LookupVm)?.Code ?? string.Empty;
            var movementRequest = new MovementRequest(
                _documentId, direction, TravelDatePicker.SelectedDate ?? DateTime.Today,
                (TransportCombo.SelectedItem as LookupVm)?.Id, DriverCheck.IsChecked == true,
                (TravelReasonCombo.SelectedItem as LookupVm)?.Id, GrantedCheck.IsChecked == true,
                (DeportationReasonCombo.SelectedItem as LookupVm)?.Id, TransitCheck.IsChecked == true,
                HasVisaCheck.IsChecked == true, (VisaCombo.SelectedItem as VisaVm)?.Id,
                ParseInt(ChildrenBox.Text), NoteBox.Text,
                (ResidentCountryCombo.SelectedItem as CountryVm)?.Id,
                (DestinationCountryCombo.SelectedItem as CountryVm)?.Id,
                OccupationBox.Text, AccommodationAddressBox.Text,
                (AccommodationCombo.SelectedItem as LookupVm)?.Id, ParseShort(DurationBox.Text),
                (TripCombo.SelectedItem as TripVm)?.Id);

            var movementId = await _movements.RegisterAsync(movementRequest);

            StatusText.Text = Strings.Format("PAL_Saved", movementId);
            await RunWatchlistCheckAsync(movementId);
            await LoadDocumentAsync(_documentId); // recharge images, visas, historique
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

    /// <summary>Fiche sanitaire du voyageur (questionnaires) : ouverte sur le document
    /// enregistré, sans quitter l'enregistrement passeport.</summary>
    private void HealthForm_Click(object sender, RoutedEventArgs e)
    {
        var window = new HealthQuestionnaireWindow(_questionnaires, _documents, _reports, _reportWriter)
        {
            Owner = this,
        };
        if (_documentId != 0)
            window.PreselectDocument(_documentId, $"{SurnameBox.Text} {GivenNameBox.Text} — {DocNumberBox.Text}".Trim());
        window.Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void LoadPhoto_Click(object sender, RoutedEventArgs e)
    {
        var bytes = PickImageFile();
        if (bytes is null) return;
        _pendingPhoto = bytes;
        PhotoPreview.Source = ToImage(bytes);
    }

    private async void ReadDocument_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var result = await _documentReader.ReadAsync();
        if (!result.Success)
        {
            MessageBox.Show(this, Strings.Get(result.ErrorCode ?? "DEVICE_Error_Unreadable"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Mappage des codes lus vers les référentiels internes.
        var docTypes = await _referenceData.GetItemsAsync("DOCUMENT_TYPE");
        var sexes = await _referenceData.GetItemsAsync("SEX");
        var countries = await _geo.GetCountriesAsync();

        if (result.DocumentTypeCode is { } typeCode
            && docTypes.FirstOrDefault(t => string.Equals(t.Code, typeCode, StringComparison.OrdinalIgnoreCase)) is { } docType)
            SelectById(DocTypeCombo, docType.Id);
        DocNumberBox.Text = result.DocumentNumber ?? string.Empty;
        SurnameBox.Text = result.Surname ?? string.Empty;
        GivenNameBox.Text = result.GivenNames ?? string.Empty;
        BirthDatePicker.SelectedDate = result.BirthDate;
        IssueDatePicker.SelectedDate = result.IssueDate;
        ExpireDatePicker.SelectedDate = result.ExpireDate;
        PersonalNumberBox.Text = result.PersonalNumber ?? string.Empty;

        if (result.SexCode is { } sexCode
            && sexes.FirstOrDefault(s => string.Equals(s.Code, sexCode, StringComparison.OrdinalIgnoreCase)) is { } sex)
            SelectById(SexCombo, sex.Id);
        if (result.NationalityAlpha3 is { } alpha3
            && countries.FirstOrDefault(c => string.Equals(c.Alpha3, alpha3, StringComparison.OrdinalIgnoreCase)) is { } nationality)
            SelectById(CitizenshipCombo, nationality.Id);
        if (result.CountryAlpha3 is { } issueAlpha3
            && countries.FirstOrDefault(c => string.Equals(c.Alpha3, issueAlpha3, StringComparison.OrdinalIgnoreCase)) is { } issuer)
            SelectById(CountryOfIssueCombo, issuer.Id);

        StatusText.Text = Strings.Format("PAL_DocumentRead", $"{result.DocumentNumber} — {result.Surname} {result.GivenNames}");
    });

    private async void CapturePhoto_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var photo = await _camera.CaptureAsync();
        _pendingPhoto = photo;
        PhotoPreview.Source = ToImage(photo);
        StatusText.Text = Strings.Get("PAL_PhotoCaptured");
    });

    private void Fingerprints_Click(object sender, RoutedEventArgs e)
    {
        if (_documentId == 0)
        {
            MessageBox.Show(this, Strings.Get("Bio_RequiresSavedDocument"), Strings.Get("Common_Information"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _ = OpenFingerprintsAsync();
    }

    private async Task OpenFingerprintsAsync()
    {
        var detail = await _documents.GetAsync(_documentId);
        if (detail is null) return;
        var window = new FingerprintsWindow(detail.PersonId, $"{detail.Surname} {detail.GivenName} ({detail.DocumentNumber})",
            _biometrics, _fingerprintScanner) { Owner = this };
        window.ShowDialog();
    }

    private void LoadScan_Click(object sender, RoutedEventArgs e)
    {
        var bytes = PickImageFile();
        if (bytes is null) return;
        _pendingScan = bytes;
        ScanStatus.Text = Strings.Get("PAL_LoadScan") + " ✓";
    }

    private byte[]? PickImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Tous les fichiers (*.*)|*.*",
            Title = Strings.Get("PAL_LoadPhoto")
        };
        if (dialog.ShowDialog(this) != true) return null;
        try
        {
            return File.ReadAllBytes(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return null;
        }
    }

    private void HasVisa_Changed(object sender, RoutedEventArgs e)
    {
        if (VisaCombo is not null)
            VisaCombo.IsEnabled = HasVisaCheck.IsChecked == true;
    }

    private void Granted_Changed(object sender, RoutedEventArgs e)
    {
        if (DeportationReasonCombo is not null)
            DeportationReasonCombo.IsEnabled = GrantedCheck.IsChecked != true;
    }

    /// <summary>
    /// Contrôle d'alerte à l'enregistrement (héritage : appel systématique des
    /// fonctions de liste à chaque inscription) : liste personnes puis documents,
    /// alarme affichée ; selon le réglage hérité « CheckInDoubtablePersons »,
    /// une personne douteuse est inscrite en seconde inspection (motifs FDP/FD,
    /// SSC à défaut), et un avertissement est affiché si la personne a déjà fait
    /// l'objet d'une décision au-delà de « Permit » (fn_CheckPersonInDoubtablePersons).
    /// </summary>
    private async Task RunWatchlistCheckAsync(int movementId)
    {
        var criteria = new StopListCriteria(
            Surname: SurnameBox.Text,
            GivenName: GivenNameBox.Text,
            DocumentNumber: DocNumberBox.Text,
            PersonalNumber: PersonalNumberBox.Text,
            DateOfBirth: BirthDatePicker.SelectedDate,
            CitizenshipId: (CitizenshipCombo.SelectedItem as CountryVm)?.Id);

        var personHit = await _stopListMatcher.CheckPersonAsync(criteria);
        var documentHit = personHit is null ? await _stopListMatcher.CheckDocumentAsync(criteria) : null;
        var hit = personHit ?? documentHit;

        if (hit is not null)
        {
            var action = string.IsNullOrWhiteSpace(hit.ActionCode) ? string.Empty : $" — {hit.ActionCode}";
            MessageBox.Show(this,
                Strings.Format("WL_Alarm_Message", Strings.Get(hit.Kind == StopListKind.Person ? "WL_List_Persons" : "WL_List_Documents"),
                    hit.Criterion, $"{hit.Surname} {hit.GivenName}".Trim(), action, hit.ActionRequired ?? hit.Info ?? string.Empty),
                Strings.Get("WL_Alarm_Title"), MessageBoxButton.OK, MessageBoxImage.Exclamation);
        }

        var priorAction = await _doubtfulPersons.HasPriorActionAsync(
            SurnameBox.Text, GivenNameBox.Text, BirthDatePicker.SelectedDate, DocNumberBox.Text);

        if (await _doubtfulPersons.IsAutoCheckEnabledAsync() && (hit is not null || priorAction))
        {
            var reasonCode = hit?.Kind == StopListKind.Person ? "FDP"
                : hit?.Kind == StopListKind.Document ? "FD"
                : priorAction ? "SSC" : null;
            var reasonId = reasonCode is null ? null : await _doubtfulPersons.GetCheckingReasonIdAsync(reasonCode);
            await _doubtfulPersons.RegisterAsync(new DoubtablePersonRequest(
                movementId,
                CheckingReasonId: reasonId,
                InspectorNote: hit is null ? null : $"{hit.Surname} {hit.GivenName} (critère {hit.Criterion})".Trim(),
                PersonStopListId: hit?.Kind == StopListKind.Person ? hit.EntryId : null,
                DocumentStopListId: hit?.Kind == StopListKind.Document ? hit.EntryId : null,
                PhotoJustScanned: null));
            StatusText.Text = Strings.Get("WL_DoubtRegistered");
        }
        else if (priorAction)
        {
            MessageBox.Show(this, Strings.Get("WL_Warning_PriorDoubt"), Strings.Get("WL_Alarm_Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Passages ouverts du poste (héritage : GetOpenedTrips — l'enregistrement
    /// des personnes se fait « dans » une salle d'enregistrement ouverte).</summary>
    private async Task LoadTripsAsync()
    {
        var openTrips = await _trips.GetOpenTripsAsync();
        var items = new List<TripVm> { new(null, Strings.Get("BO_NoTrip")) };
        items.AddRange(openTrips.Select(t => new TripVm(t.CrossingId, $"{t.TripName} ({t.DirectionFr})")));
        TripCombo.ItemsSource = items;
        TripCombo.SelectedIndex = 0;
    }

    private async void RefreshTrips_Click(object sender, RoutedEventArgs e) => await GuardAsync(LoadTripsAsync);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { Save_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { Search_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { New_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) Close();
    }

    // ---- Aides ---------------------------------------------------------------

    /// <summary>
    /// Conversion d'une image enregistrée. Une donnée illisible (image reprise d'un système
    /// antérieur, fichier tronqué, format inconnu) ne doit jamais bloquer l'écran : la
    /// vignette est alors simplement absente, le dossier reste consultable.
    /// </summary>
    private static BitmapImage? ToImage(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // Format non pris en charge ou flux corrompu : l'image est ignorée.
            return null;
        }
    }

    private static int ParseInt(string? value) => int.TryParse(value, out var result) ? result : 0;
    private static short? ParseShort(string? value) => short.TryParse(value, out var result) ? result : null;

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

    private static void ShowError(Exception ex)
    {
        Serilog.Log.Error(ex, "Erreur de l'écran passeport");
        MessageBox.Show(ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record LookupVm(int Id, string? Code, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record CountryVm(int? Id, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record TripVm(int? Id, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record VisaVm(int? Id, string Display)
    {
        public override string ToString() => Display;
    }
}
