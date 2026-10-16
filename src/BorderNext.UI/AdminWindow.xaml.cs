using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;
using Microsoft.Win32;

namespace BorderNext.UI;

/// <summary>
/// Fenêtre d'administration (Lot 1) : référentiels, géographie, postes
/// frontières, utilisateurs et droits, paramétrage, journal d'audit.
/// Accès filtré par la matrice des droits (héritage : ControlsToRoles).
/// </summary>
public partial class AdminWindow : Window
{
    private readonly ReferenceDataService _referenceData;
    private readonly GeoService _geo;
    private readonly UserAdminService _users;
    private readonly PermissionService _permissions;
    private readonly AuditQueryService _audit;
    private readonly SettingsService _settings;
    private readonly UserBiometricsService _userBiometrics;
    private readonly IFingerprintScanner _fingerprintScanner;
    private readonly ICurrentUser _currentUser;

    // ---- État d'édition ------------------------------------------------------
    private LookupTypeVm? _lookupType;
    private LookupItemDto? _lookupItem;
    private CountryDto? _country;
    private ProvinceDto? _province;
    private CityDto? _city;
    private BorderPointDto? _borderPoint;
    private UserListItem? _user;
    private SettingDto? _setting;
    private IReadOnlyList<AuditEntryDto> _auditRows = Array.Empty<AuditEntryDto>();

