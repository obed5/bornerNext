namespace BorderNext.Domain.Entities;

/// <summary>
/// Message de liste de passagers reçu d'un système aérien (héritage : APIMessage).
/// La liste des passagers et les fichiers joints (EDIFACT brut, XML) y sont rattachés.
/// </summary>
public class ApisMessage
{
    public int Id { get; set; }

    public string? FlightNumber { get; set; }

    /// <summary>Lieu de départ déclaré (héritage : DepartureLocation).</summary>
    public string? DepartureLocation { get; set; }

    /// <summary>Lieu d'arrivée déclaré (héritage : ArrivalLocation).</summary>
    public string? ArrivalLocation { get; set; }

    public DateTime? DepartureDate { get; set; }
    public DateTime? ArrivalDate { get; set; }

    /// <summary>Nombre de passagers déclaré (héritage : PassengerCount).</summary>
    public int PassengerCount { get; set; }

    /// <summary>Statut de traitement (héritage : Status int, valeur initiale 0).</summary>
    public int Status { get; set; } = ApisMessageStatus.Received;

    /// <summary>Format du message d'origine.</summary>
    public string SourceFormat { get; set; } = ApisSourceFormats.Edifact;

    /// <summary>Référence de contrôle de l'échange d'origine (UNB/UNZ).</summary>
    public string? ControlReference { get; set; }

    /// <summary>Expéditeur déclaré dans l'enveloppe UNB.</summary>
    public string? SenderId { get; set; }

    /// <summary>Destinataire déclaré dans l'enveloppe UNB.</summary>
    public string? RecipientId { get; set; }

    /// <summary>Transporteur déclaré (segment TDT).</summary>
    public string? CarrierName { get; set; }

    /// <summary>Point frontière concerné (renseigné d'après le poste du message ou de l'importateur).</summary>
    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }

    public DateTime CreatedDate { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    public ICollection<ApisPassenger> Passengers { get; set; } = new List<ApisPassenger>();
    public ICollection<ApisMessageFile> Files { get; set; } = new List<ApisMessageFile>();

    /// <summary>Libellé de vol composé (ex. « AF100 FIH - CDG ») pour les listes.</summary>
    public string FlightLabel
    {
        get
        {
            var route = DepartureLocation switch
            {
                null or "" => ArrivalLocation,
                _ when ArrivalLocation is null or "" => DepartureLocation,
                _ => $"{DepartureLocation} - {ArrivalLocation}",
            };
            return string.IsNullOrEmpty(FlightNumber) ? route ?? string.Empty : $"{FlightNumber} {route}".Trim();
        }
    }
}

/// <summary>Statuts d'un message APIS — la table d'origine n'était pas documentée par
/// des données ; la valeur initiale héritée est 0.</summary>
public static class ApisMessageStatus
{
    public const int Received = 0;     // reçu / importé
    public const int Reconciled = 1;   // rapproché des dossiers locaux
    public const int Alerted = 2;      // au moins un passager douteux
    public const int Exported = 3;     // exporté vers un autre site
}

public static class ApisSourceFormats
{
    public const string Edifact = "EDIFACT";
    public const string Xml = "XML";
    public const string Manual = "MANUAL";
}

/// <summary>Fichier rattaché à un message APIS (héritage : APIMessageFiles).
/// Le contenu est conservé sur disque, le registre en base.</summary>
public class ApisMessageFile
{
    public int Id { get; set; }

    public int ApisMessageId { get; set; }
    public ApisMessage? ApisMessage { get; set; }

    /// <summary>Chemin du fichier conservé (héritage : FilePath varchar(250)).</summary>
    public string FilePath { get; set; } = string.Empty;

    public string? FileName { get; set; }
    public long? SizeBytes { get; set; }

    /// <summary>Empreinte SHA-256 du contenu (détection de doublons d'import).</summary>
    public string? ContentHash { get; set; }

    public DateTime CreatedDate { get; set; }
}

/// <summary>
/// Passager (ou membre d'équipage) d'un message APIS (héritage : APIPassenger).
/// Les colonnes de rapprochement locales sont un ajout du schéma cible.
/// </summary>
public class ApisPassenger
{
    public int Id { get; set; }

    public int ApisMessageId { get; set; }
    public ApisMessage? ApisMessage { get; set; }

