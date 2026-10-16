namespace BorderNext.Domain.Entities;

// ---------------------------------------------------------------------------
// Dossier frontalier : personnes, documents de voyage, mouvements, visas.
// Correspondance avec le système précédent :
//   Person          ← Persons
//   TravelDocument  ← Documents (porte l'état civil du titulaire)
//   DocumentImage   ← Photos (photo et document scanné, varbinary)
//   Movement        ← PassportRegistrationData (l'enregistrement à la frontière)
//   MovementChild   ← Children (enfants rattachés au mouvement)
//   Visa            ← Visa ; VisaChild ← VisaChildren
// Les clés techniques sont des entiers générés ; les identifiants hérités
// (chaînes aléatoires de 10 caractères) sont conservés dans LegacyId pour la
// migration (Lot 9).
// ---------------------------------------------------------------------------

/// <summary>Personne (héritage : Persons).</summary>
public class Person
{
    public int Id { get; set; }

    /// <summary>Identifiant hérité (héritage : ID varchar(40)).</summary>
    public string? LegacyId { get; set; }

    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }

    public ICollection<TravelDocument> Documents { get; set; } = new List<TravelDocument>();
    public ICollection<Movement> Movements { get; set; } = new List<Movement>();
    public ICollection<Fingerprint> Fingerprints { get; set; } = new List<Fingerprint>();
}

/// <summary>
/// Empreinte digitale d'une personne (héritage : Fingerprints — image + gabarit,
/// numérotation 1..10). Les colonnes de capture (qualité, appareil, date) sont un
/// ajout du schéma cible.
/// </summary>
public class Fingerprint
{
    public int PersonId { get; set; }
    public Person? Person { get; set; }

    /// <summary>Numérotation héritée des doigts : 1..5 main gauche, 6..10 main droite.</summary>
    public short FingerNumber { get; set; }

    /// <summary>Image de l'empreinte (héritage : Fingerprint, varbinary).</summary>
    public byte[]? FingerprintImage { get; set; }

    /// <summary>Gabarit biométrique (héritage : Template, varbinary).</summary>
    public byte[]? Template { get; set; }

    /// <summary>Indice de qualité retourné par le capteur (0..100).</summary>
    public int? Quality { get; set; }

    public DateTime? CaptureDate { get; set; }

    /// <summary>Appareil de capture (nom du capteur).</summary>
    public string? CaptureDevice { get; set; }

    public string? UpdateUser { get; set; }
    public short? BorderPointId { get; set; }
}

/// <summary>Document de voyage, porteur de l'état civil (héritage : Documents).</summary>
public class TravelDocument
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public int PersonId { get; set; }
    public Person? Person { get; set; }

    /// <summary>Type de document (nomenclature DOCUMENT_TYPE).</summary>
    public int DocumentTypeId { get; set; }
    public Lookup? DocumentType { get; set; }

    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>Pays de délivrance (héritage : DocumentCountryOfIssueID).</summary>
    public int? CountryOfIssueId { get; set; }
    public Country? CountryOfIssue { get; set; }

    public DateTime? IssueDate { get; set; }
    public DateTime? ExpireDate { get; set; }

    public string? Surname { get; set; }
    public string? GivenName { get; set; }
    public string? FatherName { get; set; }
    public DateTime? DateOfBirth { get; set; }

    /// <summary>Sexe (nomenclature SEX).</summary>
    public int? SexId { get; set; }
    public Lookup? Sex { get; set; }

    /// <summary>Nationalité (héritage : Citizenship → lCountries).</summary>
    public int? CitizenshipId { get; set; }
    public Country? Citizenship { get; set; }

    public string? PersonalNumber { get; set; }

    public DateTime? CreateDate { get; set; }
    public string? CreatorUser { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }

    public DocumentImage? Image { get; set; }
    public ICollection<Movement> Movements { get; set; } = new List<Movement>();
    public ICollection<Visa> Visas { get; set; } = new List<Visa>();
}

