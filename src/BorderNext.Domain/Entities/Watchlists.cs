namespace BorderNext.Domain.Entities;

// ---------------------------------------------------------------------------
// Listes d'alerte (héritage : PersonsStopList 8 532 lignes, DocumentsStopList,
// StopListFingerprints / StopListFingerTemplates) et personnes douteuses
// (DoubtablePersons, 30 lignes, workflow superviseur).
//
// Fidélité : colonnes de l'héritage conservées telles quelles (types .NET
// natifs, ID entiers BorderNext + identifiant hérité conservé pour la
// migration du Lot 9). Les colonnes d'audit BorderNext sont signalées.
// ---------------------------------------------------------------------------

/// <summary>Personne sur liste d'alerte (héritage : <c>PersonsStopList</c>).</summary>
public class PersonStopList
{
    public int Id { get; set; }

    /// <summary>Identifiant hérité (varchar(20)) — Lot 9.</summary>
    public string? LegacyId { get; set; }

    /// <summary>Ancien identifiant hérité (varchar(25)) — Lot 9.</summary>
    public string? LegacyOldId { get; set; }

    public string? Surname { get; set; }
    public string? GivenName { get; set; }
    public string? FathersName { get; set; }

    /// <summary>Variantes orthographiques (héritage : SurnameAlt, GivenNameAlt, FathersNameAlt)
    /// testées en second rang par le moteur de correspondance.</summary>
    public string? SurnameAlt { get; set; }
    public string? GivenNameAlt { get; set; }
    public string? FathersNameAlt { get; set; }

    public DateTime? DateOfBirth { get; set; }
    public int? SexId { get; set; }

    /// <summary>Nationalité (héritage : Citizenship smallint → lCountries.ID ; élargi en int
    /// pour la clé des nomenclatures BorderNext).</summary>
    public int? CitizenshipId { get; set; }
    public Country? Citizenship { get; set; }

    /// <summary>Autorité ayant inscrit la personne (héritage : WhoSetUp, chaîne libre).</summary>
    public string? WhoSetUp { get; set; }

    public DateTime? SetUpDate { get; set; }

    /// <summary>Date d'annulation (renseignée ⇒ l'entrée ne participe plus au matching).</summary>
    public DateTime? CancelDate { get; set; }

    public int? StopListSourceId { get; set; }
    public Lookup? StopListSource { get; set; }

    public string? SourceDocument { get; set; }
    public string? Info { get; set; }
    public byte[]? Photo { get; set; }

    public int? DocumentTypeId { get; set; }
    public string? DocumentNumber { get; set; }

    /// <summary>Numéro personnel (héritage : PersonalNumber, critère n° 1 du moteur).</summary>
    public string? PersonalNumber { get; set; }

    /// <summary>Action à mener, texte libre (héritage : ActionRequired).</summary>
    public string? ActionRequired { get; set; }

    /// <summary>Action de liste d'alerte (héritage : MatchInStopListActionID → lMatchInStopListActions ;
    /// codes ZP100/ZP200, tri de priorité du moteur par SortOrder).</summary>
    public int? MatchInStopListActionId { get; set; }
    public Lookup? MatchInStopListAction { get; set; }

    public DateTime UpdateDate { get; set; }
    public string? LastUpdateUser { get; set; }

    /// <summary>Horodatage hérité (colonne Timestamp datetime).</summary>
    public DateTime? TimeStamp { get; set; }

    public int? CancelStopListSourceId { get; set; }
    public Lookup? CancelStopListSource { get; set; }
    public string? CancelSourceDocument { get; set; }

    /// <summary>Levée d'alerte autorisée par un superviseur (héritage : IsAuthorized).</summary>
    public bool? IsAuthorized { get; set; }

    public ICollection<StopListFingerprint> Fingerprints { get; set; } = new List<StopListFingerprint>();

