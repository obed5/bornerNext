namespace BorderNext.Domain.Entities;

// ---------------------------------------------------------------------------
// Permis de résidence/travail et enforcement (Lot 4).
// Correspondance avec le système précédent :
//   Permit              ← Permit (23 colonnes) + PermitTemp (numérotation)
//   PermitDetails       ← PermitDetails (résidence, adresses, famille)
//   PermitContact       ← PermitContact ; PermitDriverLicense ← PermitDriverLicense
//   PermitSecurityRecord← PermitSecurityRecords ; PermitSelfEmployed ← PermitSelfEmployed
//   PermitTravelDetails ← PermitTravelDetails ; PermitDependant ← PermitDependant
//   RemovalOrder        ← RemovalOrders (déclenché depuis un refus d'entrée ;
//                          le rattachement à la liste d'alerte arrive au Lot 5)
//   PenaltyTicket       ← PenaltyTicket (créé depuis un enregistrement de passage)
// Différés et documentés : PermitMedicalHistory (module santé, Lot 8),
// PermitDocuments (stockage de fichiers, Lot 8/9), PermitApplicants (portail
// externe demandeurs), PermitClassification*/PermitApplicantVisa (déploiements
// spécifiques, tables vides dans la sauvegarde).
// ---------------------------------------------------------------------------

/// <summary>Permis (héritage : Permit).</summary>
public class Permit
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    /// <summary>Document porteur (héritage : DocumentID).</summary>
    public int DocumentId { get; set; }
    public TravelDocument? Document { get; set; }

    /// <summary>Visa associé le cas échéant (héritage : VisaID).</summary>
    public int? VisaId { get; set; }
    public Visa? Visa { get; set; }

    /// <summary>Type de permis (nomenclature PERMIT_TYPE).</summary>
    public int? PermitTypeId { get; set; }
    public Lookup? PermitType { get; set; }

    /// <summary>Numéro de permis (généré si absent : PERMIT-AAAA-NNNNNN).</summary>
    public string? Number { get; set; }

    public DateTime? IssueDate { get; set; }
    public DateTime? ExpireDate { get; set; }

    /// <summary>Statut (nomenclature PERMIT_STATUS : Started/Pending/Done/Denied).</summary>
    public int? StatusId { get; set; }
    public Lookup? Status { get; set; }

    /// <summary>État civil (nomenclature MARITAL_STATUS).</summary>
    public int? MaritalStatusId { get; set; }
    public Lookup? MaritalStatus { get; set; }

    /// <summary>Profession (nomenclature OCCUPATION) et précision libre.</summary>
    public int? OccupationId { get; set; }
    public Lookup? Occupation { get; set; }
    public string? OccupationOther { get; set; }

    /// <summary>Qualification (nomenclature QUALIFICATION).</summary>
    public int? QualificationId { get; set; }
    public Lookup? Qualification { get; set; }

    /// <summary>Filière d'études (nomenclature COURSE_OF_STUDY).</summary>
    public int? CourseOfStudyId { get; set; }

    /// <summary>Groupe sanguin (nomenclature BLOOD_TYPE).</summary>
    public int? BloodTypeId { get; set; }

    /// <summary>Autorité émettrice du document (héritage : DocumentIssuingAuthority).</summary>
    public string? DocumentIssuingAuthority { get; set; }

    public string? Note { get; set; }
    public string? PcNumber { get; set; }

    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? CreateDate { get; set; }
    public string? CreatorUser { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }

    public PermitDetails? Details { get; set; }
    public PermitSelfEmployed? SelfEmployed { get; set; }
    public PermitTravelDetails? TravelDetails { get; set; }
    public ICollection<PermitContact> Contacts { get; set; } = new List<PermitContact>();
    public ICollection<PermitDriverLicense> DriverLicenses { get; set; } = new List<PermitDriverLicense>();
    public ICollection<PermitSecurityRecord> SecurityRecords { get; set; } = new List<PermitSecurityRecord>();
    public ICollection<PermitDependant> Dependants { get; set; } = new List<PermitDependant>();
}

