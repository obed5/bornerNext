using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Ligne de résultat de recherche d'un document de voyage.</summary>
public sealed record DocumentListItem(int Id, int PersonId, string DocumentNumber, string DocumentTypeFr,
    string? Surname, string? GivenName, DateTime? DateOfBirth, string? CitizenshipFr, DateTime? ExpireDate,
    DateTime? LastMovementDate, bool? LastWasEntry);

/// <summary>Détail complet d'un document (écran passeport / recherche).</summary>
public sealed record DocumentDetail(int Id, int PersonId, string DocumentNumber, int DocumentTypeId,
    string DocumentTypeFr, int? CountryOfIssueId, DateTime? IssueDate, DateTime? ExpireDate,
    string? Surname, string? GivenName, string? FatherName, DateTime? DateOfBirth, int? SexId, int? CitizenshipId,
    string? CitizenshipFr, string? PersonalNumber, bool HasPhoto, bool HasScannedDocument);

/// <summary>Données saisies d'un document (création ou mise à jour).</summary>
public sealed record DocumentSaveRequest(string? DocumentNumber, int DocumentTypeId, int? CountryOfIssueId,
    DateTime? IssueDate, DateTime? ExpireDate, string? Surname, string? GivenName, string? FatherName,
    DateTime? DateOfBirth, int? SexId, int? CitizenshipId, string? PersonalNumber);

