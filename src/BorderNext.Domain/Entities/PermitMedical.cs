namespace BorderNext.Domain.Entities;

/// <summary>
/// Antécédents médicaux d'un permis de séjour (héritage : PermitMedicalHistory) :
/// maladies contractées, vaccinations, état courant et passage en centre de santé.
/// L'orthographe des colonnes héritées (« Desease ») est corrigée dans le schéma cible.
/// </summary>
public class PermitMedicalHistory
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public int PermitId { get; set; }
    public Permit? Permit { get; set; }

    /// <summary>Personne à charge concernée, le cas échéant (héritage : PermitDependantID).</summary>
    public int? PermitDependantId { get; set; }
    public PermitDependant? PermitDependant { get; set; }

    /// <summary>Maladie actuelle déclarée (héritage : CurrentIllness).</summary>
    public string? CurrentIllness { get; set; }

    /// <summary>Traitement en cours (héritage : CurrentMedication).</summary>
    public string? CurrentMedication { get; set; }

    // Maladies contractées (héritage : *Suffered).
    public bool MalariaSuffered { get; set; }
    public bool YellowFeverSuffered { get; set; }
    public bool TuberculosisSuffered { get; set; }
    public bool MeaslesSuffered { get; set; }
    public bool CholeraSuffered { get; set; }
    public bool TyphoidSuffered { get; set; }
    public bool JaundiceSuffered { get; set; }
    public bool ChronicCoughSuffered { get; set; }

    /// <summary>Autres antécédents déclarés (héritage : OtherSuffered).</summary>
    public string? OtherSuffered { get; set; }

    // Vaccinations (héritage : *Vaccinated).
    public bool YellowFeverVaccinated { get; set; }
    public bool MMRVaccinated { get; set; }
    public bool DiphtheriaVaccinated { get; set; }
    public bool PolioVaccinated { get; set; }

    /// <summary>Passage en centre de santé (héritage : HealthCentreVisited).</summary>
    public bool HealthCentreVisited { get; set; }

    /// <summary>Maladie chronique (héritage : ChronicDesease).</summary>
    public bool ChronicDisease { get; set; }

    /// <summary>Maladie infectieuse (héritage : InfectiousDesease).</summary>
    public bool InfectiousDisease { get; set; }

    /// <summary>Centre de santé public (héritage : isPublicHealthCentre).</summary>
    public bool IsPublicHealthCentre { get; set; }

    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
}
