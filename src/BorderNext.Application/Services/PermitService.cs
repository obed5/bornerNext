using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Données d'émission/mise à jour d'un permis (héritage : Permit).</summary>
public sealed record PermitSaveRequest(string? Number, int DocumentId, int? VisaId, int? PermitTypeId,
    DateTime? IssueDate, DateTime? ExpireDate, int? StatusId, int? MaritalStatusId, int? OccupationId,
    string? OccupationOther, int? QualificationId, int? CourseOfStudyId, int? BloodTypeId,
    string? DocumentIssuingAuthority, string? Note);

/// <summary>Ligne de liste des permis.</summary>
public sealed record PermitListItem(int Id, string? Number, string DocumentNumber, string? HolderName,
    string? TypeFr, string? StatusFr, DateTime? IssueDate, DateTime? ExpireDate, bool Revoke, bool IsValid);

/// <summary>Détails du permis (héritage : PermitDetails).</summary>
public sealed record PermitDetailsRequest(int? CurrentCountryId, int? CurrentProvinceId, int? CurrentCityId,
    string? CurrentTown, string? CurrentAddress, string? CurrentPhoneNumber, int? CurrentLocalAreaId,
    int? PreviousCountryId, int? PreviousProvinceId, int? PreviousCityId, string? PreviousTown,
    string? PreviousAddress, string? PreviousPostalCode, int? PreviousCitizenshipId,
    int? PlaceOfBirthCountryId, int? PlaceOfBirthProvinceId, string? PlaceOfBirthTown, int? SecondCitizenshipId,
    int? CurrentPermitTypeId, string? CurrentPermitNumber, string? ReferencePersonName, string? ReferencePersonPhone,
    int? PlaceOfResidenceTypeId, int? EthnicGroupId, int? NumberOfChildrenBornHere, int? NumberOfChildrenLivingHere,
    int? NumberOfPeopleLiveWith, bool IntendedToStayMore, bool HadFixJob, bool HasCitizenFamilyMember,
    bool HasChildrenBornHere, bool LivingWithOtherMigrants, bool ChildrenLivingInSameHouse, bool ChildrenEnrolledEducation);

/// <summary>Projet de voyage du demandeur (héritage : PermitTravelDetails).</summary>
public sealed record PermitTravelRequest(bool FreeWillTravel, int? TravelReasonId, int? DocumentTypeId,
    int? TransportTypeId, int? DepartureCountryId, int? DepartureProvinceId, string? DepartureTown,
    DateTime? DepartureDate, bool ImmigrationCheck, short? PortOfEntryId, DateTime? ArrivalDate, bool IrregularExit);

/// <summary>Activité indépendante (héritage : PermitSelfEmployed).</summary>
public sealed record PermitSelfEmployedRequest(string? Name, int? ProvinceId, string? Address,
    int? IndustryId, int? EmployeeRangeId);

/// <summary>Personne à charge (héritage : PermitDependant).</summary>
public sealed record DependantRequest(string? Surname, string? GivenName, string? OtherNames,
    DateTime? DateOfBirth, int? CountryOfBirthId, int? RelationId, int? DocumentTypeId, string? DocumentNumber,
    int? CitizenshipId, int? SexId, string? PhoneNumber);

/// <summary>Agrégat complet d'un permis pour l'écran de saisie.</summary>
public sealed record PermitDetailDto(PermitListItem Header, int DocumentId, int? VisaId, int? PermitTypeId,
    int? StatusId, int? MaritalStatusId, int? OccupationId, string? OccupationOther, int? QualificationId,
    int? CourseOfStudyId, int? BloodTypeId, string? DocumentIssuingAuthority, string? Note,
    PermitDetailsRequest? Details, PermitTravelRequest? Travel, PermitSelfEmployedRequest? SelfEmployed,
    IReadOnlyList<PermitContact> Contacts, IReadOnlyList<PermitDriverLicense> DriverLicenses,
    IReadOnlyList<PermitSecurityRecord> SecurityRecords, IReadOnlyList<PermitDependant> Dependants);