    /// <summary>Identifiant du document rapproché localement (héritage : PassportRegistrationID varchar(50)).</summary>
    public string? PassportRegistrationId { get; set; }

    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? CountryOfIssue { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? ExpireDate { get; set; }

    public string? Surname { get; set; }
    public string? GivenName { get; set; }
    public DateTime? DateOfBirth { get; set; }

    /// <summary>Nationalité déclarée (héritage : Citizenship, code pays).</summary>
    public string? Citizenship { get; set; }

    public string? Gender { get; set; }
    public string? CountryOfResidence { get; set; }

    /// <summary>Statut de traitement (héritage : Status int, valeur initiale 0).</summary>
    public int Status { get; set; } = ApisPassengerStatus.Received;

    /// <summary>Motif de contrôle (héritage : CheckingReason — codes CHECKING_REASON).</summary>
    public string? CheckingReason { get; set; }

    /// <summary>Vrai pour un passager, faux pour un membre d'équipage (héritage : isPassenger).</summary>
    public bool IsPassenger { get; set; } = true;

    /// <summary>Document de voyage local rapproché (ajout du schéma cible).</summary>
    public int? MatchedDocumentId { get; set; }
    public TravelDocument? MatchedDocument { get; set; }

    /// <summary>Mouvement local rapproché (ajout du schéma cible).</summary>
    public int? MatchedMovementId { get; set; }
    public Movement? MatchedMovement { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    public ICollection<ApisDoubtfulPerson> DoubtfulRecords { get; set; } = new List<ApisDoubtfulPerson>();

    public string FullName => string.Join(' ', new[] { Surname, GivenName }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

/// <summary>Statuts d'un passager APIS — valeur initiale héritée 0.</summary>
public static class ApisPassengerStatus
{
    public const int Received = 0;    // reçu
    public const int Reconciled = 1;  // rapproché d'un dossier local
    public const int Doubtful = 2;    // contrôle renforcé (liste d'alerte / connecteur)
    public const int Rejected = 3;    // écarté (doublon, données invalides)
}

/// <summary>
/// Signalement d'un passager APIS douteux (héritage : APIDoubtablePersons) : motif de
/// contrôle, action de l'agent, note du superviseur — même cycle que les personnes
/// douteuses hors API.
/// </summary>
public class ApisDoubtfulPerson
{
    public int Id { get; set; }

    public int ApisPassengerId { get; set; }
    public ApisPassenger? ApisPassenger { get; set; }

    /// <summary>Motif de contrôle (code CHECKING_REASON, héritage : CheckingReason).</summary>
    public string? CheckingReason { get; set; }

    /// <summary>Action effectuée (héritage : PerformActionID → lActions).</summary>
    public int? PerformActionId { get; set; }
    public Lookup? PerformAction { get; set; }

    public string? SupervisorsNote { get; set; }
    public string? Supervisor { get; set; }
    public string? SupervisorPcNumber { get; set; }

    /// <summary>Statut de l'action (0 = en attente, 1 = traitée — héritage : ActionStatus).</summary>
    public int ActionStatus { get; set; }

    public DateTime CreateDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
}

/// <summary>Canaux d'échange (connecteurs externes et messages).</summary>
public static class ExchangeChannels
{
    public const string Edifact = "EDIFACT";
    public const string Apis = "APIS";
    public const string Simiec = "SIMIEC";
    public const string Belize = "BELIZE";
    public const string Air = "AIR";
}

/// <summary>Directions d'un échange.</summary>
public static class ExchangeDirections
{
    public const string Import = "IMPORT";
    public const string Export = "EXPORT";
    public const string Check = "CHECK";
}

/// <summary>
/// Définition d'un échange frontalier (héritage : ImportExportManager) : table/tag
/// échangés, procédure associée, priorité, dépendance au poste. Les connecteurs
/// externes y trouvent aussi leur point d'accès et leur activation.
/// </summary>
public class ExchangeTask
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Canal (ExchangeChannels).</summary>
    public string Channel { get; set; } = ExchangeChannels.Edifact;

    /// <summary>Direction (ExchangeDirections).</summary>
    public string Direction { get; set; } = ExchangeDirections.Import;

    public bool IsActive { get; set; } = true;

    /// <summary>La tâche dépend du poste frontière courant (héritage : DependingOnBorderPoint).</summary>
    public bool DependingOnBorderPoint { get; set; }

    /// <summary>Point d'accès pour les connecteurs (héritage : endpoint en configuration).</summary>
    public string? Endpoint { get; set; }

    /// <summary>Répertoire de dépôt/lecture des fichiers échangés.</summary>
    public string? FileDirectory { get; set; }

    /// <summary>Priorité d'exécution (héritage : Priority).</summary>
    public int Priority { get; set; }

    /// <summary>Paramètres additionnels (héritage : TagName/ParameterType — conservés en texte).</summary>
    public string? Parameters { get; set; }

    public string? Description { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
}

/// <summary>Statuts d'une exécution d'échange.</summary>
public static class ExchangeRunStatus
{
    public const int Running = 0;
    public const int Success = 1;
    public const int Partial = 2;  // terminé avec des rejets enregistrés
    public const int Failed = 3;
}

/// <summary>
/// Journal d'une exécution d'échange (héritage : ExportHistory + ImportExportErrorLog) :
/// période, comptages, statut, messages d'erreur rattachés.
/// </summary>
public class ExchangeRun
{
    public int Id { get; set; }

    public int? ExchangeTaskId { get; set; }
    public ExchangeTask? ExchangeTask { get; set; }

    public string Channel { get; set; } = ExchangeChannels.Edifact;
    public string Direction { get; set; } = ExchangeDirections.Import;

    /// <summary>Référence de l'échange ou du lot traité.</summary>
    public string? Reference { get; set; }

    /// <summary>Fichier traité (import/export).</summary>
    public string? FilePath { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Statut (ExchangeRunStatus).</summary>
    public int Status { get; set; } = ExchangeRunStatus.Running;

    /// <summary>Période des données échangées (héritage : ExporteDataFrom/To).</summary>
    public DateTime? DataFrom { get; set; }
    public DateTime? DataTo { get; set; }

    /// <summary>Éléments traités / réussis / en erreur (héritage : comptages & journal d'erreurs).</summary>
    public int ItemCount { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }

    public string? Summary { get; set; }
    public string? UserId { get; set; }

    public ICollection<ExchangeError> Errors { get; set; } = new List<ExchangeError>();
}

/// <summary>Erreur unitaire d'un échange (héritage : ImportExportErrorLog).</summary>
public class ExchangeError
{
    public long Id { get; set; }

    public int ExchangeRunId { get; set; }
    public ExchangeRun? ExchangeRun { get; set; }

    /// <summary>Procédure ou étape en erreur (héritage : ErrorProcedure).</summary>
    public string? Step { get; set; }

    /// <summary>Référence de l'enregistrement concerné (n° document, ligne…).</summary>
    public string? ItemReference { get; set; }

    public string? Message { get; set; }
    public DateTime OccurredAt { get; set; }
}
