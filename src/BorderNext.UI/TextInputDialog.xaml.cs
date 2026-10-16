using System.Windows;

namespace BorderNext.UI;

/// <summary>Saisie d'une valeur texte (numéro de reçu, information d'ordre de reconduite…).</summary>
public partial class TextInputDialog : Window
{
    /// <summary>Valeur saisie (null si annulation).</summary>
    public string? Value { get; private set; }

    public TextInputDialog(string title, string info, string? initialValue)
    {
        InitializeComponent();
        Title = title;
        InfoText.Text = info;
        ValueBox.Text = initialValue ?? string.Empty;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    /// <summary>Boîte de saisie : retourne la valeur, ou null si annulée.</summary>
    public static string? Ask(Window owner, string title, string info, string? initialValue)
    {
        var dialog = new TextInputDialog(title, info, initialValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = ValueBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