/// <summary>
/// Recherche et gestion des documents de voyage et de leurs titulaires
/// (héritage : Documents + Persons + Photos).
/// </summary>
public class DocumentService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public DocumentService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>
    /// Recherche multicritère (n° de document, nom, prénom, naissance, nationalité).
    /// Au moins un critère est requis (garde-fou : pas d'extraction de masse).
    /// </summary>
    public async Task<IReadOnlyList<DocumentListItem>> SearchAsync(string? number = null, string? surname = null,
        string? givenName = null, DateTime? dateOfBirth = null, int? citizenshipId = null, int take = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(number) && string.IsNullOrWhiteSpace(surname)
            && string.IsNullOrWhiteSpace(givenName) && dateOfBirth is null && citizenshipId is null)
            throw new DomainValidationException("PAL_Error_SearchCriteriaRequired");

        var query = _db.Set<TravelDocument>()
            .Include(d => d.DocumentType!).ThenInclude(t => t.Cultures)
            .Include(d => d.Citizenship!).ThenInclude(c => c.Cultures)
            .AsQueryable();

        // Comparaison insensible à la casse des deux côtés (état civil stocké en
        // majuscules ; comportement identique SQL Server et fournisseurs de test).
        if (!string.IsNullOrWhiteSpace(number))
        {
            var upper = number.Trim().ToUpperInvariant();
            query = query.Where(d => d.DocumentNumber.ToUpper().Contains(upper));
        }
        if (!string.IsNullOrWhiteSpace(surname))
        {
            var upper = surname.Trim().ToUpperInvariant();
            query = query.Where(d => d.Surname != null && d.Surname.ToUpper().Contains(upper));
        }
        if (!string.IsNullOrWhiteSpace(givenName))
        {
            var upper = givenName.Trim().ToUpperInvariant();
            query = query.Where(d => d.GivenName != null && d.GivenName.ToUpper().Contains(upper));
        }
        if (dateOfBirth is { } dob)
            query = query.Where(d => d.DateOfBirth != null && d.DateOfBirth.Value.Date == dob.Date);
        if (citizenshipId is { } citizenship)
            query = query.Where(d => d.CitizenshipId == citizenship);

        var documents = await query.OrderBy(d => d.Surname).ThenBy(d => d.GivenName).Take(take).ToListAsync(ct);
        var ids = documents.Select(d => d.Id).ToList();

        var lastMovements = await _db.Set<Movement>()
            .Where(m => ids.Contains(m.DocumentId))
            .GroupBy(m => m.DocumentId)
            .Select(g => new { DocumentId = g.Key, Id = g.Max(m => m.Id) })
            .ToListAsync(ct);
        var movementIds = lastMovements.Select(m => m.Id).ToList();
        var movements = await _db.Set<Movement>().Include(m => m.Direction)
            .Where(m => movementIds.Contains(m.Id)).ToListAsync(ct);
        var lastByDocument = movements.ToDictionary(m => m.DocumentId, m => m);

        return documents.Select(d =>
        {
            lastByDocument.TryGetValue(d.Id, out var last);
            return new DocumentListItem(d.Id, d.PersonId, d.DocumentNumber,
                DescriptionOf(d.DocumentType?.Cultures, "fr"), d.Surname, d.GivenName, d.DateOfBirth,
                NameOf(d.Citizenship?.Cultures, "fr"), d.ExpireDate,
                last?.TravelDate, last is null ? null : IsEntryCode(last.Direction?.Code));
        }).ToList();
    }

    public async Task<DocumentDetail?> GetAsync(int documentId, CancellationToken ct = default)
    {
        var d = await _db.Set<TravelDocument>()
            .Include(x => x.DocumentType!).ThenInclude(t => t.Cultures)
            .Include(x => x.Citizenship!).ThenInclude(c => c.Cultures)
            .Include(x => x.Image)
            .SingleOrDefaultAsync(x => x.Id == documentId, ct);
        if (d is null) return null;

        return new DocumentDetail(d.Id, d.PersonId, d.DocumentNumber, d.DocumentTypeId,
            DescriptionOf(d.DocumentType?.Cultures, "fr"), d.CountryOfIssueId, d.IssueDate, d.ExpireDate,
            d.Surname, d.GivenName, d.FatherName, d.DateOfBirth, d.SexId, d.CitizenshipId,
            NameOf(d.Citizenship?.Cultures, "fr"), d.PersonalNumber,
            d.Image?.Photo is { Length: > 0 }, d.Image?.ScannedDocument is { Length: > 0 });
    }

    /// <summary>Crée le couple personne + document (une personne est créée par nouveau document).</summary>
    public async Task<int> CreateAsync(DocumentSaveRequest request, CancellationToken ct = default)
    {
        Validate(request, requireNumber: true);

        var person = new Person
        {
            BorderPointId = _currentUser.BorderPointId,
            UpdateDate = _clock.Now,
            UpdateUser = _currentUser.UserId
        };
        var document = BuildDocument(request);
        document.CreateDate = _clock.Now;
        document.CreatorUser = _currentUser.UserId;
        document.BorderPointId = _currentUser.BorderPointId;
        document.Person = person;

        _db.Set<TravelDocument>().Add(document);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(TravelDocument), document.Id.ToString(),
            $"{document.DocumentNumber} — {document.Surname} {document.GivenName}", ct);
        return document.Id;
    }

    /// <summary>
    /// Recherche le document par (type, numéro) — clé naturelle héritée — et le
    /// réutilise pour l'enregistrement ; sinon crée personne + document.
    /// </summary>
    public async Task<int> FindOrCreateAsync(DocumentSaveRequest request, CancellationToken ct = default)
    {
        Validate(request, requireNumber: true);
        var number = request.DocumentNumber!.Trim();

        var existing = await _db.Set<TravelDocument>()
            .SingleOrDefaultAsync(d => d.DocumentTypeId == request.DocumentTypeId && d.DocumentNumber == number, ct);
        if (existing is not null) return existing.Id;

        return await CreateAsync(request, ct);
    }

    public async Task UpdateAsync(int documentId, DocumentSaveRequest request, CancellationToken ct = default)
    {
        Validate(request, requireNumber: true);
        var document = await _db.Set<TravelDocument>().SingleOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new DomainValidationException("PAL_Error_DocumentNotFound", documentId.ToString());

        var duplicate = await _db.Set<TravelDocument>().AnyAsync(d =>
            d.DocumentTypeId == request.DocumentTypeId && d.DocumentNumber == request.DocumentNumber!.Trim() && d.Id != documentId, ct);
        if (duplicate) throw new DomainValidationException("PAL_Error_DocumentDuplicate", request.DocumentNumber);

        Apply(document, request);
        document.UpdateDate = _clock.Now;
        document.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(TravelDocument), document.Id.ToString(),
            $"{document.DocumentNumber}", ct);
    }

    /// <summary>Associe la photo du titulaire (héritage : Photos.Photo).</summary>
    public async Task SetPhotoAsync(int documentId, byte[]? photo, CancellationToken ct = default)
        => await SetImageAsync(documentId, photo, null, ct);

    /// <summary>Associe le document scanné (héritage : Photos.ScannedDocument).</summary>
    public async Task SetScannedDocumentAsync(int documentId, byte[]? scan, CancellationToken ct = default)
        => await SetImageAsync(documentId, null, scan, ct);

    private async Task SetImageAsync(int documentId, byte[]? photo, byte[]? scan, CancellationToken ct)
    {
        if (!await _db.Set<TravelDocument>().AnyAsync(d => d.Id == documentId, ct))
            throw new DomainValidationException("PAL_Error_DocumentNotFound", documentId.ToString());

        var image = await _db.Set<DocumentImage>().SingleOrDefaultAsync(i => i.DocumentId == documentId, ct);
        if (image is null)
        {
            image = new DocumentImage { DocumentId = documentId };
            _db.Set<DocumentImage>().Add(image);
        }
        if (photo is not null) image.Photo = photo.Length == 0 ? null : photo;
        if (scan is not null) image.ScannedDocument = scan.Length == 0 ? null : scan;
        image.UpdateDate = _clock.Now;
        image.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(DocumentImage), documentId.ToString(),
            photo is not null ? "Photo du titulaire mise à jour" : "Document scanné mis à jour", ct);
    }

    public async Task<(byte[]? Photo, byte[]? Scan)> GetImagesAsync(int documentId, CancellationToken ct = default)
    {
        var image = await _db.Set<DocumentImage>().SingleOrDefaultAsync(i => i.DocumentId == documentId, ct);
        return (image?.Photo, image?.ScannedDocument);
    }

    /// <summary>Un mouvement d'entrée ? (nomenclature MOVEMENT_DIRECTION, code hérité Entry).</summary>
    internal static bool IsEntryCode(string? directionCode)
        => string.Equals(directionCode, "Entry", StringComparison.OrdinalIgnoreCase);

    private static string DescriptionOf(IEnumerable<LookupCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Description
           ?? cultures?.FirstOrDefault()?.Description ?? string.Empty;

    private static string NameOf(IEnumerable<CountryCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Name
           ?? cultures?.FirstOrDefault()?.Name ?? string.Empty;

    private static void Validate(DocumentSaveRequest request, bool requireNumber)
    {
        if (requireNumber && string.IsNullOrWhiteSpace(request.DocumentNumber))
            throw new DomainValidationException("PAL_Error_DocumentNumberRequired");
    }

    private TravelDocument BuildDocument(DocumentSaveRequest request)
    {
        var document = new TravelDocument();
        Apply(document, request);
        return document;
    }

    private static void Apply(TravelDocument document, DocumentSaveRequest request)
    {
        document.DocumentNumber = request.DocumentNumber!.Trim();
        document.DocumentTypeId = request.DocumentTypeId;
        document.CountryOfIssueId = request.CountryOfIssueId;
        document.IssueDate = request.IssueDate;
        document.ExpireDate = request.ExpireDate;
        document.Surname = Clean(request.Surname)?.ToUpperInvariant();
        document.GivenName = Clean(request.GivenName)?.ToUpperInvariant();
        document.FatherName = Clean(request.FatherName);
        document.DateOfBirth = request.DateOfBirth;
        document.SexId = request.SexId;
        document.CitizenshipId = request.CitizenshipId;
        document.PersonalNumber = Clean(request.PersonalNumber);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
