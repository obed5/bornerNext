using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Données d'émission ou de mise à jour d'un visa (héritage : Visa).</summary>
public sealed record VisaRequest(int VisaTypeId, int? VisaEntryId, DateTime? IssueDate, DateTime? ExpireDate,
    short? Duration, int? DocumentId, string? Number, string? ReferenceNumber, string? ReceiptNumber,
    string? Address, string? ContactNumber, string? Employer, string? InvitedBy, int? RelationId,
    int? UniversityId, int? PermitStatusId, string? Occupation, int? PeriodTypeId, string? Note,
    string? VoucherNumber, int? PermitConditionId, int? ProvinceId, string? Barcode);

/// <summary>Ligne de liste des visas.</summary>
public sealed record VisaListItem(int Id, string? Number, string TypeFr, string? EntryFr, DateTime? IssueDate,
    DateTime? ExpireDate, short? Duration, string? DocumentNumber, string? HolderName, bool Revoke, bool IsValid);

/// <summary>Enfant rattaché à un visa (héritage : VisaChildren).</summary>
public sealed record VisaChildDto(int Id, string? Surname, string? GivenName, DateTime? DateOfBirth,
    int? SexId, int? CitizenshipId, int? DocumentTypeId, string? DocumentNumber);

/// <summary>
/// Gestion des visas (émission, mise à jour, révocation, enfants) —
/// héritage : Visa + VisaChildren + VisaNumberSettings (numérotation).
/// </summary>
public class VisaService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public VisaService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>Émet un visa. Le numéro est généré si absent (format VISA-AAAA-NNNNNN).</summary>
    public async Task<int> CreateAsync(VisaRequest request, CancellationToken ct = default)
    {
        await ValidateAsync(request, ct);
        if (request.DocumentId is { } documentId
            && !await _db.Set<TravelDocument>().AnyAsync(d => d.Id == documentId, ct))
            throw new DomainValidationException("PAL_Error_DocumentNotFound", documentId.ToString());

        var visa = new Visa
        {
            VisaTypeId = request.VisaTypeId,
            CreateDate = _clock.Now,
            CreatorUser = _currentUser.UserId,
            BorderPointId = _currentUser.BorderPointId,
            PcNumber = _currentUser.PcNumber
        };
        Apply(visa, request);
        visa.Number = string.IsNullOrWhiteSpace(request.Number)
            ? await GenerateNumberAsync(ct)
            : request.Number!.Trim();

        _db.Set<Visa>().Add(visa);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Visa), visa.Id.ToString(),
            $"Visa {visa.Number} ({visa.VisaTypeId})", ct);
        return visa.Id;
    }

    public async Task UpdateAsync(int visaId, VisaRequest request, CancellationToken ct = default)
    {
        await ValidateAsync(request, ct);
        var visa = await _db.Set<Visa>().SingleOrDefaultAsync(v => v.Id == visaId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", visaId.ToString());

        Apply(visa, request);
        if (!string.IsNullOrWhiteSpace(request.Number)) visa.Number = request.Number!.Trim();
        visa.UpdateDate = _clock.Now;
        visa.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Visa), visa.Id.ToString(), $"Visa {visa.Number}", ct);
    }

    public async Task SetRevokedAsync(int visaId, bool revoke, CancellationToken ct = default)
    {
        var visa = await _db.Set<Visa>().SingleOrDefaultAsync(v => v.Id == visaId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", visaId.ToString());

        visa.Revoke = revoke;
        visa.RevokeDate = revoke ? _clock.Now : null;
        visa.UpdateDate = _clock.Now;
        visa.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Visa), visa.Id.ToString(),
            $"{(revoke ? "Révocation" : "Réactivation")} visa {visa.Number}", ct);
    }

    public async Task<VisaRequest?> GetAsync(int visaId, CancellationToken ct = default)
    {
        var v = await _db.Set<Visa>().SingleOrDefaultAsync(x => x.Id == visaId, ct);
        if (v is null) return null;
        return new VisaRequest(v.VisaTypeId, v.VisaEntryId, v.IssueDate, v.ExpireDate, v.Duration, v.DocumentId,
            v.Number, v.ReferenceNumber, v.ReceiptNumber, v.Address, v.ContactNumber, v.Employer, v.InvitedBy,
            v.RelationId, v.UniversityId, v.PermitStatusId, v.Occupation, v.PeriodTypeId, v.Note,
            v.VoucherNumber, v.PermitConditionId, v.ProvinceId, v.Barcode);
    }

    public async Task<IReadOnlyList<VisaListItem>> ListByDocumentAsync(int documentId, bool includeRevoked = false, CancellationToken ct = default)
    {
        var visas = await LoadVisasAsync(_db.Set<Visa>().Where(v => v.DocumentId == documentId), includeRevoked, ct);
        return visas.OrderByDescending(v => v.ExpireDate).Select(ToListItem).ToList();
    }

    public async Task<IReadOnlyList<VisaListItem>> SearchAsync(string? number = null, bool includeRevoked = false,
        int take = 200, CancellationToken ct = default)
    {
        var query = _db.Set<Visa>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(number))
            query = query.Where(v => v.Number != null && v.Number.Contains(number));

        var visas = await LoadVisasAsync(query, includeRevoked, ct);
        return visas.OrderByDescending(v => v.CreateDate).Take(take).Select(ToListItem).ToList();
    }

    /// <summary>Le visa est-il révoqué ? (état courant, pour basculer le libellé du bouton).</summary>
    public async Task<bool> IsRevokedAsync(int visaId, CancellationToken ct = default)
        => await _db.Set<Visa>().Where(v => v.Id == visaId).Select(v => (bool?)v.Revoke).FirstOrDefaultAsync(ct) ?? false;

    /// <summary>Visa valide d'un document à une date donnée (contrôle « visa de sortie »).</summary>
    public async Task<int?> FindValidVisaAsync(int documentId, DateTime atDate, CancellationToken ct = default)
        => await _db.Set<Visa>()
            .Where(v => v.DocumentId == documentId && !v.Revoke
                        && (v.ExpireDate == null || v.ExpireDate >= atDate)
                        && (v.IssueDate == null || v.IssueDate <= atDate))
            .OrderByDescending(v => v.ExpireDate)
            .Select(v => (int?)v.Id)
            .FirstOrDefaultAsync(ct);

    // ---- Enfants du visa -----------------------------------------------------

    public async Task<IReadOnlyList<VisaChildDto>> GetChildrenAsync(int visaId, CancellationToken ct = default)
    {
        var children = await _db.Set<VisaChild>()
            .Where(c => c.VisaId == visaId && !c.IsDeleted)
            .OrderBy(c => c.Surname)
            .ToListAsync(ct);
        return children.Select(c => new VisaChildDto(c.Id, c.Surname, c.GivenName, c.DateOfBirth, c.SexId,
            c.CitizenshipId, c.DocumentTypeId, c.DocumentNumber)).ToList();
    }

    public async Task<int> AddChildAsync(int visaId, VisaChildDto child, CancellationToken ct = default)
    {
        if (!await _db.Set<Visa>().AnyAsync(v => v.Id == visaId, ct))
            throw new DomainValidationException("Admin_Error_ItemNotFound", visaId.ToString());

        var entity = new VisaChild
        {
            VisaId = visaId,
            Surname = Clean(child.Surname)?.ToUpperInvariant(),
            GivenName = Clean(child.GivenName)?.ToUpperInvariant(),
            DateOfBirth = child.DateOfBirth,
            SexId = child.SexId,
            CitizenshipId = child.CitizenshipId,
            DocumentTypeId = child.DocumentTypeId,
            DocumentNumber = Clean(child.DocumentNumber),
            BorderPointId = _currentUser.BorderPointId,
            UpdateDate = _clock.Now,
            UpdateUser = _currentUser.UserId
        };
        _db.Set<VisaChild>().Add(entity);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(VisaChild), entity.Id.ToString(),
            $"{entity.Surname} {entity.GivenName}", ct);
        return entity.Id;
    }

    public async Task UpdateChildAsync(int childId, VisaChildDto child, CancellationToken ct = default)
    {
        var entity = await _db.Set<VisaChild>().SingleOrDefaultAsync(c => c.Id == childId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", childId.ToString());

        entity.Surname = Clean(child.Surname)?.ToUpperInvariant();
        entity.GivenName = Clean(child.GivenName)?.ToUpperInvariant();
        entity.DateOfBirth = child.DateOfBirth;
        entity.SexId = child.SexId;
        entity.CitizenshipId = child.CitizenshipId;
        entity.DocumentTypeId = child.DocumentTypeId;
        entity.DocumentNumber = Clean(child.DocumentNumber);
        entity.UpdateDate = _clock.Now;
        entity.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Retrait logique d'un enfant du visa (héritage : IsDeleted).</summary>
    public async Task RemoveChildAsync(int childId, CancellationToken ct = default)
    {
        var entity = await _db.Set<VisaChild>().SingleOrDefaultAsync(c => c.Id == childId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", childId.ToString());

        entity.IsDeleted = true;
        entity.UpdateDate = _clock.Now;
        entity.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(VisaChild), childId.ToString(),
            $"{entity.Surname} {entity.GivenName}", ct);
    }

    // ---- Aides ---------------------------------------------------------------

    private async Task ValidateAsync(VisaRequest request, CancellationToken ct)
    {
        var typeOk = await _db.Set<Lookup>().AnyAsync(l => l.Id == request.VisaTypeId && l.Type!.Code == "VISA_TYPE" && !l.Revoke, ct);
        if (!typeOk) throw new DomainValidationException("VISA_Error_TypeRequired");

        if (request.VisaEntryId is { } entryId
            && !await _db.Set<Lookup>().AnyAsync(l => l.Id == entryId && l.Type!.Code == "VISA_ENTRY" && !l.Revoke, ct))
            throw new DomainValidationException("VISA_Error_EntryUnknown", entryId.ToString());

        if (request.IssueDate is { } issue && request.ExpireDate is { } expire && expire.Date < issue.Date)
            throw new DomainValidationException("VISA_Error_DatesInvalid");
        if (request.Duration is < 0)
            throw new DomainValidationException("VISA_Error_DurationInvalid");
    }

    /// <summary>Numérotation héritée (VisaNumberSettings) : VISA-AAAA-NNNNNN, séquence annuelle.</summary>
    private async Task<string> GenerateNumberAsync(CancellationToken ct)
    {
        var year = _clock.Now.Year;
        var prefix = $"VISA-{year}-";
        var count = await _db.Set<Visa>().CountAsync(v => v.Number != null && v.Number.StartsWith(prefix), ct);
        for (var i = 1; i <= 1000; i++)
        {
            var candidate = $"{prefix}{count + i:D6}";
            if (!await _db.Set<Visa>().AnyAsync(v => v.Number == candidate, ct)) return candidate;
        }
        throw new DomainValidationException("VISA_Error_NumberGeneration");
    }

    private async Task<List<Visa>> LoadVisasAsync(IQueryable<Visa> query, bool includeRevoked, CancellationToken ct)
    {
        if (!includeRevoked) query = query.Where(v => !v.Revoke);
        return await query
            .Include(v => v.VisaType!).ThenInclude(t => t.Cultures)
            .Include(v => v.VisaEntry!).ThenInclude(t => t.Cultures)
            .Include(v => v.Document)
            .ToListAsync(ct);
    }

    private static VisaListItem ToListItem(Visa v)
    {
        var now = DateTime.Now;
        var valid = !v.Revoke
                    && (v.IssueDate == null || v.IssueDate <= now)
                    && (v.ExpireDate == null || v.ExpireDate >= now);
        return new VisaListItem(v.Id, v.Number, MovementService.DescriptionOf(v.VisaType, "fr"),
            v.VisaEntry is null ? null : MovementService.DescriptionOf(v.VisaEntry, "fr"),
            v.IssueDate, v.ExpireDate, v.Duration, v.Document?.DocumentNumber,
            v.Document is null ? null : $"{v.Document.Surname} {v.Document.GivenName}".Trim(),
            v.Revoke, valid);
    }

    private static void Apply(Visa visa, VisaRequest request)
    {
        visa.VisaTypeId = request.VisaTypeId;
        visa.VisaEntryId = request.VisaEntryId;
        visa.IssueDate = request.IssueDate;
        visa.ExpireDate = request.ExpireDate;
        visa.Duration = request.Duration;
        visa.DocumentId = request.DocumentId;
        visa.ReferenceNumber = Clean(request.ReferenceNumber);
        visa.ReceiptNumber = Clean(request.ReceiptNumber);
        visa.Address = Clean(request.Address);
        visa.ContactNumber = Clean(request.ContactNumber);
        visa.Employer = Clean(request.Employer);
        visa.InvitedBy = Clean(request.InvitedBy);
        visa.RelationId = request.RelationId;
        visa.UniversityId = request.UniversityId;
        visa.PermitStatusId = request.PermitStatusId;
        visa.Occupation = Clean(request.Occupation);
        visa.PeriodTypeId = request.PeriodTypeId;
        visa.Note = Clean(request.Note);
        visa.VoucherNumber = Clean(request.VoucherNumber);
        visa.PermitConditionId = request.PermitConditionId;
        visa.ProvinceId = request.ProvinceId;
        visa.Barcode = Clean(request.Barcode);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