/// <summary>Détails du permis : résidence, adresses, situation familiale (héritage : PermitDetails).</summary>
public class PermitDetails
{
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }

    public int? CurrentCountryId { get; set; }
    public int? CurrentProvinceId { get; set; }
    public int? CurrentCityId { get; set; }
    public string? CurrentTown { get; set; }
    public string? CurrentAddress { get; set; }
    public string? CurrentPhoneNumber { get; set; }
    public int? CurrentLocalAreaId { get; set; }

    public int? PreviousCountryId { get; set; }
    public int? PreviousProvinceId { get; set; }
    public int? PreviousCityId { get; set; }
    public string? PreviousTown { get; set; }
    public string? PreviousAddress { get; set; }
    public string? PreviousPostalCode { get; set; }
    public int? PreviousCitizenshipId { get; set; }

    public int? PlaceOfBirthCountryId { get; set; }
    public int? PlaceOfBirthProvinceId { get; set; }
    public string? PlaceOfBirthTown { get; set; }
    public int? SecondCitizenshipId { get; set; }

    public int? CurrentPermitTypeId { get; set; }
    public string? CurrentPermitNumber { get; set; }

    public string? ReferencePersonName { get; set; }
    public string? ReferencePersonPhone { get; set; }

    public int? PlaceOfResidenceTypeId { get; set; }
    public int? EthnicGroupId { get; set; }

    public int? NumberOfChildrenBornHere { get; set; }
    public int? NumberOfChildrenLivingHere { get; set; }
    public int? NumberOfPeopleLiveWith { get; set; }

    public bool IntendedToStayMore { get; set; }
    public bool HadFixJob { get; set; }
    public bool HasCitizenFamilyMember { get; set; }
    public bool HasChildrenBornHere { get; set; }
    public bool LivingWithOtherMigrants { get; set; }
    public bool ChildrenLivingInSameHouse { get; set; }
    public bool ChildrenEnrolledEducation { get; set; }

    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
}

/// <summary>Contact du titulaire (héritage : PermitContact).</summary>
public class PermitContact
{
    public int Id { get; set; }
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }

    /// <summary>Type de contact (nomenclature RELATION).</summary>
    public int? ContactTypeId { get; set; }

    public string? Description { get; set; }
}

/// <summary>Permis de conduire déclaré (héritage : PermitDriverLicense).</summary>
public class PermitDriverLicense
{
    public int Id { get; set; }
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }

    public int? CountryOfIssueId { get; set; }
    public string? Number { get; set; }
}

/// <summary>Antécédent de sécurité (héritage : PermitSecurityRecords).</summary>
public class PermitSecurityRecord
{
    public int Id { get; set; }
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }

    public string? RecordType { get; set; }
    public string? Description { get; set; }
}

/// <summary>Activité indépendante (héritage : PermitSelfEmployed).</summary>
public class PermitSelfEmployed
{
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }

    public string? Name { get; set; }
    public int? ProvinceId { get; set; }
    public string? Address { get; set; }

    /// <summary>Secteur (nomenclature INDUSTRY).</summary>
    public int? IndustryId { get; set; }

    /// <summary>Effectif (nomenclature EMPLOYEE_RANGE).</summary>
    public int? EmployeeRangeId { get; set; }

    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
}

/// <summary>Projet de voyage du demandeur (héritage : PermitTravelDetails).</summary>
public class PermitTravelDetails
{
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }

    public bool FreeWillTravel { get; set; }
    public int? TravelReasonId { get; set; }
    public int? DocumentTypeId { get; set; }
    public int? TransportTypeId { get; set; }

    public int? DepartureCountryId { get; set; }
    public int? DepartureProvinceId { get; set; }
    public string? DepartureTown { get; set; }
    public DateTime? DepartureDate { get; set; }

    public bool ImmigrationCheck { get; set; }

    /// <summary>Poste d'entrée prévu (héritage : PortOfEntryID → lBorderPoints).</summary>
    public short? PortOfEntryId { get; set; }

    public DateTime? ArrivalDate { get; set; }
    public bool IrregularExit { get; set; }
}

