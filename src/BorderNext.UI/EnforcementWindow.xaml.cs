using System.Windows;
using System.Windows.Controls;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>
/// Refus et sanctions (héritage : RefusalOfEntryPrintForm, RemovalOrderPrintForm,
/// PenaltyTicket) : refus d'entrée avec base légale, ordres de reconduite /
/// avis d'inadmissibilité, amendes avec encaissement.
/// </summary>
public partial class EnforcementWindow : Window
{
    private readonly EnforcementService _enforcement;
    private readonly ReferenceDataService _referenceData;

    private RefusalListItem? _refusal;
    private RemovalOrderListItem? _order;
    private PenaltyTicketListItem? _penalty;

    public EnforcementWindow(EnforcementService enforcement, ReferenceDataService referenceData)
    {
        _enforcement = enforcement;
        _referenceData = referenceData;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        try
        {
            var justifications = await _referenceData.GetItemsAsync("REFUSAL_JUSTIFICATION");
            var items = new List<IdVm> { new(null, "—") };
            items.AddRange(justifications.Select(j => new IdVm(j.Id, string.IsNullOrEmpty(j.Fr) ? j.En : j.Fr)));
            JustificationCombo.ItemsSource = items;

            await LoadRefusalsAsync();
            await LoadOrdersAsync();
            await LoadPenaltiesAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ---- Refus d'entrée ------------------------------------------------------

    private async void SearchRefusals_Click(object sender, RoutedEventArgs e) => await LoadRefusalsAsync();

    private async Task LoadRefusalsAsync()
    {
        var previousId = _refusal?.MovementId;
        var items = await _enforcement.GetRefusalsAsync(
            RefusalFromPicker.SelectedDate, RefusalToPicker.SelectedDate?.AddDays(1).AddSeconds(-1));
        RefusalsGrid.ItemsSource = items;
        if (previousId is { } id) RefusalsGrid.SelectedItem = items.FirstOrDefault(r => r.MovementId == id);
    }

    private void Refusal_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Le rechargement de la grille peut vider la sélection : on conserve alors
        // l'élément courant pour que les actions suivantes restent valides.
        if (RefusalsGrid.SelectedItem is not RefusalListItem refusal) return;
        _refusal = refusal;
        JustificationCombo.SelectedItem = (JustificationCombo.ItemsSource as IEnumerable<IdVm>)
            ?.FirstOrDefault(j => j.Display == _refusal.RefusalJustificationFr) ?? (JustificationCombo.ItemsSource as IEnumerable<IdVm>)?.FirstOrDefault();
    }

    private async void ApplyJustification_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_refusal is null) return;
        await _enforcement.SetRefusalJustificationAsync(_refusal.MovementId, (JustificationCombo.SelectedItem as IdVm)?.Id);
        StatusText.Text = Strings.Get("ENF_JustificationSaved");
        await LoadRefusalsAsync();
    });

    private async void CreateOrder_Click(object sender, RoutedEventArgs e) => await CreateRemovalOrderAsync("Order", "ENF_OrderCreated");

    private async void CreateNotice_Click(object sender, RoutedEventArgs e) => await CreateRemovalOrderAsync("Notice", "ENF_NoticeCreated");

    private async Task CreateRemovalOrderAsync(string typeCode, string successKey)
    {
        if (_refusal is null) return;
        var info = TextInputDialog.Ask(this, Strings.Get("ENF_Col_Info"),
            $"{_refusal.DocumentNumber} — {_refusal.HolderName}\n{Strings.Get("ENF_Col_Info")} :", null);
        // Le champ d'information est optionnel : annulation = pas de création.
        if (info is null) return;

        await GuardAsync(async () =>
        {
            await _enforcement.CreateRemovalOrderFromRefusalAsync(_refusal.MovementId, typeCode, info);
            StatusText.Text = Strings.Get(successKey);
            await LoadRefusalsAsync();
            await LoadOrdersAsync();
        });
    }

    private async void CreatePenalty_Click(object sender, RoutedEventArgs e)
    {
        if (_refusal is null) return;
        try
        {
            var reasons = await _referenceData.GetItemsAsync("PENALTY_TICKET_REASON");
            var dialog = new PenaltyCreateDialog(reasons
                .Select(r => new PenaltyReasonVm(r.Id, string.IsNullOrEmpty(r.Fr) ? r.En : r.Fr)).ToList()) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Request is null) return;

            await GuardAsync(async () =>
            {
                var ticketId = await _enforcement.CreatePenaltyTicketAsync(dialog.Request with { MovementId = _refusal.MovementId });
                StatusText.Text = Strings.Format("ENF_Penalty_Created", ticketId);
                await LoadRefusalsAsync();
                await LoadPenaltiesAsync();
            });
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ---- Ordres de reconduite ------------------------------------------------

    private async Task LoadOrdersAsync()
    {
        var previousId = _order?.Id;
        var items = await _enforcement.GetRemovalOrdersAsync(OrderShowRevoked.IsChecked == true);
        OrdersGrid.ItemsSource = items;
        if (previousId is { } id) OrdersGrid.SelectedItem = items.FirstOrDefault(o => o.Id == id);
    }

    private async void OrderFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadOrdersAsync();
    }

    private void Order_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not RemovalOrderListItem order) return;
        _order = order;
        OrderRevokeBtn.Content = Strings.Get(_order.Revoke ? "Common_Restore" : "Common_Revoke");
    }

    private async void ToggleOrderRevoked_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_order is null) return;
        await _enforcement.SetRemovalOrderRevokedAsync(_order.Id, !_order.Revoke);
        await LoadOrdersAsync();
        await LoadRefusalsAsync();
    });

    // ---- Amendes -------------------------------------------------------------

    private async Task LoadPenaltiesAsync()
    {
        var previousId = _penalty?.Id;
        var items = await _enforcement.GetPenaltyTicketsAsync(PenaltyShowRevoked.IsChecked == true, PenaltySearch.Text);
        PenaltiesGrid.ItemsSource = items;
        if (previousId is { } id) PenaltiesGrid.SelectedItem = items.FirstOrDefault(t => t.Id == id);
    }

    private async void SearchPenalties_Click(object sender, RoutedEventArgs e) => await LoadPenaltiesAsync();

    private async void PenaltyFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadPenaltiesAsync();
    }

    private void Penalty_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PenaltiesGrid.SelectedItem is not PenaltyTicketListItem penalty) return;
        _penalty = penalty;
        PenaltyRevokeBtn.Content = Strings.Get(_penalty.Revoke ? "ENF_RestorePenalty" : "ENF_CancelPenalty");
    }

    private async void Pay_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_penalty is null || _penalty.Paid) return;
        var receipt = TextInputDialog.Ask(this, Strings.Get("ENF_Pay_Title"),
            $"{_penalty.Number} — {_penalty.Amount:N2} {_penalty.CurrencyCode}\n{Strings.Get("ENF_Pay_Info")}", string.Empty);
        if (receipt is null) return;

        await _enforcement.SetPaidAsync(_penalty.Id, true, receipt);
        StatusText.Text = Strings.Get("ENF_Paid");
        await LoadPenaltiesAsync();
    });

    private async void TogglePenaltyRevoked_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_penalty is null) return;
        await _enforcement.SetPenaltyRevokedAsync(_penalty.Id, !_penalty.Revoke);
        await LoadPenaltiesAsync();
        await LoadRefusalsAsync();
    });

    // ---- Aides ---------------------------------------------------------------

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

    private static void ShowError(Exception ex)
    {
        Serilog.Log.Error(ex, "Erreur de l'écran refus et sanctions");
        MessageBox.Show(ex.Message, Strings.Get("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record IdVm(int? Id, string Display) { public override string ToString() => Display; }
}