/// <summary>
/// Gestion des permis de résidence/travail (héritage : PermitRegistrationForm —
/// 366 méthodes, PermitManagementForm) : permis, détails de résidence,
/// contacts, permis de conduire, antécédents, activité indépendante,
/// projet de voyage, personnes à charge.
/// </summary>
public class PermitService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public PermitService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>Émet un permis ; le numéro est généré si absent (PERMIT-AAAA-NNNNNN).</summary>
    public async Task<int> CreateAsync(PermitSaveRequest request, CancellationToken ct = default)
    {
        await ValidateAsync(request, ct);
        var permit = new Permit
        {
            DocumentId = request.DocumentId,
            CreateDate = _clock.Now,
            CreatorUser = _currentUser.UserId,
            PcNumber = _currentUser.PcNumber,
            BorderPointId = _currentUser.BorderPointId
        };
        Apply(permit, request);
        permit.Number = string.IsNullOrWhiteSpace(request.Number)
            ? await GenerateNumberAsync(ct)
            : request.Number!.Trim();

        _db.Set<Permit>().Add(permit);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Permit), permit.Id.ToString(),
            $"Permis {permit.Number} (document {request.DocumentId})", ct);
        return permit.Id;
    }

    public async Task UpdateAsync(int permitId, PermitSaveRequest request, CancellationToken ct = default)
    {
        await ValidateAsync(request, ct);
        var permit = await _db.Set<Permit>().SingleOrDefaultAsync(p => p.Id == permitId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", permitId.ToString());

        if (!string.IsNullOrWhiteSpace(request.Number)
            && await _db.Set<Permit>().AnyAsync(p => p.Number == request.Number!.Trim() && p.Id != permitId, ct))
            throw new DomainValidationException("PV_Error_DuplicateNumber", request.Number);

        Apply(permit, request);
        if (!string.IsNullOrWhiteSpace(request.Number)) permit.Number = request.Number!.Trim();
        permit.UpdateDate = _clock.Now;
        permit.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Permit), permit.Id.ToString(), $"Permis {permit.Number}", ct);
    }

    public async Task SetRevokedAsync(int permitId, bool revoke, CancellationToken ct = default)
    {
        var permit = await _db.Set<Permit>().SingleOrDefaultAsync(p => p.Id == permitId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", permitId.ToString());

        permit.Revoke = revoke;
        permit.RevokeDate = revoke ? _clock.Now : null;
        permit.UpdateDate = _clock.Now;
        permit.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Permit), permitId.ToString(),
            $"{(revoke ? "Révocation" : "Réactivation")} permis {permit.Number}", ct);
    }

    public async Task<IReadOnlyList<PermitListItem>> SearchAsync(string? number = null, string? documentNumber = null,
        string? surname = null, bool includeRevoked = false, int take = 200, CancellationToken ct = default)
    {
        var query = _db.Set<Permit>()
            .Include(p => p.PermitType!).ThenInclude(t => t.Cultures)
            .Include(p => p.Status!).ThenInclude(t => t.Cultures)
            .Include(p => p.Document)
            .Where(p => includeRevoked || !p.Revoke);

        if (!string.IsNullOrWhiteSpace(number))
        {
            var upper = number.Trim().ToUpperInvariant();
            query = query.Where(p => p.Number != null && p.Number.ToUpper().Contains(upper));
        }
        if (!string.IsNullOrWhiteSpace(documentNumber))
        {
            var upper = documentNumber.Trim().ToUpperInvariant();
            query = query.Where(p => p.Document!.DocumentNumber.ToUpper().Contains(upper));
        }
        if (!string.IsNullOrWhiteSpace(surname))
        {
            var upper = surname.Trim().ToUpperInvariant();
            query = query.Where(p => p.Document!.Surname != null && p.Document.Surname.ToUpper().Contains(upper));
        }

        var permits = await query.OrderByDescending(p => p.CreateDate).Take(take).ToListAsync(ct);
        return permits.Select(ToList).ToList();
    }

    public async Task<PermitDetailDto?> GetDetailAsync(int permitId, CancellationToken ct = default)
    {
        var permit = await _db.Set<Permit>()
            .Include(p => p.Document)
            .Include(p => p.PermitType!).ThenInclude(t => t.Cultures)
            .Include(p => p.Status!).ThenInclude(t => t.Cultures)
            .Include(p => p.Details)
            .Include(p => p.TravelDetails)
            .Include(p => p.SelfEmployed)
            .Include(p => p.Contacts)
            .Include(p => p.DriverLicenses)
            .Include(p => p.SecurityRecords)
            .Include(p => p.Dependants)
            .SingleOrDefaultAsync(p => p.Id == permitId, ct);
        if (permit is null) return null;

        return new PermitDetailDto(ToList(permit), permit.DocumentId, permit.VisaId, permit.PermitTypeId,
            permit.StatusId, permit.MaritalStatusId, permit.OccupationId, permit.OccupationOther,
            permit.QualificationId, permit.CourseOfStudyId, permit.BloodTypeId, permit.DocumentIssuingAuthority,
            permit.Note,
            permit.Details is null ? null : new PermitDetailsRequest(permit.Details.CurrentCountryId,
                permit.Details.CurrentProvinceId, permit.Details.CurrentCityId, permit.Details.CurrentTown,
                permit.Details.CurrentAddress, permit.Details.CurrentPhoneNumber, permit.Details.CurrentLocalAreaId,
                permit.Details.PreviousCountryId, permit.Details.PreviousProvinceId, permit.Details.PreviousCityId,
                permit.Details.PreviousTown, permit.Details.PreviousAddress, permit.Details.PreviousPostalCode,
                permit.Details.PreviousCitizenshipId, permit.Details.PlaceOfBirthCountryId,
                permit.Details.PlaceOfBirthProvinceId, permit.Details.PlaceOfBirthTown, permit.Details.SecondCitizenshipId,
                permit.Details.CurrentPermitTypeId, permit.Details.CurrentPermitNumber, permit.Details.ReferencePersonName,
                permit.Details.ReferencePersonPhone, permit.Details.PlaceOfResidenceTypeId, permit.Details.EthnicGroupId,
                permit.Details.NumberOfChildrenBornHere, permit.Details.NumberOfChildrenLivingHere,
                permit.Details.NumberOfPeopleLiveWith, permit.Details.IntendedToStayMore, permit.Details.HadFixJob,
                permit.Details.HasCitizenFamilyMember, permit.Details.HasChildrenBornHere,
                permit.Details.LivingWithOtherMigrants, permit.Details.ChildrenLivingInSameHouse,
                permit.Details.ChildrenEnrolledEducation),
            permit.TravelDetails is null ? null : new PermitTravelRequest(permit.TravelDetails.FreeWillTravel,
                permit.TravelDetails.TravelReasonId, permit.TravelDetails.DocumentTypeId,
                permit.TravelDetails.TransportTypeId, permit.TravelDetails.DepartureCountryId,
                permit.TravelDetails.DepartureProvinceId, permit.TravelDetails.DepartureTown,
                permit.TravelDetails.DepartureDate, permit.TravelDetails.ImmigrationCheck,
                permit.TravelDetails.PortOfEntryId, permit.TravelDetails.ArrivalDate, permit.TravelDetails.IrregularExit),
            permit.SelfEmployed is null ? null : new PermitSelfEmployedRequest(permit.SelfEmployed.Name,
                permit.SelfEmployed.ProvinceId, permit.SelfEmployed.Address, permit.SelfEmployed.IndustryId,
                permit.SelfEmployed.EmployeeRangeId),
            permit.Contacts.ToList(), permit.DriverLicenses.ToList(), permit.SecurityRecords.ToList(),
            permit.Dependants.ToList());
    }

    // ---- Sections 1:1 ---------------------------------------------------------

    public async Task SaveDetailsAsync(int permitId, PermitDetailsRequest r, CancellationToken ct = default)
    {
        var details = await _db.Set<PermitDetails>().SingleOrDefaultAsync(d => d.PermitId == permitId, ct);
        if (details is null)
        {
            details = new PermitDetails { PermitId = permitId };
            _db.Set<PermitDetails>().Add(details);
        }
        details.CurrentCountryId = r.CurrentCountryId;
        details.CurrentProvinceId = r.CurrentProvinceId;
        details.CurrentCityId = r.CurrentCityId;
        details.CurrentTown = Clean(r.CurrentTown);
        details.CurrentAddress = Clean(r.CurrentAddress);
        details.CurrentPhoneNumber = Clean(r.CurrentPhoneNumber);
        details.CurrentLocalAreaId = r.CurrentLocalAreaId;
        details.PreviousCountryId = r.PreviousCountryId;
        details.PreviousProvinceId = r.PreviousProvinceId;
        details.PreviousCityId = r.PreviousCityId;
        details.PreviousTown = Clean(r.PreviousTown);
        details.PreviousAddress = Clean(r.PreviousAddress);
        details.PreviousPostalCode = Clean(r.PreviousPostalCode);
        details.PreviousCitizenshipId = r.PreviousCitizenshipId;
        details.PlaceOfBirthCountryId = r.PlaceOfBirthCountryId;
        details.PlaceOfBirthProvinceId = r.PlaceOfBirthProvinceId;
        details.PlaceOfBirthTown = Clean(r.PlaceOfBirthTown);
        details.SecondCitizenshipId = r.SecondCitizenshipId;
        details.CurrentPermitTypeId = r.CurrentPermitTypeId;
        details.CurrentPermitNumber = Clean(r.CurrentPermitNumber);
        details.ReferencePersonName = Clean(r.ReferencePersonName);
        details.ReferencePersonPhone = Clean(r.ReferencePersonPhone);
        details.PlaceOfResidenceTypeId = r.PlaceOfResidenceTypeId;
        details.EthnicGroupId = r.EthnicGroupId;
        details.NumberOfChildrenBornHere = NonNegative(r.NumberOfChildrenBornHere);
        details.NumberOfChildrenLivingHere = NonNegative(r.NumberOfChildrenLivingHere);
        details.NumberOfPeopleLiveWith = NonNegative(r.NumberOfPeopleLiveWith);
        details.IntendedToStayMore = r.IntendedToStayMore;
        details.HadFixJob = r.HadFixJob;
        details.HasCitizenFamilyMember = r.HasCitizenFamilyMember;
        details.HasChildrenBornHere = r.HasChildrenBornHere;
        details.LivingWithOtherMigrants = r.LivingWithOtherMigrants;
        details.ChildrenLivingInSameHouse = r.ChildrenLivingInSameHouse;
        details.ChildrenEnrolledEducation = r.ChildrenEnrolledEducation;
        details.UpdateDate = _clock.Now;
        details.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(PermitDetails), permitId.ToString(), "Détails du permis", ct);
    }

    public async Task SaveTravelDetailsAsync(int permitId, PermitTravelRequest r, CancellationToken ct = default)
    {
        var travel = await _db.Set<PermitTravelDetails>().SingleOrDefaultAsync(t => t.PermitId == permitId, ct);
        if (travel is null)
        {
            travel = new PermitTravelDetails { PermitId = permitId };
            _db.Set<PermitTravelDetails>().Add(travel);
        }
        travel.FreeWillTravel = r.FreeWillTravel;
        travel.TravelReasonId = r.TravelReasonId;
        travel.DocumentTypeId = r.DocumentTypeId;
        travel.TransportTypeId = r.TransportTypeId;
        travel.DepartureCountryId = r.DepartureCountryId;
        travel.DepartureProvinceId = r.DepartureProvinceId;
        travel.DepartureTown = Clean(r.DepartureTown);
        travel.DepartureDate = r.DepartureDate;
        travel.ImmigrationCheck = r.ImmigrationCheck;
        travel.PortOfEntryId = r.PortOfEntryId;
        travel.ArrivalDate = r.ArrivalDate;
        travel.IrregularExit = r.IrregularExit;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(PermitTravelDetails), permitId.ToString(), "Projet de voyage", ct);
    }

    public async Task SaveSelfEmployedAsync(int permitId, PermitSelfEmployedRequest r, CancellationToken ct = default)
    {
        var activity = await _db.Set<PermitSelfEmployed>().SingleOrDefaultAsync(s => s.PermitId == permitId, ct);
        if (activity is null)
        {
            activity = new PermitSelfEmployed { PermitId = permitId };
            _db.Set<PermitSelfEmployed>().Add(activity);
        }
        activity.Name = Clean(r.Name);
        activity.ProvinceId = r.ProvinceId;
        activity.Address = Clean(r.Address);
        activity.IndustryId = r.IndustryId;
        activity.EmployeeRangeId = r.EmployeeRangeId;
        activity.UpdateDate = _clock.Now;
        activity.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(PermitSelfEmployed), permitId.ToString(), "Activité indépendante", ct);
    }

    // ---- Listes enfants -------------------------------------------------------

    public async Task<int> AddContactAsync(int permitId, int? contactTypeId, string? description, CancellationToken ct = default)
    {
        var contact = new PermitContact { PermitId = permitId, ContactTypeId = contactTypeId, Description = Clean(description) };
        _db.Set<PermitContact>().Add(contact);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(PermitContact), contact.Id.ToString(), contact.Description, ct);
        return contact.Id;
    }

    public async Task RemoveContactAsync(int contactId, CancellationToken ct = default)
        => await RemoveChildAsync<PermitContact>(contactId, ct);

    public async Task<int> AddDriverLicenseAsync(int permitId, int? countryOfIssueId, string? number, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(number)) throw new DomainValidationException("PV_Error_LicenseNumberRequired");
        var license = new PermitDriverLicense { PermitId = permitId, CountryOfIssueId = countryOfIssueId, Number = number.Trim() };
        _db.Set<PermitDriverLicense>().Add(license);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(PermitDriverLicense), license.Id.ToString(), license.Number, ct);
        return license.Id;
    }

    public async Task RemoveDriverLicenseAsync(int licenseId, CancellationToken ct = default)
        => await RemoveChildAsync<PermitDriverLicense>(licenseId, ct);

    public async Task<int> AddSecurityRecordAsync(int permitId, string? recordType, string? description, CancellationToken ct = default)
    {
        var record = new PermitSecurityRecord { PermitId = permitId, RecordType = Clean(recordType), Description = Clean(description) };
        _db.Set<PermitSecurityRecord>().Add(record);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(PermitSecurityRecord), record.Id.ToString(), record.RecordType, ct);
        return record.Id;
    }

    public async Task RemoveSecurityRecordAsync(int recordId, CancellationToken ct = default)
        => await RemoveChildAsync<PermitSecurityRecord>(recordId, ct);

    public async Task<int> AddDependantAsync(int permitId, DependantRequest request, CancellationToken ct = default)
    {
        RequireDependantName(request);
        var dependant = new PermitDependant { PermitId = permitId };
        ApplyDependant(dependant, request);
        _db.Set<PermitDependant>().Add(dependant);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(PermitDependant), dependant.Id.ToString(),
            $"{dependant.Surname} {dependant.GivenName}".Trim(), ct);
        return dependant.Id;
    }

    public async Task UpdateDependantAsync(int dependantId, DependantRequest request, CancellationToken ct = default)
    {
        RequireDependantName(request);
        var dependant = await _db.Set<PermitDependant>().SingleOrDefaultAsync(d => d.Id == dependantId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", dependantId.ToString());
        ApplyDependant(dependant, request);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(PermitDependant), dependantId.ToString(),
            $"{dependant.Surname} {dependant.GivenName}".Trim(), ct);
    }

    public async Task RemoveDependantAsync(int dependantId, CancellationToken ct = default)
        => await RemoveChildAsync<PermitDependant>(dependantId, ct);

    // ---- Aides ---------------------------------------------------------------

    private async Task RemoveChildAsync<T>(int id, CancellationToken ct) where T : class
    {
        var entity = await _db.Set<T>().FindAsync([id], ct);
        if (entity is null) return;
        _db.Set<T>().Remove(entity);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, typeof(T).Name, id.ToString(), "Suppression", ct);
    }

    private static void RequireDependantName(DependantRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Surname) && string.IsNullOrWhiteSpace(r.GivenName))
            throw new DomainValidationException("Admin_Error_NameRequired");
    }

    private static void ApplyDependant(PermitDependant d, DependantRequest r)
    {
        d.Surname = Clean(r.Surname)?.ToUpperInvariant();
        d.GivenName = Clean(r.GivenName)?.ToUpperInvariant();
        d.OtherNames = Clean(r.OtherNames);
        d.DateOfBirth = r.DateOfBirth;
        d.CountryOfBirthId = r.CountryOfBirthId;
        d.RelationId = r.RelationId;
        d.DocumentTypeId = r.DocumentTypeId;
        d.DocumentNumber = Clean(r.DocumentNumber);
        d.CitizenshipId = r.CitizenshipId;
        d.SexId = r.SexId;
        d.PhoneNumber = Clean(r.PhoneNumber);
        d.UpdateDate = DateTime.Now;
    }

    private async Task ValidateAsync(PermitSaveRequest request, CancellationToken ct)
    {
        if (!await _db.Set<TravelDocument>().AnyAsync(d => d.Id == request.DocumentId, ct))
            throw new DomainValidationException("PAL_Error_DocumentNotFound", request.DocumentId.ToString());

        if (request.IssueDate is { } issue && request.ExpireDate is { } expire && expire.Date < issue.Date)
            throw new DomainValidationException("VISA_Error_DatesInvalid");

        if (request.PermitTypeId is { } typeId
            && !await _db.Set<Lookup>().AnyAsync(l => l.Id == typeId && l.Type!.Code == "PERMIT_TYPE" && !l.Revoke, ct))
            throw new DomainValidationException("PV_Error_TypeRequired");

        if (request.StatusId is { } statusId
            && !await _db.Set<Lookup>().AnyAsync(l => l.Id == statusId && l.Type!.Code == "PERMIT_STATUS" && !l.Revoke, ct))
            throw new DomainValidationException("PV_Error_StatusUnknown", statusId.ToString());
    }

    /// <summary>Numérotation : PERMIT-AAAA-NNNNNN (héritage : PermitTemp / numérotation paramétrée).</summary>
    private async Task<string> GenerateNumberAsync(CancellationToken ct)
    {
        var prefix = $"PERMIT-{_clock.Now.Year}-";
        var count = await _db.Set<Permit>().CountAsync(p => p.Number != null && p.Number.StartsWith(prefix), ct);
        for (var i = 1; i <= 1000; i++)
        {
            var candidate = $"{prefix}{count + i:D6}";
            if (!await _db.Set<Permit>().AnyAsync(p => p.Number == candidate, ct)) return candidate;
        }
        throw new DomainValidationException("PV_Error_NumberGeneration");
    }

    private static void Apply(Permit permit, PermitSaveRequest request)
    {
        permit.DocumentId = request.DocumentId;
        permit.VisaId = request.VisaId;
        permit.PermitTypeId = request.PermitTypeId;
        permit.IssueDate = request.IssueDate;
        permit.ExpireDate = request.ExpireDate;
        permit.StatusId = request.StatusId;
        permit.MaritalStatusId = request.MaritalStatusId;
        permit.OccupationId = request.OccupationId;
        permit.OccupationOther = Clean(request.OccupationOther);
        permit.QualificationId = request.QualificationId;
        permit.CourseOfStudyId = request.CourseOfStudyId;
        permit.BloodTypeId = request.BloodTypeId;
        permit.DocumentIssuingAuthority = Clean(request.DocumentIssuingAuthority);
        permit.Note = Clean(request.Note);
    }

    private PermitListItem ToList(Permit p)
    {
        var now = _clock.Now;
        var valid = !p.Revoke
                    && (p.IssueDate == null || p.IssueDate <= now)
                    && (p.ExpireDate == null || p.ExpireDate >= now);
        return new PermitListItem(p.Id, p.Number, p.Document?.DocumentNumber ?? string.Empty,
            p.Document is null ? null : $"{p.Document.Surname} {p.Document.GivenName}".Trim(),
            p.PermitType is null ? null : MovementService.DescriptionOf(p.PermitType, "fr"),
            p.Status is null ? null : MovementService.DescriptionOf(p.Status, "fr"),
            p.IssueDate, p.ExpireDate, p.Revoke, valid);
    }

    private static int? NonNegative(int? value) => value is < 0 ? 0 : value;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