/// <summary>Personne à charge du titulaire (héritage : PermitDependant).</summary>
public class PermitDependant
{
    public int Id { get; set; }
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }

    public string? Surname { get; set; }
    public string? GivenName { get; set; }
    public string? OtherNames { get; set; }
    public DateTime? DateOfBirth { get; set; }

    public int? CountryOfBirthId { get; set; }

    /// <summary>Lien de parenté (nomenclature RELATION).</summary>
    public int? RelationId { get; set; }

    public int? DocumentTypeId { get; set; }
    public string? DocumentNumber { get; set; }
    public int? CitizenshipId { get; set; }
    public int? SexId { get; set; }
    public string? PhoneNumber { get; set; }

    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
}

/// <summary>Ordre de reconduite / avis d'inadmissibilité (héritage : RemovalOrders).</summary>
public class RemovalOrder
{
    public int Id { get; set; }

    /// <summary>Personne concernée.</summary>
    public int PersonId { get; set; }
    public Person? Person { get; set; }

    /// <summary>Mouvement refusé à l'origine (héritage : flux stop-list → ordre).</summary>
    public int? MovementId { get; set; }
    public Movement? Movement { get; set; }

    /// <summary>Rattachement à la liste d'alerte (héritage : PersonStopListID ; Lot 5).</summary>
    public int? PersonStopListId { get; set; }

    /// <summary>Numéro de document au moment de l'ordre (héritage : DocNumber, copie).</summary>
    public string? DocNumber { get; set; }

    /// <summary>Type d'ordre (nomenclature REMOVAL_ORDER_TYPE : Order/Notice).</summary>
    public int TypeId { get; set; }
    public Lookup? Type { get; set; }

    /// <summary>Motif / informations complémentaires (héritage : Info).</summary>
    public string? Info { get; set; }

    /// <summary>Officier ayant délivré l'ordre (héritage : OfficerName).</summary>
    public string? OfficerName { get; set; }

    public DateTime CreateDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
    public short? BorderPointId { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
}

/// <summary>Amende / ticket de pénalité (héritage : PenaltyTicket).</summary>
public class PenaltyTicket
{
    public int Id { get; set; }

    /// <summary>Enregistrement de passage à l'origine (héritage : RegistrationID).</summary>
    public int? MovementId { get; set; }
    public Movement? Movement { get; set; }

    /// <summary>Motif (nomenclature PENALTY_TICKET_REASON).</summary>
    public int PenaltyReasonId { get; set; }
    public Lookup? PenaltyReason { get; set; }

    public decimal Amount { get; set; }

    // Copies héritées du document et du titulaire au moment de l'amende.
    public int? DocumentTypeId { get; set; }
    public string? DocumentNumber { get; set; }
    public DateTime? DocumentExpireDate { get; set; }
    public DateTime? DocumentIssueDate { get; set; }
    public string? Surname { get; set; }
    public string? GivenName { get; set; }
    public int? SexId { get; set; }
    public int? DocumentCountryOfIssueId { get; set; }

    public string? VisaNumber { get; set; }
    public DateTime? VisaIssueDate { get; set; }
    public DateTime? VisaExpireDate { get; set; }

    /// <summary>Durée du dépassement (jours ; héritage : Duration).</summary>
    public int? Duration { get; set; }

    public string? Number { get; set; }
    public string? CurrencyCode { get; set; }
    public bool Paid { get; set; }
    public DateTime? PaidDate { get; set; }
    public string? ReceiptNumber { get; set; }

    public DateTime CreateDate { get; set; }
    public string? UserId { get; set; }
    public DateTime? UpdateDate { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public short? BorderPointId { get; set; }
}
