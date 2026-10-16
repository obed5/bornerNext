namespace BorderNext.Application.Abstractions;

/// <summary>
/// Moteur d'édition : produit le PDF d'un document localisé (éditions et fiches).
/// L'implémentation vit dans l'infrastructure (QuestPDF) ; l'application ne
/// connaît que cette abstraction.
/// </summary>
public interface IReportWriter
{
    /// <summary>Rend le document au format PDF.</summary>
    byte[] RenderPdf(Reporting.LocalizedReport report);
}