    /// <summary>Entrée active : non annulée.</summary>
    public bool IsActive => CancelDate == null;

    /// <summary>Nationalité affichable (non mappé) : nom français, sinon première culture, sinon identifiant.</summary>
    public string? CitizenshipName => Citizenship?.Cultures.FirstOrDefault(c => c.Culture == "fr")?.Name
                                      ?? Citizenship?.Cultures.FirstOrDefault()?.Name
                                      ?? CitizenshipId?.ToString();
}

/// <summary>Document sur liste d'alerte (héritage : <c>DocumentsStopList</c>).</summary>
public class DocumentStopList
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }
    public string? LegacyOldId { get; set; }

    public int? DocumentTypeId { get; set; }
    public string? DocumentNumber { get; set; }
    public string? PersonalNumber { get; set; }

    /// <summary>Pays de délivrance (héritage : CountryID).</summary>
    public int? CountryId { get; set; }
    public Country? Country { get; set; }

    public string? Surname { get; set; }
    public string? GivenName { get; set; }
    public string? FathersName { get; set; }

    public DateTime? DateOfBirth { get; set; }
    public int? SexId { get; set; }

    public DateTime? SetUpDate { get; set; }
    public DateTime? CancelDate { get; set; }

    public int? StopListSourceId { get; set; }
    public Lookup? StopListSource { get; set; }

    public string? SourceDocument { get; set; }
    public string? Info { get; set; }
    public string? ActionRequired { get; set; }

    public int? MatchInStopListActionId { get; set; }
    public Lookup? MatchInStopListAction { get; set; }

    public DateTime UpdateDate { get; set; }
    public string? LastUpdateUser { get; set; }
    public DateTime? TimeStamp { get; set; }

    public int? CancelStopListSourceId { get; set; }
    public Lookup? CancelStopListSource { get; set; }
    public string? CancelSourceDocument { get; set; }

    /// <summary>Entrée active : non annulée.</summary>
    public bool IsActive => CancelDate == null;

    /// <summary>Pays affichable (non mappé) : nom français, sinon première culture, sinon identifiant.</summary>
    public string? CountryName => Country?.Cultures.FirstOrDefault(c => c.Culture == "fr")?.Name
                                  ?? Country?.Cultures.FirstOrDefault()?.Name
                                  ?? CountryId?.ToString();
}

/// <summary>Empreinte rattachée à une entrée de liste d'alerte personnes
/// (héritage : <c>StopListFingerprints</c>, clé composite StopListID + FingerNumber).
/// L'index par HashName de <c>StopListFingerTemplates</c> (optimisation de la
/// recherche héritée) est remplacé par un index sur la clé composite.</summary>
public class StopListFingerprint
{
    public int PersonStopListId { get; set; }
    public PersonStopList? PersonStopList { get; set; }

    /// <summary>1..10 (héritage : FingerNumber).</summary>
    public short FingerNumber { get; set; }

    /// <summary>Image 8 bits de l'empreinte (héritage : Fingerprint varbinary).</summary>
    public byte[]? FingerprintImage { get; set; }

    /// <summary>Gabarit biométrique (héritage : Template varbinary).</summary>
    public byte[]? Template { get; set; }
}

/// <summary>États du workflow « personne douteuse » (le client précédent observe
/// les statuts 1/2/3 avec note de superviseur et annulation d'alarme ; les
/// dossiers repris arrivent déjà traités — statut 2).</summary>
public static class DoubtfulWorkflowStatus
{
    /// <summary>En attente de la seconde inspection (superviseur).</summary>
    public const short Pending = 1;

    /// <summary>Traité : le superviseur a consigné la décision (note + action).</summary>
    public const short Processed = 2;

    /// <summary>Annulé : le superviseur a écarté le doute sans action.</summary>
    public const short Cancelled = 3;
}

/// <summary>Personne douteuse (héritage : <c>DoubtablePersons</c>).</summary>
public class DoubtablePerson
{
    public int Id { get; set; }

