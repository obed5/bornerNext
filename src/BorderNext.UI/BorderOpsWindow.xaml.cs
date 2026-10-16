using System.Windows;
using System.Windows.Controls;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Opérations frontière (héritage : RegisterTripForm, RegistratedTripsForm, TripsForm,
/// PAXChangeFlight, VesselsForm, CarrierForm, TerminalForm) : voyages et passages
/// (vols, navires, trains), personnes rattachées et réaffectation, registre des
/// navires, référentiels des transporteurs et terminaux.
/// </summary>
public partial class BorderOpsWindow : Window
{
    private readonly TripService _trips;
    private readonly TransportReferentialService _referentials;
    private readonly ReferenceDataService _referenceData;
    private readonly GeoService _geo;

    private TripCrossingItem? _crossing;
    private VesselListItem? _vessel;
    private Carrier? _carrier;
    private Terminal? _terminal;

    private List<CodeVm> _transportTypes = new();
    private List<CodeVm> _directions = new();
    private List<CodeVm> _vesselTypes = new();
    private List<IdVm> _terminalChoices = new();
    private List<IdVm> _carrierChoices = new();
    private List<CountryVm> _countries = new();
    private List<CityVm> _cities = new();

    public BorderOpsWindow(TripService trips, TransportReferentialService referentials,
        ReferenceDataService referenceData, GeoService geo)
    {
        _trips = trips;
        _referentials = referentials;
        _referenceData = referenceData;
        _geo = geo;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        try
        {
            _transportTypes = (await _referenceData.GetItemsAsync("TRANSPORT_TYPE"))
                .Select(i => new CodeVm(i.Code ?? string.Empty, string.IsNullOrEmpty(i.Fr) ? i.Code ?? "" : i.Fr)).ToList();
            _directions = (await _referenceData.GetItemsAsync("MOVEMENT_DIRECTION"))
                .Select(i => new CodeVm(i.Code ?? string.Empty, string.IsNullOrEmpty(i.Fr) ? i.Code ?? "" : i.Fr)).ToList();
            _vesselTypes = (await _referenceData.GetItemsAsync("VESSEL_TYPE"))
                .Select(i => new CodeVm(i.Code ?? string.Empty, string.IsNullOrEmpty(i.Fr) ? i.Code ?? "" : i.Fr)).ToList();
            _countries = (await _geo.GetCountriesAsync())
                .Select(c => new CountryVm(c.Id, string.IsNullOrEmpty(c.Fr) ? c.En : c.Fr)).ToList();
            _cities = (await _geo.GetCitiesAsync())
                .Select(c => new CityVm(c.Id, string.IsNullOrEmpty(c.Fr) ? c.Code ?? c.Fr : c.Fr)).ToList();

            var terminals = await _referentials.SearchTerminalsAsync(take: 1000);
            _terminalChoices = new List<IdVm> { new(null, "—") };
            _terminalChoices.AddRange(terminals.Select(t => new IdVm(t.Id, $"{t.Code} — {t.DisplayName}")));

            var carriers = await _referentials.SearchCarriersAsync(take: 1000);
            _carrierChoices = new List<IdVm> { new(null, "—") };
            _carrierChoices.AddRange(carriers.Select(c => new IdVm(c.Id, $"{c.Code} — {c.DisplayName}")));

            var typeChoices = new List<CodeVm> { new(string.Empty, "—") };
            typeChoices.AddRange(_transportTypes);

            TripTypeFilter.ItemsSource = typeChoices;
            TripTypeFilter.SelectedIndex = 0;
            TripTypeEdit.ItemsSource = typeChoices;
            TripTypeEdit.SelectedIndex = 0;
            CarrierTypeFilter.ItemsSource = new List<CodeVm>(typeChoices);
            CarrierTypeFilter.SelectedIndex = 0;
            CarrierTypeEdit.ItemsSource = typeChoices;
            CarrierTypeEdit.SelectedIndex = 0;
            TerminalTypeFilter.ItemsSource = new List<CodeVm>(typeChoices);
            TerminalTypeFilter.SelectedIndex = 0;
            TerminalTypeEdit.ItemsSource = typeChoices;
            TerminalTypeEdit.SelectedIndex = 0;

            TripDirectionEdit.ItemsSource = _directions;
            if (_directions.Count > 0) TripDirectionEdit.SelectedIndex = 0;
            TripFromTerminal.ItemsSource = _terminalChoices;
            TripToTerminal.ItemsSource = _terminalChoices;
            TripFromTerminal.SelectedIndex = 0;
            TripToTerminal.SelectedIndex = 0;
            TripCarrierEdit.ItemsSource = _carrierChoices;
            TripCarrierEdit.SelectedIndex = 0;

            VesselTypeEdit.ItemsSource = new List<CodeVm> { new(string.Empty, "—") }.Concat(_vesselTypes).ToList();
            VesselTypeEdit.SelectedIndex = 0;
            VesselNationalityEdit.ItemsSource = new List<CountryVm> { new(null, "—") }.Concat(_countries).ToList();
            VesselNationalityEdit.SelectedIndex = 0;
            VesselHomePortEdit.ItemsSource = new List<CountryVm> { new(null, "—") }.Concat(_countries).ToList();
            VesselHomePortEdit.SelectedIndex = 0;

            CarrierCountryEdit.ItemsSource = new List<CountryVm> { new(null, "—") }.Concat(_countries).ToList();
            CarrierCountryEdit.SelectedIndex = 0;
            TerminalCountryEdit.ItemsSource = new List<CountryVm> { new(null, "—") }.Concat(_countries).ToList();
            TerminalCountryEdit.SelectedIndex = 0;
            TerminalCityEdit.ItemsSource = new List<CityVm> { new(null, "—") }.Concat(_cities).ToList();
            TerminalCityEdit.SelectedIndex = 0;

            await LoadTripsAsync();
            await LoadVesselsAsync();
            await LoadCarriersAsync();
            await LoadTerminalsAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ---- Voyages -------------------------------------------------------------

    private async void SearchTrips_Click(object sender, RoutedEventArgs e) => await LoadTripsAsync();

    private async Task LoadTripsAsync()
    {
        var previousId = _crossing?.CrossingId;
        var items = await _trips.GetCrossingsAsync(
            TripFromPicker.SelectedDate,
            TripToPicker.SelectedDate,
            (TripTypeFilter.SelectedItem as CodeVm)?.Code is { Length: > 0 } code ? code : null,
            TripOnlyOpen.IsChecked == true ? true : null);

        TripsGrid.ItemsSource = items;
        if (previousId is { } id) TripsGrid.SelectedItem = items.FirstOrDefault(c => c.CrossingId == id);

        var openTrips = await _trips.GetOpenTripsAsync();
        var targets = new List<IdVm>();
        targets.AddRange(openTrips
            .Where(t => t.CrossingId != previousId)
            .Select(t => new IdVm(t.CrossingId, $"{t.TripName} ({t.DirectionFr})")));
        ReassignTarget.ItemsSource = targets;
        if (targets.Count > 0) ReassignTarget.SelectedIndex = 0;
    }

    private async void Trip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TripsGrid.SelectedItem is not TripCrossingItem item) return;
        _crossing = item;

        TripNameEdit.Text = item.Name;
        SelectCode(TripTypeEdit, item.TransportTypeCode);
        SelectCode(TripDirectionEdit, item.DirectionCode);
        TripDateEdit.SelectedDate = item.Date;
        TripScheduledEdit.Text = item.ScheduledTime?.ToString(@"hh\:mm") ?? string.Empty;
        TripExternalEdit.Text = item.ExternalNumber ?? string.Empty;
        TripDescriptionEdit.Text = item.Description ?? string.Empty;
        SelectId(TripFromTerminal, item.FromTerminalId);
        SelectId(TripToTerminal, item.ToTerminalId);
        SelectId(TripCarrierEdit, item.CarrierId);
        OpenTripBtn.Content = Strings.Get(item.IsOpen ? "BO_Close" : "BO_Open");

        PassengersGrid.ItemsSource = await _trips.GetPassengersAsync(item.CrossingId);
    }

    private void NewTrip_Click(object sender, RoutedEventArgs e)
    {
        _crossing = null;
        TripsGrid.SelectedItem = null;
        TripNameEdit.Text = string.Empty;
        SelectCode(TripTypeEdit, "Flight");
        SelectCode(TripDirectionEdit, MovementService.EntryCode);
        TripDateEdit.SelectedDate = DateTime.Today;
        TripScheduledEdit.Text = string.Empty;
        TripExternalEdit.Text = TripDescriptionEdit.Text = string.Empty;
        TripFromTerminal.SelectedIndex = 0;
        TripToTerminal.SelectedIndex = 0;
        TripCarrierEdit.SelectedIndex = 0;
        PassengersGrid.ItemsSource = null;
        StatusText.Text = Strings.Get("BO_NewTripHint");
    }

    private async void SaveTrip_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var request = BuildTripRequest();
        if (_crossing is null)
        {
            var crossingId = await _trips.RegisterAsync(request);
            StatusText.Text = Strings.Format("BO_TripRegistered", crossingId);
        }
        else
        {
            await _trips.UpdateAsync(_crossing.CrossingId, request);
            StatusText.Text = Strings.Format("BO_TripUpdated", _crossing.CrossingId);
        }
        await LoadTripsAsync();
    });

    private TripRequest BuildTripRequest()
        => new(
            Name: TripNameEdit.Text,
            TransportTypeCode: (TripTypeEdit.SelectedItem as CodeVm)?.Code ?? string.Empty,
            Date: TripDateEdit.SelectedDate ?? DateTime.Today,
            DirectionCode: (TripDirectionEdit.SelectedItem as CodeVm)?.Code ?? MovementService.EntryCode,
            FromTerminalId: (TripFromTerminal.SelectedItem as IdVm)?.Id,
            ToTerminalId: (TripToTerminal.SelectedItem as IdVm)?.Id,
            CarrierId: (TripCarrierEdit.SelectedItem as IdVm)?.Id,
            ExternalNumber: TripExternalEdit.Text,
            Description: TripDescriptionEdit.Text,
            ScheduledTime: ParseTime(TripScheduledEdit.Text),
            Open: true);

    private async void ToggleOpen_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_crossing is null) return;
        await _trips.SetOpenAsync(_crossing.CrossingId, !_crossing.IsOpen);
        StatusText.Text = Strings.Get(_crossing.IsOpen ? "BO_Closed" : "BO_Opened");
        await LoadTripsAsync();
    });

    private async void DeleteTrip_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_crossing is null) return;
        var answer = MessageBox.Show(this, Strings.Format("BO_ConfirmDelete", _crossing.Name), Strings.Get("Common_Error"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await _trips.DeleteAsync(_crossing.CrossingId);
        _crossing = null;
        StatusText.Text = Strings.Get("BO_TripDeleted");
        await LoadTripsAsync();
    });

    private async void Reassign_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (PassengersGrid.SelectedItem is not TripPassengerItem passenger)
        {
            StatusText.Text = Strings.Get("BO_SelectPassenger");
            return;
        }
        if ((ReassignTarget.SelectedItem as IdVm)?.Id is not { } targetId)
        {
            StatusText.Text = Strings.Get("BO_NoTargetTrip");
            return;
        }

        await _trips.ReassignMovementAsync(passenger.MovementId, targetId);
        StatusText.Text = Strings.Format("BO_Reassigned", passenger.HolderName);
        if (_crossing is not null) PassengersGrid.ItemsSource = await _trips.GetPassengersAsync(_crossing.CrossingId);
    });

    // ---- Navires -------------------------------------------------------------

    private async void SearchVessels_Click(object sender, RoutedEventArgs e) => await LoadVesselsAsync();

    private async Task LoadVesselsAsync()
    {
        var previousId = _vessel?.Id;
        var items = await _referentials.SearchVesselsAsync(VesselSearch.Text);
        VesselsGrid.ItemsSource = items;
        if (previousId is { } id) VesselsGrid.SelectedItem = items.FirstOrDefault(v => v.Id == id);
    }

    private void Vessel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VesselsGrid.SelectedItem is not VesselListItem item) return;
        _vessel = item;
        VesselNameEdit.Text = item.Name;
        VesselFormerNameEdit.Text = item.FormerName ?? string.Empty;
        VesselRegistrationEdit.Text = item.RegistrationNumber;
        VesselDimensionEdit.Text = VesselColorEdit.Text = string.Empty;
        VesselCallSignEdit.Text = item.RadioCallSign ?? string.Empty;
        VesselFrequenciesEdit.Text = string.Empty;
        SelectCode(VesselTypeEdit, null);
        VesselNationalityEdit.SelectedIndex = 0;
        VesselHomePortEdit.SelectedIndex = 0;
        _ = LoadVesselDetailAsync(item.Id);
    }

    private async Task LoadVesselDetailAsync(int id)
    {
        var vessel = await _referentials.GetVesselAsync(id);
        if (vessel == null) return;
        VesselDimensionEdit.Text = vessel.Dimension ?? string.Empty;
        VesselColorEdit.Text = vessel.Color ?? string.Empty;
        VesselFrequenciesEdit.Text = vessel.Frequencies ?? string.Empty;
        SelectCode(VesselTypeEdit, vessel.VesselType?.Code);
        SelectCountry(VesselNationalityEdit, vessel.NationalityCountryId);
        SelectCountry(VesselHomePortEdit, vessel.HomePortCountryId);
    }

    private void NewVessel_Click(object sender, RoutedEventArgs e)
    {
        _vessel = null;
        VesselsGrid.SelectedItem = null;
        VesselNameEdit.Text = VesselFormerNameEdit.Text = VesselRegistrationEdit.Text = string.Empty;
        VesselDimensionEdit.Text = VesselColorEdit.Text = VesselCallSignEdit.Text = VesselFrequenciesEdit.Text = string.Empty;
        VesselTypeEdit.SelectedIndex = 0;
        VesselNationalityEdit.SelectedIndex = 0;
        VesselHomePortEdit.SelectedIndex = 0;
        StatusText.Text = Strings.Get("WL_NewEntryHint");
    }

    private async void SaveVessel_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var request = new VesselRequest(
            Name: VesselNameEdit.Text,
            RegistrationNumber: VesselRegistrationEdit.Text,
            FormerName: VesselFormerNameEdit.Text,
            HomePortCountryId: (VesselHomePortEdit.SelectedItem as CountryVm)?.Id,
            NationalityCountryId: (VesselNationalityEdit.SelectedItem as CountryVm)?.Id,
            Dimension: VesselDimensionEdit.Text,
            Color: VesselColorEdit.Text,
            VesselTypeCode: (VesselTypeEdit.SelectedItem as CodeVm)?.Code is { Length: > 0 } code ? code : null,
            RadioCallSign: VesselCallSignEdit.Text,
            Frequencies: VesselFrequenciesEdit.Text);

        if (_vessel is null)
        {
            var id = await _referentials.CreateVesselAsync(request);
            StatusText.Text = Strings.Format("BO_VesselSaved", id);
        }
        else
        {
            await _referentials.UpdateVesselAsync(_vessel.Id, request);
            StatusText.Text = Strings.Format("BO_VesselUpdated", _vessel.Id);
        }
        await LoadVesselsAsync();
    });

    // ---- Transporteurs -------------------------------------------------------

    private async void SearchCarriers_Click(object sender, RoutedEventArgs e) => await LoadCarriersAsync();

    private async Task LoadCarriersAsync()
    {
        var previousId = _carrier?.Id;
        var typeCode = (CarrierTypeFilter.SelectedItem as CodeVm)?.Code is { Length: > 0 } code ? code : null;
        var items = await _referentials.SearchCarriersAsync(CarrierSearch.Text, typeCode);
        CarriersGrid.ItemsSource = items;
        if (previousId is { } id) CarriersGrid.SelectedItem = items.FirstOrDefault(c => c.Id == id);
    }

    private void Carrier_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CarriersGrid.SelectedItem is not Carrier carrier) return;
        _carrier = carrier;
        CarrierCodeEdit.Text = carrier.Code;
        CarrierFrEdit.Text = carrier.DisplayName ?? string.Empty;
        SelectCode(CarrierTypeEdit, carrier.TransportType?.Code);
        SelectCountry(CarrierCountryEdit, carrier.CountryId);
        RevokeCarrierBtn.Content = Strings.Get(carrier.Revoke ? "Common_Restore" : "Common_Revoke");
    }

    private void NewCarrier_Click(object sender, RoutedEventArgs e)
    {
        _carrier = null;
        CarriersGrid.SelectedItem = null;
        CarrierCodeEdit.Text = CarrierFrEdit.Text = string.Empty;
        CarrierTypeEdit.SelectedIndex = 0;
        CarrierCountryEdit.SelectedIndex = 0;
        StatusText.Text = Strings.Get("WL_NewEntryHint");
    }

    private async void SaveCarrier_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var request = new CarrierRequest(
            Code: CarrierCodeEdit.Text,
            Fr: CarrierFrEdit.Text,
            En: CarrierFrEdit.Text,
            TransportTypeCode: (CarrierTypeEdit.SelectedItem as CodeVm)?.Code is { Length: > 0 } code ? code : null,
            CountryId: (CarrierCountryEdit.SelectedItem as CountryVm)?.Id);

        if (_carrier is null)
        {
            var id = await _referentials.CreateCarrierAsync(request);
            StatusText.Text = Strings.Format("BO_CarrierSaved", id);
        }
        else
        {
            await _referentials.UpdateCarrierAsync(_carrier.Id, request);
            StatusText.Text = Strings.Format("BO_CarrierUpdated", _carrier.Id);
        }
        await LoadCarriersAsync();
    });

    private async void ToggleCarrierRevoked_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_carrier is null) return;
        await _referentials.SetCarrierRevokedAsync(_carrier.Id, !_carrier.Revoke);
        StatusText.Text = Strings.Get(_carrier.Revoke ? "BO_Restored" : "BO_Revoked");
        await LoadCarriersAsync();
    });

    // ---- Terminaux -----------------------------------------------------------

    private async void SearchTerminals_Click(object sender, RoutedEventArgs e) => await LoadTerminalsAsync();

    private async Task LoadTerminalsAsync()
    {
        var previousId = _terminal?.Id;
        var typeCode = (TerminalTypeFilter.SelectedItem as CodeVm)?.Code is { Length: > 0 } code ? code : null;
        var items = await _referentials.SearchTerminalsAsync(TerminalSearch.Text, typeCode);
        TerminalsGrid.ItemsSource = items;
        if (previousId is { } id) TerminalsGrid.SelectedItem = items.FirstOrDefault(t => t.Id == id);
    }

    private void Terminal_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TerminalsGrid.SelectedItem is not Terminal terminal) return;
        _terminal = terminal;
        TerminalCodeEdit.Text = terminal.Code;
        TerminalFrEdit.Text = terminal.DisplayName ?? string.Empty;
        SelectCode(TerminalTypeEdit, terminal.TransportType?.Code);
        SelectCountry(TerminalCountryEdit, terminal.CountryId);
        SelectCity(TerminalCityEdit, terminal.CityId);
        RevokeTerminalBtn.Content = Strings.Get(terminal.Revoke ? "Common_Restore" : "Common_Revoke");
    }

    private void NewTerminal_Click(object sender, RoutedEventArgs e)
    {
        _terminal = null;
        TerminalsGrid.SelectedItem = null;
        TerminalCodeEdit.Text = TerminalFrEdit.Text = string.Empty;
        TerminalTypeEdit.SelectedIndex = 0;
        TerminalCountryEdit.SelectedIndex = 0;
        TerminalCityEdit.SelectedIndex = 0;
        StatusText.Text = Strings.Get("WL_NewEntryHint");
    }

    private async void SaveTerminal_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var request = new TerminalRequest(
            Code: TerminalCodeEdit.Text,
            Fr: TerminalFrEdit.Text,
            En: TerminalFrEdit.Text,
            TransportTypeCode: (TerminalTypeEdit.SelectedItem as CodeVm)?.Code is { Length: > 0 } code ? code : null,
            CountryId: (TerminalCountryEdit.SelectedItem as CountryVm)?.Id,
            CityId: (TerminalCityEdit.SelectedItem as CityVm)?.Id);

        if (_terminal is null)
        {
            var id = await _referentials.CreateTerminalAsync(request);
            StatusText.Text = Strings.Format("BO_TerminalSaved", id);
        }
        else
        {
            await _referentials.UpdateTerminalAsync(_terminal.Id, request);
            StatusText.Text = Strings.Format("BO_TerminalUpdated", _terminal.Id);
        }
        await LoadTerminalsAsync();
    });

    private async void ToggleTerminalRevoked_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_terminal is null) return;
        await _referentials.SetTerminalRevokedAsync(_terminal.Id, !_terminal.Revoke);
        StatusText.Text = Strings.Get(_terminal.Revoke ? "BO_Restored" : "BO_Revoked");
        await LoadTerminalsAsync();
    });

    // ---- Aides ---------------------------------------------------------------

    private static TimeSpan? ParseTime(string? value)
        => TimeSpan.TryParse(value, out var time) ? time : null;

    private static void SelectCode(ComboBox combo, string? code)
        => combo.SelectedItem = (combo.ItemsSource as IEnumerable<CodeVm>)?.FirstOrDefault(c => c.Code == code)
                                ?? (combo.ItemsSource as IEnumerable<CodeVm>)?.FirstOrDefault();

    private static void SelectId(ComboBox combo, int? id)
        => combo.SelectedItem = (combo.ItemsSource as IEnumerable<IdVm>)?.FirstOrDefault(i => i.Id == id)
                                ?? (combo.ItemsSource as IEnumerable<IdVm>)?.FirstOrDefault();

    private static void SelectCountry(ComboBox combo, int? id)
        => combo.SelectedItem = (combo.ItemsSource as IEnumerable<CountryVm>)?.FirstOrDefault(c => c.Id == id)
                                ?? (combo.ItemsSource as IEnumerable<CountryVm>)?.FirstOrDefault();

    private static void SelectCity(ComboBox combo, int? id)
        => combo.SelectedItem = (combo.ItemsSource as IEnumerable<CityVm>)?.FirstOrDefault(c => c.Id == id)
                                ?? (combo.ItemsSource as IEnumerable<CityVm>)?.FirstOrDefault();

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

    private sealed record CodeVm(string Code, string Display) { public override string ToString() => Display; }
    private sealed record IdVm(int? Id, string Display) { public override string ToString() => Display; }
    private sealed record CountryVm(int? Id, string Display) { public override string ToString() => Display; }
    private sealed record CityVm(int? Id, string Display) { public override string ToString() => Display; }
}
