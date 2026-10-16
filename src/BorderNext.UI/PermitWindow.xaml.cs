using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Reporting;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Écran des permis (héritage : PermitRegistrationForm / PermitManagementForm) :
/// permis, résidence et situation, contacts / permis de conduire / antécédents,
/// voyage et activité indépendante, personnes à charge.
/// </summary>
public partial class PermitWindow : Window
{
    private readonly PermitService _permits;
    private readonly DocumentService _documents;
    private readonly VisaService _visas;
    private readonly ReferenceDataService _referenceData;
    private readonly GeoService _geo;
    private readonly ReportService _reports;
    private readonly IReportWriter _reportWriter;

    private int _documentId;
    private int _permitId;
    private bool _loadingMedical;

    public PermitWindow(PermitService permits, DocumentService documents, VisaService visas,
        ReferenceDataService referenceData, GeoService geo, ReportService reports, IReportWriter reportWriter)
    {
        _permits = permits;
        _documents = documents;
        _visas = visas;
        _referenceData = referenceData;
        _geo = geo;
        _reports = reports;
        _reportWriter = reportWriter;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        try
        {
            await FillLookupAsync(TypeCombo, "PERMIT_TYPE");
            await FillLookupAsync(StatusCombo, "PERMIT_STATUS");
            await FillLookupAsync(MaritalCombo, "MARITAL_STATUS");
            await FillLookupAsync(OccupationCombo, "OCCUPATION");
            await FillLookupAsync(QualificationCombo, "QUALIFICATION");
            await FillLookupAsync(CourseCombo, "COURSE_OF_STUDY");
            await FillLookupAsync(BloodCombo, "BLOOD_TYPE");
            await FillLookupAsync(ContactTypeCombo, "RELATION");
            await FillLookupAsync(DepRelationCombo, "RELATION");
            await FillLookupAsync(CurrentPermitTypeCombo, "PERMIT_TYPE");
            await FillLookupAsync(TravelReasonCombo, "TRAVEL_REASON");
            await FillLookupAsync(TravelTransportCombo, "TRANSPORT_TYPE");

            var countries = await _geo.GetCountriesAsync();
            var countryItems = new List<CountryVm> { new(null, "—") };
            countryItems.AddRange(countries.Select(c => new CountryVm(c.Id, c.Fr)));
            foreach (var combo in new[] { CurrentCountryCombo, BirthCountryCombo, SecondCitizenshipCombo, DepartureCountryCombo })
                combo.ItemsSource = countryItems.ToList();
            LicenseCountryCombo.ItemsSource = countryItems.ToList();

            var provinces = await _geo.GetProvincesAsync();
            var provinceItems = new List<IdVm> { new(null, "—") };
            provinceItems.AddRange(provinces.Select(p => new IdVm(p.Id, $"{p.Fr} ({p.CountryFr})")));
            CurrentProvinceCombo.ItemsSource = provinceItems;

            var cities = await _geo.GetCitiesAsync();
            var cityItems = new List<IdVm> { new(null, "—") };
            cityItems.AddRange(cities.Select(c => new IdVm(c.Id, $"{c.Fr} ({c.CountryFr})")));
            CurrentCityCombo.ItemsSource = cityItems;

            var ports = await _geo.GetBorderPointsAsync();
            var portItems = new List<PortVm> { new(null, "—") };
            portItems.AddRange(ports.Select(p => new PortVm(p.Id, $"{p.Code} — {p.Fr}")));
            PortCombo.ItemsSource = portItems;

            SetRowSelected(DependantsGrid);
            ResetToNew();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task FillLookupAsync(ComboBox combo, string typeCode)
    {
        var items = await _referenceData.GetItemsAsync(typeCode);
        combo.ItemsSource = items.Select(i => new LookupVm(i.Id, i.Code, string.IsNullOrEmpty(i.Fr) ? i.En : i.Fr)).ToList();
    }

    /// <summary>Sélectionne un statut de départ plausible (Pending/Initial) plutôt que le premier alphabétique.</summary>
    private void SelectDefaultStatus()
    {
        if (StatusCombo.ItemsSource is not IEnumerable<LookupVm> items) return;
        var preferred = items.FirstOrDefault(i => string.Equals(i.Code, "Pending", StringComparison.OrdinalIgnoreCase))
                        ?? items.FirstOrDefault(i => string.Equals(i.Code, "Initial", StringComparison.OrdinalIgnoreCase));
        StatusCombo.SelectedItem = preferred ?? items.FirstOrDefault();
    }

    // ---- Sélection du document porteur ---------------------------------------

    private async void SearchDocument_Click(object sender, RoutedEventArgs e)
    {
        var search = new DocumentSearchWindow(_documents, _geo) { Owner = this };
        if (search.ShowDialog() != true || search.SelectedDocumentId == 0) return;

        try
        {
            var detail = await _documents.GetAsync(search.SelectedDocumentId);
            if (detail is null) return;
            _documentId = detail.Id;
            HolderText.Text = Strings.Format("Permit_Holder", $"{detail.Surname} {detail.GivenName} ({detail.DocumentNumber})");

            var visas = await _visas.ListByDocumentAsync(detail.Id);
            var visaItems = new List<IdVm> { new(null, "—") };
            visaItems.AddRange(visas.Select(v => new IdVm(v.Id, $"{v.Number} ({v.TypeFr})")));
            VisaCombo.ItemsSource = visaItems;

            await LoadTravelAndBusinessAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task LoadTravelAndBusinessAsync()
    {
        var dependants = await _geo.GetCountriesAsync(); // (aucune action : simple préchargement des référentiels)
        _ = dependants;
    }

    private void ResetToNew()
    {
        _permitId = 0;
        _documentId = 0;
        HolderText.Text = Strings.Get("Permit_NoDocument");
        NumberBox.Text = string.Empty;
        IssuePicker.SelectedDate = DateTime.Today;
        ExpirePicker.SelectedDate = DateTime.Today.AddYears(2);
        NoteBox.Text = string.Empty;
        IssuingAuthorityBox.Text = string.Empty;
        OccupationOtherBox.Text = string.Empty;
        StatusText.Text = string.Empty;
        RevokeBtn.Content = Strings.Get("Common_Revoke");
        TypeCombo.SelectedIndex = 0;
        SelectDefaultStatus();
    }

    private void New_Click(object sender, RoutedEventArgs e) => ResetToNew();

    // ---- Enregistrement du permis --------------------------------------------

    private async void SaveHeader_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_documentId == 0) throw new DomainValidationException("PAL_Error_DocumentRequired");

        var request = new PermitSaveRequest(
            NumberBox.Text, _documentId, (VisaCombo.SelectedItem as IdVm)?.Id,
            (TypeCombo.SelectedItem as LookupVm)?.Id, IssuePicker.SelectedDate, ExpirePicker.SelectedDate,
            (StatusCombo.SelectedItem as LookupVm)?.Id, (MaritalCombo.SelectedItem as LookupVm)?.Id,
            (OccupationCombo.SelectedItem as LookupVm)?.Id, OccupationOtherBox.Text,
            (QualificationCombo.SelectedItem as LookupVm)?.Id, (CourseCombo.SelectedItem as LookupVm)?.Id,
            (BloodCombo.SelectedItem as LookupVm)?.Id, IssuingAuthorityBox.Text, NoteBox.Text);

        if (_permitId == 0)
            _permitId = await _permits.CreateAsync(request);
        else
            await _permits.UpdateAsync(_permitId, request);

        StatusText.Text = Strings.Get("Permit_Saved");
        await ReloadAsync();
    });

    private async Task ReloadAsync()
    {
        if (_permitId == 0) return;
        var detail = await _permits.GetDetailAsync(_permitId);
        if (detail is null) return;

        HolderText.Text = Strings.Format("Permit_Holder", detail.Header.HolderName ?? detail.Header.DocumentNumber);
        NumberBox.Text = detail.Header.Number ?? string.Empty;
        IssuePicker.SelectedDate = detail.Header.IssueDate;
        ExpirePicker.SelectedDate = detail.Header.ExpireDate;
        RevokeBtn.Content = Strings.Get(detail.Header.Revoke ? "Common_Restore" : "Common_Revoke");

        ContactsGrid.ItemsSource = detail.Contacts;
        LicensesGrid.ItemsSource = detail.DriverLicenses;
        RecordsGrid.ItemsSource = detail.SecurityRecords;
        DependantsGrid.ItemsSource = detail.Dependants;
        SetRowSelected(DependantsGrid);
        await ReloadMedicalAsync();

        // Détails : préremplissage si déjà saisis.
        if (detail.Details is { } d)
        {
            CurrentAddressBox.Text = d.CurrentAddress ?? string.Empty;
            CurrentTownBox.Text = d.CurrentTown ?? string.Empty;
            CurrentPhoneBox.Text = d.CurrentPhoneNumber ?? string.Empty;
            PreviousAddressBox.Text = d.PreviousAddress ?? string.Empty;
            PreviousTownBox.Text = d.PreviousTown ?? string.Empty;
            PreviousPostalBox.Text = d.PreviousPostalCode ?? string.Empty;
            BirthTownBox.Text = d.PlaceOfBirthTown ?? string.Empty;
            CurrentPermitNumberBox.Text = d.CurrentPermitNumber ?? string.Empty;
            ReferenceNameBox.Text = d.ReferencePersonName ?? string.Empty;
            ReferencePhoneBox.Text = d.ReferencePersonPhone ?? string.Empty;
            ChildrenBornBox.Text = d.NumberOfChildrenBornHere?.ToString() ?? string.Empty;
            ChildrenLivingBox.Text = d.NumberOfChildrenLivingHere?.ToString() ?? string.Empty;
            PeopleLiveWithBox.Text = d.NumberOfPeopleLiveWith?.ToString() ?? string.Empty;
            StayMoreCheck.IsChecked = d.IntendedToStayMore;
            FixJobCheck.IsChecked = d.HadFixJob;
            CitizenFamilyCheck.IsChecked = d.HasCitizenFamilyMember;
            ChildrenBornHereCheck.IsChecked = d.HasChildrenBornHere;
            LivingMigrantsCheck.IsChecked = d.LivingWithOtherMigrants;
            ChildrenSameHouseCheck.IsChecked = d.ChildrenLivingInSameHouse;
            ChildrenEducationCheck.IsChecked = d.ChildrenEnrolledEducation;
        }
        if (detail.Travel is { } t)
        {
            DepartureTownBox.Text = t.DepartureTown ?? string.Empty;
            DeparturePicker.SelectedDate = t.DepartureDate;
            ArrivalPicker.SelectedDate = t.ArrivalDate;
            FreeWillCheck.IsChecked = t.FreeWillTravel;
            ImmigrationCheckCheck.IsChecked = t.ImmigrationCheck;
            IrregularExitCheck.IsChecked = t.IrregularExit;
            SelectById(DepartureCountryCombo, t.DepartureCountryId);
            SelectById(TravelReasonCombo, t.TravelReasonId);
            SelectById(TravelTransportCombo, t.TransportTypeId);
            SelectPort(t.PortOfEntryId);
        }
        if (detail.SelfEmployed is { } s)
        {
            BusinessNameBox.Text = s.Name ?? string.Empty;
            BusinessAddressBox.Text = s.Address ?? string.Empty;
        }
    }

    private async void Revoke_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_permitId == 0) return;
        var detail = await _permits.GetDetailAsync(_permitId);
        await _permits.SetRevokedAsync(_permitId, !(detail?.Header.Revoke ?? false));
        await ReloadAsync();
    });

    // ---- Détails -------------------------------------------------------------

    private async void SaveDetails_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!RequirePermit()) return;
        await _permits.SaveDetailsAsync(_permitId, new PermitDetailsRequest(
            (CurrentCountryCombo.SelectedItem as CountryVm)?.Id, (CurrentProvinceCombo.SelectedItem as IdVm)?.Id,
            (CurrentCityCombo.SelectedItem as IdVm)?.Id, CurrentTownBox.Text, CurrentAddressBox.Text,
            CurrentPhoneBox.Text, null,
            null, null, null, PreviousTownBox.Text, PreviousAddressBox.Text, PreviousPostalBox.Text, null,
            (BirthCountryCombo.SelectedItem as CountryVm)?.Id, null, BirthTownBox.Text,
            (SecondCitizenshipCombo.SelectedItem as CountryVm)?.Id,
            (CurrentPermitTypeCombo.SelectedItem as LookupVm)?.Id, CurrentPermitNumberBox.Text,
            ReferenceNameBox.Text, ReferencePhoneBox.Text, null, null,
            ParseIntOrNull(ChildrenBornBox.Text), ParseIntOrNull(ChildrenLivingBox.Text), ParseIntOrNull(PeopleLiveWithBox.Text),
            StayMoreCheck.IsChecked == true, FixJobCheck.IsChecked == true, CitizenFamilyCheck.IsChecked == true,
            ChildrenBornHereCheck.IsChecked == true, LivingMigrantsCheck.IsChecked == true,
            ChildrenSameHouseCheck.IsChecked == true, ChildrenEducationCheck.IsChecked == true));
        StatusText.Text = Strings.Get("Permit_DetailsSaved");
    });

    // ---- Voyage et activité --------------------------------------------------

    private async void SaveTravel_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!RequirePermit()) return;
        await _permits.SaveTravelDetailsAsync(_permitId, new PermitTravelRequest(
            FreeWillCheck.IsChecked == true, (TravelReasonCombo.SelectedItem as LookupVm)?.Id, null,
            (TravelTransportCombo.SelectedItem as LookupVm)?.Id, (DepartureCountryCombo.SelectedItem as CountryVm)?.Id,
            null, DepartureTownBox.Text, DeparturePicker.SelectedDate, ImmigrationCheckCheck.IsChecked == true,
            (PortCombo.SelectedItem as PortVm)?.Id, ArrivalPicker.SelectedDate, IrregularExitCheck.IsChecked == true));
        await _permits.SaveSelfEmployedAsync(_permitId, new PermitSelfEmployedRequest(
            BusinessNameBox.Text, null, BusinessAddressBox.Text, null, null));
        StatusText.Text = Strings.Get("Permit_TravelSaved");
    });

    // ---- Contacts / conduite / antécédents -----------------------------------

    private async void AddContact_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!RequirePermit()) return;
        await _permits.AddContactAsync(_permitId, (ContactTypeCombo.SelectedItem as LookupVm)?.Id, ContactDescBox.Text);
        ContactDescBox.Text = string.Empty;
        await ReloadAsync();
    });

    private async void RemoveContact_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (ContactsGrid.SelectedItem is not PermitContact contact) return;
        await _permits.RemoveContactAsync(contact.Id);
        await ReloadAsync();
    });

    private async void AddLicense_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!RequirePermit()) return;
        await _permits.AddDriverLicenseAsync(_permitId, (LicenseCountryCombo.SelectedItem as CountryVm)?.Id, LicenseNumberBox.Text);
        LicenseNumberBox.Text = string.Empty;
        await ReloadAsync();
    });

    private async void RemoveLicense_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (LicensesGrid.SelectedItem is not PermitDriverLicense license) return;
        await _permits.RemoveDriverLicenseAsync(license.Id);
        await ReloadAsync();
    });

    private async void AddRecord_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!RequirePermit()) return;
        await _permits.AddSecurityRecordAsync(_permitId, RecordTypeBox.Text, RecordDescBox.Text);
        RecordTypeBox.Text = RecordDescBox.Text = string.Empty;
        await ReloadAsync();
    });

    private async void RemoveRecord_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (RecordsGrid.SelectedItem is not PermitSecurityRecord record) return;
        await _permits.RemoveSecurityRecordAsync(record.Id);
        await ReloadAsync();
    });

    // ---- Dépendants ----------------------------------------------------------

    private void Dependant_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DependantsGrid.SelectedItem is not PermitDependant dependant) return;
        DepSurnameBox.Text = dependant.Surname ?? string.Empty;
        DepGivenNameBox.Text = dependant.GivenName ?? string.Empty;
        DepBirthPicker.SelectedDate = dependant.DateOfBirth;
        DepDocumentBox.Text = dependant.DocumentNumber ?? string.Empty;
        DepPhoneBox.Text = dependant.PhoneNumber ?? string.Empty;
        SelectById(DepRelationCombo, dependant.RelationId);
    }

    private async void AddDependant_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!RequirePermit()) return;
        await _permits.AddDependantAsync(_permitId, CurrentDependantRequest());
        ClearDependantForm();
        await ReloadAsync();
    });

    private async void UpdateDependant_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (DependantsGrid.SelectedItem is not PermitDependant dependant) return;
        await _permits.UpdateDependantAsync(dependant.Id, CurrentDependantRequest());
        await ReloadAsync();
    });

    private async void RemoveDependant_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (DependantsGrid.SelectedItem is not PermitDependant dependant) return;
        await _permits.RemoveDependantAsync(dependant.Id);
        ClearDependantForm();
        await ReloadAsync();
    });

    private DependantRequest CurrentDependantRequest() => new(DepSurnameBox.Text, DepGivenNameBox.Text, null,
        DepBirthPicker.SelectedDate, null, (DepRelationCombo.SelectedItem as LookupVm)?.Id, null,
        DepDocumentBox.Text, null, null, DepPhoneBox.Text);

    private void ClearDependantForm()
    {
        DepSurnameBox.Text = DepGivenNameBox.Text = DepDocumentBox.Text = DepPhoneBox.Text = string.Empty;
        DepBirthPicker.SelectedDate = null;
        DepRelationCombo.SelectedIndex = 0;
    }

    // ---- Antécédents médicaux (héritage : PermitMedicalHistory) --------------

    /// <summary>Charge les antécédents du permis (et des personnes à charge) dans l'onglet.</summary>
    private async Task ReloadMedicalAsync()
    {
        if (_permitId == 0) return;

        // L'aide « enregistrez d'abord le permis » n'a plus lieu d'être une fois le permis créé.
        MedicalHint.Visibility = Visibility.Collapsed;

        // Repeupler la liste des personnes concernées déclenche SelectionChanged :
        // sans ce verrou, un second chargement démarrerait sur le même contexte EF.
        _loadingMedical = true;
        try
        {
            await LoadMedicalAsync();
        }
        finally
        {
            _loadingMedical = false;
        }
    }

    private async Task LoadMedicalAsync()
    {
        var dependantItems = new List<object> { new IdVm(null, Strings.Get("PM_Holder")) };
        foreach (var dependant in DependantsGrid.ItemsSource as IEnumerable<PermitDependant> ?? Enumerable.Empty<PermitDependant>())
            dependantItems.Add(new IdVm(dependant.Id, $"{dependant.Surname} {dependant.GivenName}".Trim()));
        var previous = (MedicalDependantCombo.SelectedItem as IdVm)?.Id;
        MedicalDependantCombo.ItemsSource = dependantItems;
        MedicalDependantCombo.SelectedIndex =
            dependantItems.FindIndex(v => (v as IdVm)?.Id == previous);
        if (MedicalDependantCombo.SelectedIndex < 0) MedicalDependantCombo.SelectedIndex = 0;

        var dependantId = (MedicalDependantCombo.SelectedItem as IdVm)?.Id;
        var history = await _permits.GetMedicalHistoryAsync(_permitId, dependantId);

        MedicalIllnessBox.Text = history?.CurrentIllness ?? string.Empty;
        MedicalMedicationBox.Text = history?.CurrentMedication ?? string.Empty;
        MedMalaria.IsChecked = history?.MalariaSuffered ?? false;
        MedYellowFever.IsChecked = history?.YellowFeverSuffered ?? false;
        MedTuberculosis.IsChecked = history?.TuberculosisSuffered ?? false;
        MedMeasles.IsChecked = history?.MeaslesSuffered ?? false;
        MedCholera.IsChecked = history?.CholeraSuffered ?? false;
        MedTyphoid.IsChecked = history?.TyphoidSuffered ?? false;
        MedJaundice.IsChecked = history?.JaundiceSuffered ?? false;
        MedChronicCough.IsChecked = history?.ChronicCoughSuffered ?? false;
        MedOtherSufferedBox.Text = history?.OtherSuffered ?? string.Empty;
        MedVaccinatedYellowFever.IsChecked = history?.YellowFeverVaccinated ?? false;
        MedVaccinatedMMR.IsChecked = history?.MMRVaccinated ?? false;
        MedVaccinatedDiphtheria.IsChecked = history?.DiphtheriaVaccinated ?? false;
        MedVaccinatedPolio.IsChecked = history?.PolioVaccinated ?? false;
        MedChronicDisease.IsChecked = history?.ChronicDisease ?? false;
        MedInfectiousDisease.IsChecked = history?.InfectiousDisease ?? false;
        MedHealthCentre.IsChecked = history?.HealthCentreVisited ?? false;
        MedPublicHealthCentre.IsChecked = history?.IsPublicHealthCentre ?? false;
        MedicalUpdatedText.Text = history?.UpdateDate is { } updated
            ? Strings.Format("PM_UpdatedOn", updated.ToString("dd/MM/yyyy HH:mm"), history.UpdateUser ?? "—")
            : string.Empty;
    }

    private async void MedicalDependant_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMedical || !IsLoaded || _permitId == 0) return;
        await GuardAsync(ReloadMedicalAsync);
    }

    private async void SaveMedical_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!RequirePermit()) return;

        await _permits.SaveMedicalHistoryAsync(_permitId, new PermitMedicalRequest(
            PermitDependantId: (MedicalDependantCombo.SelectedItem as IdVm)?.Id,
            CurrentIllness: MedicalIllnessBox.Text,
            CurrentMedication: MedicalMedicationBox.Text,
            MalariaSuffered: MedMalaria.IsChecked == true,
            YellowFeverSuffered: MedYellowFever.IsChecked == true,
            TuberculosisSuffered: MedTuberculosis.IsChecked == true,
            MeaslesSuffered: MedMeasles.IsChecked == true,
            CholeraSuffered: MedCholera.IsChecked == true,
            TyphoidSuffered: MedTyphoid.IsChecked == true,
            JaundiceSuffered: MedJaundice.IsChecked == true,
            ChronicCoughSuffered: MedChronicCough.IsChecked == true,
            OtherSuffered: MedOtherSufferedBox.Text,
            YellowFeverVaccinated: MedVaccinatedYellowFever.IsChecked == true,
            MMRVaccinated: MedVaccinatedMMR.IsChecked == true,
            DiphtheriaVaccinated: MedVaccinatedDiphtheria.IsChecked == true,
            PolioVaccinated: MedVaccinatedPolio.IsChecked == true,
            HealthCentreVisited: MedHealthCentre.IsChecked == true,
            ChronicDisease: MedChronicDisease.IsChecked == true,
            InfectiousDisease: MedInfectiousDisease.IsChecked == true,
            IsPublicHealthCentre: MedPublicHealthCentre.IsChecked == true));

        await ReloadMedicalAsync();
        MessageBox.Show(this, Strings.Get("PM_Saved"), Strings.Get("Common_Information"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    });

    /// <summary>Impression de la fiche d'antécédents médicaux (édition PDF).</summary>
    private async void PrintMedical_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!RequirePermit()) return;
        var detail = await _permits.GetDetailAsync(_permitId);
        var number = detail?.Header.Number ?? string.Empty;

        var template = await _reports.BuildAsync(ReportCatalog.MedicalHistoryForm,
            new ReportParameters(DocumentNumber: number));
        var localized = ReportLocalizer.Localize(template, Strings.Get);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"BorderNext_AntecedentsMedicaux_{DateTime.Now:HHmmss}.pdf");
        await System.IO.File.WriteAllBytesAsync(path, _reportWriter.RenderPdf(localized));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    });

    // ---- Aides ---------------------------------------------------------------

    private bool RequirePermit()
    {
        if (_permitId != 0) return true;
        MessageBox.Show(this, Strings.Get("Permit_RequiresSaved"), Strings.Get("Common_Information"),
            MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchDocument_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) Close();
    }

    private static void SetRowSelected(DataGrid grid)
    {
        if (grid.Items.Count > 0) grid.SelectedIndex = 0;
    }

    private static int? ParseIntOrNull(string? value) => int.TryParse(value, out var result) ? result : null;

    private static void SelectById(ComboBox combo, int? id)
    {
        if (combo.ItemsSource is not System.Collections.IEnumerable items) return;
        foreach (var item in items)
        {
            if (item is LookupVm l && l.Id == id) { combo.SelectedItem = item; return; }
            if (item is CountryVm c && c.Id == id) { combo.SelectedItem = item; return; }
            if (item is IdVm i && i.Id == id) { combo.SelectedItem = item; return; }
        }
    }

    private void SelectPort(short? portId)
    {
        if (PortCombo.ItemsSource is not System.Collections.IEnumerable items) return;
        foreach (var item in items)
            if (item is PortVm p && p.Id == portId) { PortCombo.SelectedItem = item; return; }
    }

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
        Serilog.Log.Error(ex, "Erreur de l'écran permis");
        MessageBox.Show(ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record LookupVm(int Id, string? Code, string Display) { public override string ToString() => Display; }
    private sealed record CountryVm(int? Id, string Display) { public override string ToString() => Display; }
    private sealed record IdVm(int? Id, string Display) { public override string ToString() => Display; }
    private sealed record PortVm(short? Id, string Display) { public override string ToString() => Display; }
}