    /// <summary>Identifiant hérité (= PassportRegistrationID legacy, varchar(20)) — Lot 9.</summary>
    public string? LegacyId { get; set; }

    /// <summary>Mouvement ayant déclenché le doute (héritage : PassportRegistrationID).</summary>
    public int MovementId { get; set; }
    public Movement? Movement { get; set; }

    /// <summary>Photo prise au guichet (héritage : PhotoJustScanned).</summary>
    public byte[]? PhotoJustScanned { get; set; }

    /// <summary>Superviseur de la seconde inspection (héritage : Supervisor, nom affiché).
    /// La requête héritée l'expose en tant que SecondInspector.</summary>
    public string? Supervisor { get; set; }

    /// <summary>Compte du superviseur (ajout BorderNext, pour la file « à traiter »).</summary>
    public string? SupervisorUserId { get; set; }

    /// <summary>Date de la seconde inspection (héritage : SupervisorRegistrationTime).</summary>
    public DateTime? SupervisorRegistrationTime { get; set; }

    /// <summary>Numéro du poste du superviseur (héritage : SupervisorPCNumber).</summary>
    public string? SupervisorPcNumber { get; set; }

    /// <summary>Action décidée (héritage : Status ; la requête héritée joint
    /// <c>DoubtablePersons.Status = lActions.ID</c> et l'expose en DoutablePersonActionCode :
    /// 1 = Permit, 2 = Refuse, 3 = Detain, 4 = SendToMe, 11 = RAPATRIER).</summary>
    public int? Status { get; set; }

    /// <summary>Action effectuée (colonne ajoutée par les scripts 2023 — héritage API
    /// PerformActionID ; même nomenclature que Status).</summary>
    public int? PerformedActionId { get; set; }

    /// <summary>État du workflow (ajout BorderNext reprenant les statuts 1/2/3 du système précédent).</summary>
    public short WorkflowStatus { get; set; } = DoubtfulWorkflowStatus.Pending;

    /// <summary>Entrée de liste d'alerte personnes à l'origine du doute (héritage : PersonStopID).</summary>
    public int? PersonStopListId { get; set; }
    public PersonStopList? PersonStopList { get; set; }

    /// <summary>Entrée de liste d'alerte documents à l'origine du doute (héritage : DocumentStopID).</summary>
    public int? DocumentStopListId { get; set; }
    public DocumentStopList? DocumentStopList { get; set; }

    /// <summary>Note de l'inspecteur (héritage : InspectorsNote).</summary>
    public string? InspectorNote { get; set; }

    /// <summary>Note du superviseur (héritage : SupervisorsNote).</summary>
    public string? SupervisorNote { get; set; }

    /// <summary>Motif de contrôle (héritage : CheckingReason → rpt.lCheckingReason.ExternalID ;
    /// codes A_FP, D_NOT, EP, FD, FDP, FIND, MIND, FPR, S_VIS, SSC — le motif SSC
    /// « envoyé en seconde inspection » est celui du workflow).</summary>
    public int? CheckingReasonId { get; set; }
    public Lookup? CheckingReason { get; set; }

    // -- Refus éventuel consigné par le superviseur (héritage : Refusal…) -----
    public string? RefusalOfficerName { get; set; }
    public string? RefusalOfficerRank { get; set; }
    public string? RefusalDocNumber { get; set; }
    public int? RefusalCountryId { get; set; }
    public Country? RefusalCountry { get; set; }
    public int? RefusalJustificationId { get; set; }
    public Lookup? RefusalJustification { get; set; }
    public string? RefusalJustificationOther { get; set; }
    public DateTime? RefusalCreateDate { get; set; }

    // -- Ajouts BorderNext (traçabilité de la création) -----------------------
    public DateTime CreateDate { get; set; }
    public string? CreatedByUserId { get; set; }
    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }
}