/// <summary>Photo du titulaire et document scanné (héritage : Photos).</summary>
public class DocumentImage
{
    /// <summary>Clé partagée avec le document (héritage : DocumentID).</summary>
    public int DocumentId { get; set; }
    public TravelDocument? Document { get; set; }

    public byte[]? Photo { get; set; }
    public byte[]? ScannedDocument { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
}

/// <summary>Mouvement à la frontière — l'enregistrement passeport (héritage : PassportRegistrationData).</summary>
public class Movement
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public int PersonId { get; set; }
    public Person? Person { get; set; }
    public int DocumentId { get; set; }
    public TravelDocument? Document { get; set; }

    public DateTime TravelDate { get; set; }

    /// <summary>Sens du mouvement (nomenclature MOVEMENT_DIRECTION : entrée/sortie).</summary>
    public int DirectionLookupId { get; set; }
    public Lookup? Direction { get; set; }

    /// <summary>Mode de transport (nomenclature TRANSPORT_TYPE).</summary>
    public int? TransportTypeId { get; set; }
    public Lookup? TransportType { get; set; }

    /// <summary>Identifiant de passage hérité (Transports initialement, conservé pour
    /// la migration du Lot 9).</summary>
    public string? TransportCrossBorderId { get; set; }

    /// <summary>Passage de voyage (Lot 6 ; héritage : PassportRegistrationData.TransportCrossBorderID
    /// → TransportCrossedBorder.ID).</summary>
    public int? TripCrossingId { get; set; }
    public TripCrossing? TripCrossing { get; set; }

    public bool IsDriver { get; set; }

    /// <summary>Motif de voyage (nomenclature TRAVEL_REASON).</summary>
    public int? TravelReasonId { get; set; }
    public Lookup? TravelReason { get; set; }

    /// <summary>Passage accordé (héritage : Granted ; false = refus avec motif).</summary>
    public bool Granted { get; set; } = true;

    /// <summary>Motif de déportation/refus (nomenclature DEPORTATION_REASON).</summary>
    public int? DeportationReasonId { get; set; }
    public Lookup? DeportationReason { get; set; }

    /// <summary>
    /// Base légale du refus d'entrée (nomenclature REFUSAL_JUSTIFICATION,
    /// héritage : lRefusalJustifications — imprimée sur le refus d'entrée).
    /// </summary>
    public int? RefusalJustificationId { get; set; }
    public Lookup? RefusalJustification { get; set; }

    public bool IsTransit { get; set; }
    public bool HasVisa { get; set; }

    public int? VisaId { get; set; }
    public Visa? Visa { get; set; }

    public int ChildrenCount { get; set; }
    public string? Note { get; set; }

    /// <summary>Pays de résidence (héritage : ResidentCountryID).</summary>
    public int? ResidentCountryId { get; set; }
    public Country? ResidentCountry { get; set; }

    /// <summary>Pays de destination (héritage : DestinationCountryID).</summary>
    public int? DestinationCountryId { get; set; }
    public Country? DestinationCountry { get; set; }

    public string? Occupation { get; set; }

    public string? AccommodationAddress { get; set; }

    /// <summary>Type d'hébergement (nomenclature ACCOMODATION_TYPE).</summary>
    public int? AccommodationTypeId { get; set; }
    public Lookup? AccommodationType { get; set; }

    /// <summary>Durée de séjour prévue (nuitées ; héritage : DurationOfStay).</summary>
    public short? DurationOfStay { get; set; }

    public string? Inspector { get; set; }
    public string? PcNumber { get; set; }
    public DateTime RegistrationTime { get; set; }
    public DateTime? UpdateDate { get; set; }
    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }

    public ICollection<MovementChild> Children { get; set; } = new List<MovementChild>();
}

/// <summary>Enfant rattaché à un mouvement (héritage : Children).</summary>
public class MovementChild
{
    public int Id { get; set; }
    public int MovementId { get; set; }
    public Movement? Movement { get; set; }

