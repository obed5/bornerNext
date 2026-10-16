namespace BorderNext.Application.Reporting;

/// <summary>
/// Localisation d'un document d'édition : les libellés du modèle sont des clés
/// i18n (les en-têtes préfixés « @ » sont des textes littéraux, ex. nom de
/// nationalité en colonne) ; la couche présentation fournit le traducteur.
/// </summary>
public static class ReportLocalizer
{
    /// <summary>Clé i18n marquant un libellé littéral (non traduit).</summary>
    public const string LiteralPrefix = "@";

    public static LocalizedReport Localize(ReportTemplate template, Func<string, string> translate,
        string? organisation = null, string? footer = null, bool landscape = false)
    {
        string Label(string key) => key.StartsWith(LiteralPrefix, StringComparison.Ordinal)
            ? key[LiteralPrefix.Length..]
            : translate(key);

        var filters = template.Filters
            .Select(f => $"{translate(f.LabelKey)} : {f.Value}")
            .ToList();
        var summary = template.Summary
            .Select(s => string.IsNullOrWhiteSpace(s.Value)
                ? translate(s.LabelKey)
                : $"{translate(s.LabelKey)} : {s.Value}")
            .ToList();
        var tables = template.Tables.Select(t => new ReportTable(
            string.IsNullOrWhiteSpace(t.TitleKey) ? null : translate(t.TitleKey!),
            t.Columns.Select(c => new ReportColumn(Label(c.HeaderKey), c.Numeric)).ToList(),
            t.Rows.Select(r => r.Select(v => v?.ToString() ?? string.Empty).ToArray()).ToList(),
            t.Totals?.Select(v => v?.ToString() ?? string.Empty).ToArray())).ToList();

        var generated = translate("RP_GeneratedLine")
            .Replace("{date}", template.GeneratedAt.ToString("dd/MM/yyyy HH:mm"), StringComparison.Ordinal)
            .Replace("{user}", template.GeneratedBy ?? "—", StringComparison.Ordinal);

        return new LocalizedReport(
            translate(template.TitleKey),
            string.IsNullOrWhiteSpace(template.SubtitleKey) ? null : translate(template.SubtitleKey!),
            filters, tables, summary, generated, organisation, footer, landscape);
    }
}