    public AdminWindow(ReferenceDataService referenceData, GeoService geo, UserAdminService users,
        PermissionService permissions, AuditQueryService audit, SettingsService settings,
        UserBiometricsService userBiometrics, IFingerprintScanner fingerprintScanner, ICurrentUser currentUser)
    {
        _referenceData = referenceData;
        _geo = geo;
        _users = users;
        _permissions = permissions;
        _audit = audit;
        _settings = settings;
        _userBiometrics = userBiometrics;
        _fingerprintScanner = fingerprintScanner;
        _currentUser = currentUser;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    // ---- Chargement ----------------------------------------------------------

    private async Task LoadAllAsync()
    {
        try
        {
            if (!await ApplyPermissionsAsync()) return;
            await LoadLookupTypesAsync();
            await LoadCountriesAsync();
            await LoadProvincesAsync();
            await LoadCitiesAsync();
            await LoadBorderPointsAsync();
            await LoadRolesAsync();
            await LoadUsersAsync();
            await LoadSettingsAsync();
            await LoadAuditEventTypesAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Échec du chargement de l'administration");
            MessageBox.Show(this, ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Masque les onglets non couverts par la matrice des droits.</summary>
    private async Task<bool> ApplyPermissionsAsync()
    {
        var tabs = new (TabItem Tab, string Control)[]
        {
            (TabReference, "DM_CONTROL"),
            (TabGeo, "DM_CONTROL"),
            (TabBorderPoints, "DM_CTRL_MNG"),
            (TabUsers, "DM_USERS"),
            (TabSettings, "DM_CTRL_MNG"),
            (TabAudit, "DM_REP")
        };

        var isSuper = _currentUser.RoleCode is "ADMIN" or "SUPERUSER";
        var visible = 0;
        foreach (var (tab, control) in tabs)
        {
            var allowed = isSuper || await _permissions.CanAsync(control);
            tab.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
            if (allowed) visible++;
        }

        if (visible == 0)
        {
            MessageBox.Show(this, Strings.Get("Admin_NoPermission"), Strings.Get("Common_Information"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
            return false;
        }
        return true;
    }

    // ---- Référentiels --------------------------------------------------------

    private async Task LoadLookupTypesAsync()
    {
        var types = await _referenceData.GetTypesAsync();
        LookupTypesList.ItemsSource = types.Select(t => new LookupTypeVm(t.Id, t.Code, $"{t.NameFr} ({t.ItemCount})")).ToList();
    }

    private async void LookupType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _lookupType = LookupTypesList.SelectedItem as LookupTypeVm;
        _lookupItem = null;
        await LoadLookupItemsAsync();
    }

    private async Task LoadLookupItemsAsync()
    {
        if (_lookupType is null) { LookupItemsGrid.ItemsSource = null; return; }
        LookupItemsGrid.ItemsSource = await _referenceData.GetItemsAsync(_lookupType.Code,
            LookupShowRevoked.IsChecked == true, LookupSearch.Text);
        ClearLookupEditor();
    }

    private void ClearLookupEditor()
    {
        _lookupItem = null;
        LookupCodeBox.Text = string.Empty;
        LookupFrBox.Text = string.Empty;
        LookupEnBox.Text = string.Empty;
        LookupSortBox.Text = "0";
        LookupRevokeBtn.Content = Strings.Get("Common_Revoke");
    }

    private void LookupItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LookupItemsGrid.SelectedItem is not LookupItemDto item) return;
        _lookupItem = item;
        LookupCodeBox.Text = item.Code ?? string.Empty;
        LookupFrBox.Text = item.Fr;
        LookupEnBox.Text = item.En;
        LookupSortBox.Text = item.SortOrder.ToString();
        LookupRevokeBtn.Content = Strings.Get(item.Revoke ? "Common_Restore" : "Common_Revoke");
    }

    private async void LookupFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadLookupItemsAsync();
    }

    private async void LookupSearch_Click(object sender, RoutedEventArgs e) => await LoadLookupItemsAsync();

    private void LookupNew_Click(object sender, RoutedEventArgs e) => ClearLookupEditor();

    private async void LookupSave_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_lookupType is null) throw new DomainValidationException("Admin_Error_TypeNotFound");
        var sort = ParseInt(LookupSortBox.Text, 0);
        if (_lookupItem is null)
            await _referenceData.CreateItemAsync(_lookupType.Code, LookupCodeBox.Text, sort, LookupFrBox.Text, LookupEnBox.Text);
        else
            await _referenceData.UpdateItemAsync(_lookupItem.Id, LookupCodeBox.Text, sort, LookupFrBox.Text, LookupEnBox.Text);
        await LoadLookupItemsAsync();
        await LoadLookupTypesAsync();
    });

    private async void LookupRevoke_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_lookupItem is null) return;
        await _referenceData.SetItemRevokedAsync(_lookupItem.Id, !_lookupItem.Revoke);
        await LoadLookupItemsAsync();
        await LoadLookupTypesAsync();
    });

    // ---- Pays ----------------------------------------------------------------

    private async Task LoadCountriesAsync()
    {
        var countries = await _geo.GetCountriesAsync();
        CountriesGrid.ItemsSource = countries;

        var combo = new List<CountryComboVm> { new(null, $"({Strings.Get("Common_All")})") };
        combo.AddRange(countries.Select(c => new CountryComboVm(c.Id, $"{c.Fr} ({(c.Alpha2 ?? "—")})")));
        CountryRegionCombo.ItemsSource = await BuildRegionComboAsync();

        ProvinceCountryCombo.ItemsSource = combo.Where(c => c.Id != null).ToList();
        CityCountryCombo.ItemsSource = combo.Where(c => c.Id != null).ToList();
    }

    private async Task<IReadOnlyList<RegionComboVm>> BuildRegionComboAsync()
    {
        var regions = new List<RegionComboVm> { new(null, "—") };
        var items = await _referenceData.GetItemsAsync("REGION");
        regions.AddRange(items.Select(r => new RegionComboVm(r.Code, string.IsNullOrEmpty(r.Fr) ? r.Code ?? "?" : r.Fr)));
        return regions;
    }

    private void Country_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CountriesGrid.SelectedItem is not CountryDto country) return;
        _country = country;
        CountryAlpha2Box.Text = country.Alpha2 ?? string.Empty;
        CountryAlpha3Box.Text = country.Alpha3 ?? string.Empty;
        CountryFrBox.Text = country.Fr;
        CountryEnBox.Text = country.En;
        CountrySortBox.Text = country.SortOrder.ToString();
        CountryRegionCombo.SelectedItem = (CountryRegionCombo.ItemsSource as IEnumerable<RegionComboVm>)
            ?.FirstOrDefault(r => r.Code == country.RegionCode);
        CountryRevokeBtn.Content = Strings.Get(country.Revoke ? "Common_Restore" : "Common_Revoke");
    }

    private void CountryNew_Click(object sender, RoutedEventArgs e)
    {
        _country = null;
        CountriesGrid.SelectedItem = null;
        CountryAlpha2Box.Text = CountryAlpha3Box.Text = CountryFrBox.Text = CountryEnBox.Text = string.Empty;
        CountrySortBox.Text = "0";
        CountryRegionCombo.SelectedIndex = 0;
        CountryRevokeBtn.Content = Strings.Get("Common_Revoke");
    }

    private async void CountryFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        CountriesGrid.ItemsSource = await _geo.GetCountriesAsync(CountryShowRevoked.IsChecked == true, CountrySearch.Text);
    }

    private async void CountrySearch_Click(object sender, RoutedEventArgs e)
        => CountriesGrid.ItemsSource = await _geo.GetCountriesAsync(CountryShowRevoked.IsChecked == true, CountrySearch.Text);

    private async void CountrySave_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var sort = ParseInt(CountrySortBox.Text, 0);
        var region = (CountryRegionCombo.SelectedItem as RegionComboVm)?.Code;
        if (_country is null)
            await _geo.CreateCountryAsync(CountryAlpha2Box.Text, CountryAlpha3Box.Text, region, sort, CountryFrBox.Text, CountryEnBox.Text);
        else
            await _geo.UpdateCountryAsync(_country.Id, CountryAlpha2Box.Text, CountryAlpha3Box.Text, region, sort, CountryFrBox.Text, CountryEnBox.Text);
        await ReloadCountriesAsync();
    });

    private async void CountryRevoke_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_country is null) return;
        await _geo.SetCountryRevokedAsync(_country.Id, !_country.Revoke);
        await ReloadCountriesAsync();
    });

    private async Task ReloadCountriesAsync()
    {
        CountrySearch.Text = string.Empty;
        await LoadCountriesAsync();
        await LoadCitiesAsync();
    }

    // ---- Provinces -----------------------------------------------------------

    private async Task LoadProvincesAsync()
        => ProvincesGrid.ItemsSource = await _geo.GetProvincesAsync(includeRevoked: ProvinceShowRevoked.IsChecked == true);

    private void Province_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProvincesGrid.SelectedItem is not ProvinceDto province) return;
        _province = province;
        ProvinceCountryCombo.SelectedItem = (ProvinceCountryCombo.ItemsSource as IEnumerable<CountryComboVm>)
            ?.FirstOrDefault(c => c.Id == province.CountryId);
        ProvinceCodeBox.Text = province.Code ?? string.Empty;
        ProvinceFrBox.Text = province.Fr;
        ProvinceEnBox.Text = province.En;
        ProvinceAbbrBox.Text = province.Abbreviation ?? string.Empty;
        ProvinceSortBox.Text = province.SortOrder.ToString();
        ProvinceRevokeBtn.Content = Strings.Get(province.Revoke ? "Common_Restore" : "Common_Revoke");
    }

    private void ProvinceNew_Click(object sender, RoutedEventArgs e)
    {
        _province = null;
        ProvincesGrid.SelectedItem = null;
        ProvinceCodeBox.Text = ProvinceFrBox.Text = ProvinceEnBox.Text = ProvinceAbbrBox.Text = string.Empty;
        ProvinceSortBox.Text = "0";
        ProvinceRevokeBtn.Content = Strings.Get("Common_Revoke");
    }

    private async void ProvinceFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadProvincesAsync();
    }

    private async void ProvinceSave_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var countryId = (ProvinceCountryCombo.SelectedItem as CountryComboVm)?.Id
            ?? throw new DomainValidationException("Admin_Error_CountryRequired");
        var sort = ParseInt(ProvinceSortBox.Text, 0);
        if (_province is null)
            await _geo.CreateProvinceAsync(countryId, ProvinceCodeBox.Text, sort, ProvinceFrBox.Text, ProvinceEnBox.Text, ProvinceAbbrBox.Text);
        else
            await _geo.UpdateProvinceAsync(_province.Id, countryId, ProvinceCodeBox.Text, sort, ProvinceFrBox.Text, ProvinceEnBox.Text, ProvinceAbbrBox.Text);
        await LoadProvincesAsync();
    });

    private async void ProvinceRevoke_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_province is null) return;
        await _geo.SetProvinceRevokedAsync(_province.Id, !_province.Revoke);
        await LoadProvincesAsync();
    });

    // ---- Villes --------------------------------------------------------------

    private async Task LoadCitiesAsync()
    {
        CitiesGrid.ItemsSource = await _geo.GetCitiesAsync(includeRevoked: CityShowRevoked.IsChecked == true, search: CitySearch.Text);
        var provinces = await _geo.GetProvincesAsync();
        var combo = new List<ProvinceComboVm> { new(null, "—") };
        combo.AddRange(provinces.Select(p => new ProvinceComboVm(p.Id, $"{p.Fr} ({p.CountryFr})")));
        CityProvinceCombo.ItemsSource = combo;
    }

    private void City_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CitiesGrid.SelectedItem is not CityDto city) return;
        _city = city;
        CityCountryCombo.SelectedItem = (CityCountryCombo.ItemsSource as IEnumerable<CountryComboVm>)
            ?.FirstOrDefault(c => c.Id == city.CountryId);
        CityProvinceCombo.SelectedItem = (CityProvinceCombo.ItemsSource as IEnumerable<ProvinceComboVm>)
            ?.FirstOrDefault(p => p.Id == city.ProvinceId);
        CityCodeBox.Text = city.Code ?? string.Empty;
        CityFrBox.Text = city.Fr;
        CityEnBox.Text = city.En;
        CityAbbrBox.Text = city.Abbreviation ?? string.Empty;
        CitySortBox.Text = city.SortOrder.ToString();
        CityRevokeBtn.Content = Strings.Get(city.Revoke ? "Common_Restore" : "Common_Revoke");
    }

    private void CityNew_Click(object sender, RoutedEventArgs e)
    {
        _city = null;
        CitiesGrid.SelectedItem = null;
        CityCodeBox.Text = CityFrBox.Text = CityEnBox.Text = CityAbbrBox.Text = string.Empty;
        CitySortBox.Text = "0";
        CityProvinceCombo.SelectedIndex = 0;
        CityRevokeBtn.Content = Strings.Get("Common_Revoke");
    }

    private async void CityFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadCitiesAsync();
    }

    private async void CitySearch_Click(object sender, RoutedEventArgs e) => await LoadCitiesAsync();

    private async void CitySave_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var countryId = (CityCountryCombo.SelectedItem as CountryComboVm)?.Id
            ?? throw new DomainValidationException("Admin_Error_CountryRequired");
        var provinceId = (CityProvinceCombo.SelectedItem as ProvinceComboVm)?.Id;
        var sort = ParseInt(CitySortBox.Text, 0);
        if (_city is null)
            await _geo.CreateCityAsync(countryId, provinceId, CityCodeBox.Text, sort, CityFrBox.Text, CityEnBox.Text, CityAbbrBox.Text);
        else
            await _geo.UpdateCityAsync(_city.Id, countryId, provinceId, CityCodeBox.Text, sort, CityFrBox.Text, CityEnBox.Text, CityAbbrBox.Text);
        await LoadCitiesAsync();
    });

    private async void CityRevoke_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_city is null) return;
        await _geo.SetCityRevokedAsync(_city.Id, !_city.Revoke);
        await LoadCitiesAsync();
    });

    // ---- Postes frontières ---------------------------------------------------

    private async Task LoadBorderPointsAsync()
    {
        var points = await _geo.GetBorderPointsAsync(BorderPointShowRevoked.IsChecked == true);
        BorderPointsGrid.ItemsSource = points;

        var combo = new List<BorderPointComboVm> { new(null, "—") };
        combo.AddRange(points.Where(p => !p.Revoke).Select(p => new BorderPointComboVm(p.Id, $"{p.Code} — {p.Fr}")));
        UserBorderPointCombo.ItemsSource = combo;
        SettingBorderPointCombo.ItemsSource = combo.ToList();
    }

    private void BorderPoint_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BorderPointsGrid.SelectedItem is not BorderPointDto point) return;
        _borderPoint = point;
        BorderPointCodeBox.Text = point.Code;
        BorderPointFrBox.Text = point.Fr;
        BorderPointEnBox.Text = point.En;
        BorderPointSiteFrBox.Text = point.RegionalSiteFr ?? string.Empty;
        BorderPointSortBox.Text = point.SortOrder.ToString();
        BorderPointRevokeBtn.Content = Strings.Get(point.Revoke ? "Common_Restore" : "Common_Revoke");
    }

    private void BorderPointNew_Click(object sender, RoutedEventArgs e)
    {
        _borderPoint = null;
        BorderPointsGrid.SelectedItem = null;
        BorderPointCodeBox.Text = BorderPointFrBox.Text = BorderPointEnBox.Text = BorderPointSiteFrBox.Text = string.Empty;
        BorderPointSortBox.Text = "0";
        BorderPointRevokeBtn.Content = Strings.Get("Common_Revoke");
    }

    private async void BorderPointFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadBorderPointsAsync();
    }

    private async void BorderPointSave_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var sort = ParseInt(BorderPointSortBox.Text, 0);
        if (_borderPoint is null)
            await _geo.CreateBorderPointAsync(BorderPointCodeBox.Text, sort, BorderPointFrBox.Text, BorderPointEnBox.Text, BorderPointSiteFrBox.Text, BorderPointSiteFrBox.Text);
        else
            await _geo.UpdateBorderPointAsync(_borderPoint.Id, BorderPointCodeBox.Text, sort, BorderPointFrBox.Text, BorderPointEnBox.Text, BorderPointSiteFrBox.Text, BorderPointSiteFrBox.Text);
        await LoadBorderPointsAsync();
    });

    private async void BorderPointRevoke_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_borderPoint is null) return;
        await _geo.SetBorderPointRevokedAsync(_borderPoint.Id, !_borderPoint.Revoke);
        await LoadBorderPointsAsync();
    });

    // ---- Utilisateurs --------------------------------------------------------

    private async Task LoadRolesAsync()
    {
        var roles = await _users.GetRolesAsync();
        var roleVms = roles.Select(r => new RoleComboVm(r.Id, $"{r.Name} ({r.Code})")).ToList();
        UserRoleCombo.ItemsSource = roleVms;
        RightsRoleCombo.ItemsSource = roleVms.ToList();
    }

    private async Task LoadUsersAsync()
        => UsersGrid.ItemsSource = await _users.GetUsersAsync(UserShowRevoked.IsChecked == true, UserSearch.Text);

    private void User_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserListItem user) return;
        _user = user;
        UserIdBox.Text = user.Id;
        UserIdBox.IsEnabled = false;
        UserNameBox.Text = user.UserName;
        UserFirstNameBox.Text = user.FirstName ?? string.Empty;
        UserLastNameBox.Text = user.LastName ?? string.Empty;
        UserRoleCombo.SelectedItem = (UserRoleCombo.ItemsSource as IEnumerable<RoleComboVm>)?.FirstOrDefault(r => r.Id == user.RoleId);
        UserBorderPointCombo.SelectedItem = (UserBorderPointCombo.ItemsSource as IEnumerable<BorderPointComboVm>)
            ?.FirstOrDefault(p => p.Id == user.BorderPointId);
        UserRevokeBtn.Content = Strings.Get(user.Revoke ? "Common_Restore" : "Common_Revoke");
        _ = LoadUserBiometricsAsync(user.Id);
    }

    // ---- Biométrie opérateur (héritage : Users.First/SecondFingerPrint) -------

    private async Task LoadUserBiometricsAsync(string userId)
    {
        try
        {
            var status = await _userBiometrics.GetStatusAsync(userId);
            var slots = status.ToDictionary(s => s.Slot, s => s.Enrolled);
            UserBiometricsText.Text = Strings.Format("Bio_User_Status",
                Strings.Get(slots.GetValueOrDefault(1) ? "Bio_Yes" : "Bio_No"),
                Strings.Get(slots.GetValueOrDefault(2) ? "Bio_Yes" : "Bio_No"));
            Enroll1Btn.IsEnabled = Clear1Btn.IsEnabled = true;
            Enroll2Btn.IsEnabled = Clear2Btn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Échec du chargement de la biométrie opérateur");
            UserBiometricsText.Text = string.Empty;
        }
    }

    private async void Enroll1_Click(object sender, RoutedEventArgs e) => await EnrollOperatorAsync(1);

    private async void Enroll2_Click(object sender, RoutedEventArgs e) => await EnrollOperatorAsync(2);

    private async Task EnrollOperatorAsync(int slot)
    {
        if (_user is null) return;
        var dialog = new FingerprintScanDialog(_fingerprintScanner, (short)slot,
            $"{_user.UserName} — {Strings.Format("Bio_Enroll_Slot1", slot)}") { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Capture is null) return;

        await GuardAsync(async () =>
        {
            await _userBiometrics.EnrollAsync(_user.Id, slot, dialog.Capture);
            StatusText.Text = Strings.Format("Bio_User_Enrolled", slot);
            await LoadUserBiometricsAsync(_user.Id);
        });
    }

    private async void Clear1_Click(object sender, RoutedEventArgs e) => await ClearOperatorAsync(1);

    private async void Clear2_Click(object sender, RoutedEventArgs e) => await ClearOperatorAsync(2);

    private async Task ClearOperatorAsync(int slot)
    {
        if (_user is null) return;
        await GuardAsync(async () =>
        {
            await _userBiometrics.ClearAsync(_user.Id, slot);
            StatusText.Text = Strings.Format("Bio_User_Cleared", slot);
            await LoadUserBiometricsAsync(_user.Id);
        });
    }

    private async void UserFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadUsersAsync();
    }

    private async void UserSearch_Click(object sender, RoutedEventArgs e) => await LoadUsersAsync();

    private async void UserCreate_Click(object sender, RoutedEventArgs e)
    {
        var prompt = new PasswordPromptWindow(Strings.Format("Admin_Users_New") + " — " + Strings.Get("PasswordPrompt_Info")) { Owner = this };
        if (prompt.ShowDialog() != true) return;

        await GuardAsync(async () =>
        {
            var roleId = (UserRoleCombo.SelectedItem as RoleComboVm)?.Id
                ?? throw new DomainValidationException("Admin_Error_ItemNotFound", "role");
            var borderPointId = (UserBorderPointCombo.SelectedItem as BorderPointComboVm)?.Id;
            await _users.CreateUserAsync(UserIdBox.Text, UserNameBox.Text, UserFirstNameBox.Text, UserLastNameBox.Text,
                roleId, borderPointId, prompt.Password!);
            await LoadUsersAsync();
        });
    }

    private async void UserSave_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_user is null) return;
        var roleId = (UserRoleCombo.SelectedItem as RoleComboVm)?.Id
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", "role");
        var borderPointId = (UserBorderPointCombo.SelectedItem as BorderPointComboVm)?.Id;
        await _users.UpdateUserAsync(_user.Id, UserFirstNameBox.Text, UserLastNameBox.Text, roleId, borderPointId);
        await LoadUsersAsync();
    });

    private async void UserResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (_user is null) return;
        var prompt = new PasswordPromptWindow($"{_user.UserName} — {Strings.Get("PasswordPrompt_Info")}") { Owner = this };
        if (prompt.ShowDialog() != true) return;

        await GuardAsync(async () =>
        {
            await _users.ResetPasswordAsync(_user.Id, prompt.Password!);
            await LoadUsersAsync();
        });
    }

    private async void UserRevoke_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_user is null) return;
        await _users.SetUserRevokedAsync(_user.Id, !_user.Revoke);
        await LoadUsersAsync();
    });

    // ---- Droits --------------------------------------------------------------

    private sealed class RightVm
    {
        public int ControlId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Fr { get; init; } = string.Empty;
        public bool Permission { get; set; }
        public bool Visibility { get; set; }
    }

    private async void RightsRole_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RightsRoleCombo.SelectedItem is not RoleComboVm role) return;
        var rights = await _permissions.GetRoleRightsAsync(role.Id);
        RightsGrid.ItemsSource = rights.Select(r => new RightVm
        {
            ControlId = r.ControlId,
            Code = r.Code,
            Fr = r.Fr,
            Permission = r.Permission,
            Visibility = r.Visibility
        }).ToList();
    }

    private async void RightsSave_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (RightsRoleCombo.SelectedItem is not RoleComboVm role) return;
        if (RightsGrid.ItemsSource is not IEnumerable<RightVm> rows) return;
        foreach (var row in rows)
            await _permissions.SetRoleRightAsync(role.Id, row.ControlId, row.Permission, row.Visibility);
    });

    // ---- Paramètres ----------------------------------------------------------

    private async Task LoadSettingsAsync()
        => SettingsGrid.ItemsSource = await _settings.ListAsync(SettingSearch.Text);

    private void Setting_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsGrid.SelectedItem is not SettingDto setting) return;
        _setting = setting;
        SettingTypeBox.Text = setting.Type;
        SettingValueBox.Text = setting.Value?.ToString() ?? string.Empty;
        SettingStringBox.Text = setting.StringValue ?? string.Empty;
        SettingDescriptionBox.Text = setting.Description ?? string.Empty;
        SettingBorderPointCombo.SelectedItem = (SettingBorderPointCombo.ItemsSource as IEnumerable<BorderPointComboVm>)
            ?.FirstOrDefault(p => p.Id == setting.BorderPointId);
    }

    private void SettingNew_Click(object sender, RoutedEventArgs e)
    {
        _setting = null;
        SettingsGrid.SelectedItem = null;
        SettingTypeBox.Text = SettingValueBox.Text = SettingStringBox.Text = SettingDescriptionBox.Text = string.Empty;
        SettingBorderPointCombo.SelectedIndex = 0;
    }

    private async void SettingSearch_Click(object sender, RoutedEventArgs e) => await LoadSettingsAsync();

    private async void SettingSave_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        int? value = string.IsNullOrWhiteSpace(SettingValueBox.Text) ? null : ParseInt(SettingValueBox.Text, 0);
        var borderPointId = (SettingBorderPointCombo.SelectedItem as BorderPointComboVm)?.Id;
        await _settings.UpsertAsync(_setting?.Id, SettingTypeBox.Text, value, SettingStringBox.Text,
            SettingDescriptionBox.Text, borderPointId);
        await LoadSettingsAsync();
    });

    private async void SettingDelete_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_setting is null) return;
        await _settings.DeleteAsync(_setting.Id);
        SetNewSetting();
        await LoadSettingsAsync();
    });

    private void SetNewSetting() => SettingNew_Click(this, new RoutedEventArgs());

    // ---- Audit ---------------------------------------------------------------

    private async Task LoadAuditEventTypesAsync()
    {
        var types = await _audit.GetEventTypesAsync();
        var combo = new List<string> { Strings.Get("Common_All") };
        combo.AddRange(types);
        AuditEventCombo.ItemsSource = combo;
        AuditEventCombo.SelectedIndex = 0;
        await LoadAuditAsync();
        AuditGrid.ItemsSource = _auditRows;
    }

    private async Task LoadAuditAsync()
        => _auditRows = await _audit.SearchAsync(AuditUserBox.Text,
            AuditEventCombo.SelectedIndex <= 0 ? null : AuditEventCombo.SelectedItem as string,
            AuditFromPicker.SelectedDate, AuditToPicker.SelectedDate?.AddDays(1).AddSeconds(-1));

    private async void AuditSearch_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        await LoadAuditAsync();
        AuditGrid.ItemsSource = _auditRows;
    });

    private async void AuditExport_Click(object sender, RoutedEventArgs e)
    {
        if (_auditRows.Count == 0) await LoadAuditAsync();
        var dialog = new SaveFileDialog
        {
            FileName = $"audit-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Filter = "CSV|*.csv"
        };
        if (dialog.ShowDialog(this) != true) return;

        await GuardAsync(async () =>
        {
            var csv = await _audit.ExportCsvAsync(_auditRows);
            await File.WriteAllTextAsync(dialog.FileName, "\uFEFF" + csv);
            StatusText.Text = Strings.Format("Admin_Audit_ExportDone", dialog.FileName);
        }, showSaved: false);
    }

    // ---- Aides ---------------------------------------------------------------

    private async void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        switch (sender)
        {
            case TextBox box when box == LookupSearch: await LoadLookupItemsAsync(); break;
            case TextBox box when box == CountrySearch: CountrySearch_Click(sender, e); break;
            case TextBox box when box == CitySearch: await LoadCitiesAsync(); break;
            case TextBox box when box == UserSearch: await LoadUsersAsync(); break;
            case TextBox box when box == SettingSearch: await LoadSettingsAsync(); break;
            case TextBox box when box == AuditUserBox: await LoadAuditAsync(); break;
        }
    }

    /// <summary>Exécute une action d'administration en traduisant les erreurs métier.</summary>
    private async Task GuardAsync(Func<Task> action, bool showSaved = true)
    {
        try
        {
            await action();
            if (showSaved) StatusText.Text = Strings.Get("Admin_Saved");
        }
        catch (DomainValidationException ex)
        {
            MessageBox.Show(this, Strings.Get(ex.ErrorKey), Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Erreur d'administration");
            MessageBox.Show(this, ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var result) ? result : fallback;

    // ---- Petits objets de listes --------------------------------------------

    private sealed record LookupTypeVm(int Id, string Code, string Display);
    private sealed record CountryComboVm(int? Id, string Display);
    private sealed record ProvinceComboVm(int? Id, string Display);
    private sealed record RegionComboVm(string? Code, string Display);
    private sealed record RoleComboVm(short Id, string Display);
    private sealed record BorderPointComboVm(short? Id, string Display);
}