    public string? Surname { get; set; }
    public string? GivenName { get; set; }
    public DateTime? DateOfBirth { get; set; }

    /// <summary>Sexe (nomenclature SEX).</summary>
    public int? SexId { get; set; }

    /// <summary>Nationalité (pays).</summary>
    public int? CitizenshipId { get; set; }

    /// <summary>Type de document (nomenclature DOCUMENT_TYPE).</summary>
    public int? DocumentTypeId { get; set; }

    public string? DocumentNumber { get; set; }

    /// <summary>Personne responsable (héritage : Attorney).</summary>
    public string? Attorney { get; set; }

    /// <summary>Billet vendu/émis (héritage : Ticket).</summary>
    public bool Ticket { get; set; }

    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
}

/// <summary>Visa (héritage : Visa).</summary>
public class Visa
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    /// <summary>Numéro de visa (généré si absent : VISA-AAAA-NNNNNN).</summary>
    public string? Number { get; set; }

    /// <summary>Type de visa (nomenclature VISA_TYPE).</summary>
    public int VisaTypeId { get; set; }
    public Lookup? VisaType { get; set; }

    /// <summary>Entrées (nomenclature VISA_ENTRY : unique/multiple).</summary>
    public int? VisaEntryId { get; set; }
    public Lookup? VisaEntry { get; set; }

    public DateTime? IssueDate { get; set; }
    public DateTime? ExpireDate { get; set; }

    /// <summary>Durée autorisée (jours ; héritage : Duration).</summary>
    public short? Duration { get; set; }

    /// <summary>Document porteur (héritage : DocumentID).</summary>
    public int? DocumentId { get; set; }
    public TravelDocument? Document { get; set; }

    public string? ReferenceNumber { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? Address { get; set; }
    public string? ContactNumber { get; set; }
    public string? Employer { get; set; }
    public string? InvitedBy { get; set; }

    /// <summary>Lien de parenté (nomenclature RELATION).</summary>
    public int? RelationId { get; set; }

    /// <summary>Université (nomenclature UNIVERSITY).</summary>
    public int? UniversityId { get; set; }

    /// <summary>Statut permis (nomenclature PERMIT_STATUS).</summary>
    public int? PermitStatusId { get; set; }

    public string? Occupation { get; set; }

    /// <summary>Type de période (nomenclature PERIOD_TYPE).</summary>
    public int? PeriodTypeId { get; set; }

    public string? Note { get; set; }
    public string? VoucherNumber { get; set; }

    /// <summary>Condition de permis (nomenclature PERMIT_CONDITION).</summary>
    public int? PermitConditionId { get; set; }

    /// <summary>Province de destination (héritage : ProvinceID).</summary>
    public int? ProvinceId { get; set; }
    public Province? Province { get; set; }

    public string? Barcode { get; set; }
    public string? PcNumber { get; set; }
    public byte[]? Photo { get; set; }

    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? CreateDate { get; set; }
    public string? CreatorUser { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }

    public ICollection<VisaChild> Children { get; set; } = new List<VisaChild>();
}

/// <summary>Enfant rattaché à un visa (héritage : VisaChildren).</summary>
public class VisaChild
{
    public int Id { get; set; }
    public int VisaId { get; set; }
    public Visa? Visa { get; set; }

    public string? Surname { get; set; }
    public string? GivenName { get; set; }
    public DateTime? DateOfBirth { get; set; }

    /// <summary>Sexe (nomenclature SEX).</summary>
    public int? SexId { get; set; }

    /// <summary>Nationalité (pays).</summary>
    public int? CitizenshipId { get; set; }

    /// <summary>Type de document (nomenclature DOCUMENT_TYPE).</summary>
    public int? DocumentTypeId { get; set; }

    public string? DocumentNumber { get; set; }
    public byte[]? Photo { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
    public short? BorderPointId { get; set; }
}
