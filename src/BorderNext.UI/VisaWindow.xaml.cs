using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Gestion des visas (héritage : émission de visa + VisaChildren) :
/// document porteur, détails du visa, enfants rattachés, liste des visas du
/// document.
/// </summary>
public partial class VisaWindow : Window
{
    private readonly VisaService _visas;
    private readonly DocumentService _documents;
    private readonly ReferenceDataService _referenceData;
    private readonly GeoService _geo;

    private int _documentId;      // 0 = visa sans document porteur
    private int _visaId;          // 0 = nouveau visa

    public VisaWindow(VisaService visas, DocumentService documents, ReferenceDataService referenceData, GeoService geo)
    {
        _visas = visas;
        _documents = documents;
        _referenceData = referenceData;
        _geo = geo;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        try
        {
            await FillLookupComboAsync(TypeCombo, "VISA_TYPE");
            await FillLookupComboAsync(EntryCombo, "VISA_ENTRY");
            ResetToNewVisa();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task FillLookupComboAsync(ComboBox combo, string typeCode)
    {
        var items = await _referenceData.GetItemsAsync(typeCode);
        combo.ItemsSource = items.Select(i => new LookupVm(i.Id, string.IsNullOrEmpty(i.Fr) ? i.En : i.Fr)).ToList();
    }

    // ---- Document porteur ----------------------------------------------------

    private async void SearchDocument_Click(object sender, RoutedEventArgs e)
    {
        var search = new DocumentSearchWindow(_documents, _geo) { Owner = this };
        if (search.ShowDialog() != true || search.SelectedDocumentId == 0) return;

        try
        {
            await LoadDocumentAsync(search.SelectedDocumentId);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task LoadDocumentAsync(int documentId)
    {
        var detail = await _documents.GetAsync(documentId);
        if (detail is null) return;

        _documentId = documentId;
        HolderText.Text = $"{detail.Surname} {detail.GivenName}".Trim();
        DocumentText.Text = $"{detail.DocumentTypeFr} — {detail.DocumentNumber}";
        NationalityText.Text = detail.CitizenshipFr ?? string.Empty;
        ExpiryText.Text = detail.ExpireDate is { } expire ? Strings.Get("PAL_ExpireDate") + $" : {expire:dd/MM/yyyy}" : string.Empty;
        DocumentMode.Text = Strings.Format("PAL_Mode_Existing", detail.DocumentNumber);
        DocumentMode.Foreground = System.Windows.Media.Brushes.SeaGreen;

        await RefreshVisasAsync();
    }

    private async Task RefreshVisasAsync()
    {
        if (_documentId == 0) { VisasGrid.ItemsSource = null; return; }
        VisasGrid.ItemsSource = await _visas.ListByDocumentAsync(_documentId, includeRevoked: true);
    }

    // ---- Formulaire visa -----------------------------------------------------

    private void ResetToNewVisa()
    {
        _visaId = 0;
        NumberBox.Text = string.Empty;
        TypeCombo.SelectedIndex = 0;
        EntryCombo.SelectedIndex = 0;
        IssuePicker.SelectedDate = DateTime.Today;
        ExpirePicker.SelectedDate = DateTime.Today.AddMonths(3);
        DurationBox.Text = "90";
        ReferenceBox.Text = string.Empty;
        ReceiptBox.Text = string.Empty;
        VoucherBox.Text = string.Empty;
        EmployerBox.Text = string.Empty;
        InvitedByBox.Text = string.Empty;
        OccupationBox.Text = string.Empty;
        AddressBox.Text = string.Empty;
        ContactBox.Text = string.Empty;
        NoteBox.Text = string.Empty;
        ChildrenGrid.ItemsSource = null;
        RevokeBtn.Content = Strings.Get("Common_Revoke");
        StatusText.Text = string.Empty;
    }

    private void New_Click(object sender, RoutedEventArgs e) => ResetToNewVisa();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = new VisaRequest(
                (TypeCombo.SelectedItem as LookupVm)?.Id ?? 0,
                (EntryCombo.SelectedItem as LookupVm)?.Id,
                IssuePicker.SelectedDate, ExpirePicker.SelectedDate,
                ParseShort(DurationBox.Text), _documentId == 0 ? null : _documentId,
                NumberBox.Text, ReferenceBox.Text, ReceiptBox.Text, AddressBox.Text, ContactBox.Text,
                EmployerBox.Text, InvitedByBox.Text, null, null, null, OccupationBox.Text, null,
                NoteBox.Text, VoucherBox.Text, null, null, null);

            if (_visaId == 0)
                _visaId = await _visas.CreateAsync(request);
            else
                await _visas.UpdateAsync(_visaId, request);

            StatusText.Text = Strings.Get("Visa_Saved") + $" ({NumberBox.Text})";
            await RefreshVisasAsync();
            var saved = await _visas.GetAsync(_visaId);
            if (saved is not null) NumberBox.Text = saved.Number ?? string.Empty;
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

    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (_visaId == 0) return;
        try
        {
            var revoked = await _visas.IsRevokedAsync(_visaId);
            await _visas.SetRevokedAsync(_visaId, !revoked);
            RevokeBtn.Content = Strings.Get(!revoked ? "Common_Restore" : "Common_Revoke");
            await RefreshVisasAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void Visas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VisasGrid.SelectedItem is not VisaListItem visa) return;
        _visaId = visa.Id;
        var request = await _visas.GetAsync(visa.Id);
        if (request is null) return;

        NumberBox.Text = request.Number ?? string.Empty;
        SelectById(TypeCombo, request.VisaTypeId);
        SelectById(EntryCombo, request.VisaEntryId);
        IssuePicker.SelectedDate = request.IssueDate;
        ExpirePicker.SelectedDate = request.ExpireDate;
        DurationBox.Text = request.Duration?.ToString() ?? string.Empty;
        ReferenceBox.Text = request.ReferenceNumber ?? string.Empty;
        ReceiptBox.Text = request.ReceiptNumber ?? string.Empty;
        VoucherBox.Text = request.VoucherNumber ?? string.Empty;
        EmployerBox.Text = request.Employer ?? string.Empty;
        InvitedByBox.Text = request.InvitedBy ?? string.Empty;
        OccupationBox.Text = request.Occupation ?? string.Empty;
        AddressBox.Text = request.Address ?? string.Empty;
        ContactBox.Text = request.ContactNumber ?? string.Empty;
        NoteBox.Text = request.Note ?? string.Empty;
        RevokeBtn.Content = Strings.Get(visa.Revoke ? "Common_Restore" : "Common_Revoke");

        ChildrenGrid.ItemsSource = await _visas.GetChildrenAsync(visa.Id);
    }

    // ---- Enfants -------------------------------------------------------------

    private async void AddChild_Click(object sender, RoutedEventArgs e)
    {
        if (_visaId == 0)
        {
            MessageBox.Show(this, Strings.Get("PAL_Error_DocumentNotFound"), Strings.Get("Common_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _visas.AddChildAsync(_visaId, new VisaChildDto(0, ChildSurnameBox.Text, ChildGivenNameBox.Text,
                ChildBirthPicker.SelectedDate, null, null, null, ChildDocumentBox.Text));
            ChildSurnameBox.Text = string.Empty;
            ChildGivenNameBox.Text = string.Empty;
            ChildBirthPicker.SelectedDate = null;
            ChildDocumentBox.Text = string.Empty;
            ChildrenGrid.ItemsSource = await _visas.GetChildrenAsync(_visaId);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void RemoveChild_Click(object sender, RoutedEventArgs e)
    {
        if (_visaId == 0 || ChildrenGrid.SelectedItem is not VisaChildDto child) return;
        try
        {
            await _visas.RemoveChildAsync(child.Id);
            ChildrenGrid.ItemsSource = await _visas.GetChildrenAsync(_visaId);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { Save_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchDocument_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { New_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) Close();
    }

    private static void SelectById(ComboBox combo, int? id)
    {
        if (combo.ItemsSource is not System.Collections.IEnumerable items) return;
        foreach (var item in items)
            if (item is LookupVm l && l.Id == id) { combo.SelectedItem = item; return; }
    }

    private static short? ParseShort(string? value) => short.TryParse(value, out var result) ? result : null;

    private static void ShowError(Exception ex)
    {
        Serilog.Log.Error(ex, "Erreur de l'écran visas");
        MessageBox.Show(ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record LookupVm(int Id, string Display)
    {
        public override string ToString() => Display;
    }
}
