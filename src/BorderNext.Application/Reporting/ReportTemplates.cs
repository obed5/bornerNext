namespace BorderNext.Application.Reporting;

/// <summary>
/// Modèles de documents d'édition : le service d'édition produit des
/// <see cref="ReportTemplate"/> (libellés par clés i18n, valeurs déjà mises en
/// forme), que la couche présentation localise en <see cref="LocalizedReport"/>
/// avant remise au moteur PDF. Cette séparation garde l'application indépendante
/// de l'interface et du moteur d'impression.
/// </summary>
public sealed record ReportColumnTemplate(string HeaderKey, bool Numeric = false);

/// <summary>Tableau d'un document : en-têtes par clé i18n, lignes de valeurs déjà
/// mises en forme, ligne de totaux optionnelle.</summary>
public sealed record ReportTableTemplate(
    string? TitleKey,
    IReadOnlyList<ReportColumnTemplate> Columns,
    IReadOnlyList<object?[]> Rows,
    object?[]? Totals = null);

/// <summary>Critère d'édition affiché en tête de document (libellé + valeur).</summary>
public sealed record ReportFilterLine(string LabelKey, string Value);

/// <summary>Ligne de synthèse : libellé traduit seul (valeur nulle) ou libellé + valeur.</summary>
public sealed record ReportSummaryLine(string LabelKey, string? Value = null);

/// <summary>Document prêt à imprimer (hors libellés traduits).</summary>
public sealed record ReportTemplate(
    string ReportId,
    string TitleKey,
    string? SubtitleKey,
    IReadOnlyList<ReportFilterLine> Filters,
    IReadOnlyList<ReportTableTemplate> Tables,
    IReadOnlyList<ReportSummaryLine> Summary,
    DateTime GeneratedAt,
    string? GeneratedBy);

/// <summary>Colonne localisée d'un tableau imprimable.</summary>
public sealed record ReportColumn(string Header, bool Numeric);

/// <summary>Tableau localisé : en-têtes et cellules en texte prêt à imprimer.</summary>
public sealed record ReportTable(string? Title, IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<string?[]> Rows, string?[]? Totals);

/// <summary>Document entièrement localisé, remis au moteur d'édition PDF.</summary>
public sealed record LocalizedReport(
    string Title,
    string? Subtitle,
    IReadOnlyList<string> FilterLines,
    IReadOnlyList<ReportTable> Tables,
    IReadOnlyList<string> SummaryLines,
    string GeneratedLine,
    string? Organisation,
    string? FooterText,
    bool Landscape = false);

/// <summary>Paramètres communs des éditions (les rapports n'utilisent que les champs
/// pertinents, déclarés par le catalogue).</summary>
public sealed record ReportParameters(
    DateTime? From = null,
    DateTime? To = null,
    short? BorderPointId = null,
    string? BorderPointName = null,
    string? DirectionCode = null,
    int? CountryId = null,
    int? DocumentTypeId = null,
    int? VisaTypeId = null,
    string? FlightNumber = null,
    string? Inspector = null,
    string? DocumentNumber = null,
    string? Surname = null,
    string? GivenName = null,
    bool IncludeChildren = false,
    int? PermitTypeId = null,
    string? EffectiveFrom = null);

/// <summary>Champs de paramètre qu'un rapport exploite (affichage des contrôles d'écran).</summary>
[Flags]
public enum ReportFields
{
    None = 0,
    Period = 1,
    BorderPoint = 2,
    Direction = 4,
    Country = 8,
    DocumentType = 16,
    VisaType = 32,
    Flight = 64,
    Inspector = 128,
    Person = 256,          // nom, prénom, n° de document (impressions individuelles)
    Children = 512,        // inclure les enfants rattachés
    PermitType = 1024,
}
