using System.Globalization;
using System.Windows;
using BorderNext.Application.Services;
using BorderNext.UI.Resources;

namespace BorderNext.UI;

/// <summary>Dialogue de création d'une amende (héritage : PenaltyTicket).</summary>
public partial class PenaltyCreateDialog : Window
{
    /// <summary>Demande prête à créer (MovementId renseigné par l'appelant).</summary>
    public PenaltyTicketRequest? Request { get; private set; }

    public PenaltyCreateDialog(IReadOnlyList<PenaltyReasonVm> reasons, decimal defaultAmount = 100m,
        string defaultCurrency = EnforcementService.DefaultCurrency)
    {
        InitializeComponent();
        ReasonCombo.ItemsSource = reasons;
        if (reasons.Count > 0) ReasonCombo.SelectedIndex = 0;
        AmountBox.Text = defaultAmount.ToString("0.00", CultureInfo.CurrentCulture);
        CurrencyBox.Text = defaultCurrency;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ReasonCombo.SelectedItem is not PenaltyReasonVm reason)
        {
            ShowError(Strings.Get("ENF_Error_ReasonRequired"));
            return;
        }
        if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            ShowError(Strings.Get("ENF_Error_AmountInvalid"));
            return;
        }
        if (DurationBox.Text.Length > 0 && (!int.TryParse(DurationBox.Text, out var days) || days < 0))
        {
            ShowError(Strings.Get("ENF_Error_DurationInvalid"));
            return;
        }

        Request = new PenaltyTicketRequest(0, reason.Id, amount,
            int.TryParse(DurationBox.Text, out var duration) ? duration : null,
            NumberBox.Text, CurrencyBox.Text);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}

/// <summary>Entrée de liste pour les motifs d'amende.</summary>
public sealed record PenaltyReasonVm(int Id, string Display)
{
    public override string ToString() => Display;
}
