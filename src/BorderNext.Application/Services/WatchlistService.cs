using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Demande de création/mise à jour d'une entrée de liste d'alerte personnes
/// (héritage : PersonsStopListForm).</summary>
public sealed record PersonStopListRequest(
    string? Surname = null,
    string? GivenName = null,
    string? FathersName = null,
    string? SurnameAlt = null,
    string? GivenNameAlt = null,
    string? FathersNameAlt = null,
    DateTime? DateOfBirth = null,
    int? SexId = null,
    int? CitizenshipId = null,
    string? WhoSetUp = null,
    int? StopListSourceId = null,
    string? SourceDocument = null,
    string? Info = null,
    byte[]? Photo = null,
    int? DocumentTypeId = null,
    string? DocumentNumber = null,
    string? PersonalNumber = null,
    string? ActionRequired = null,
    int? MatchInStopListActionId = null);

/// <summary>Demande de création/mise à jour d'une entrée de liste d'alerte documents
/// (héritage : DocumentStopListForm).</summary>
public sealed record DocumentStopListRequest(
    int? DocumentTypeId = null,
    string? DocumentNumber = null,
    string? PersonalNumber = null,
    int? CountryId = null,
    string? Surname = null,
    string? GivenName = null,
    string? FathersName = null,
    DateTime? DateOfBirth = null,
    int? SexId = null,
    int? StopListSourceId = null,
    string? SourceDocument = null,
    string? Info = null,
    string? ActionRequired = null,
    int? MatchInStopListActionId = null);

/// <summary>Critères de recherche des listes d'alerte personnes
/// (héritage : GetPersonsStopListByCondition ; correspondance partielle « LIKE » insensible à la casse).</summary>
public sealed record PersonStopListSearch(
    string? Surname = null,
    string? GivenName = null,
    string? DocumentNumber = null,
    int? CitizenshipId = null,
    int? StopListSourceId = null,
    string? SourceDocument = null,
    string? Info = null,
    DateTime? DateOfBirthFrom = null,
    DateTime? DateOfBirthTo = null,
    DateTime? SetUpDateFrom = null,
    DateTime? SetUpDateTo = null,
    DateTime? CancelDateFrom = null,
    DateTime? CancelDateTo = null,
    bool? IsAuthorized = null,
    bool ShowCancelled = false);

/// <summary>Critères de recherche des listes d'alerte documents
/// (héritage : GetDocumentsStopListByCondition).</summary>
public sealed record DocumentStopListSearch(
    string? Surname = null,
    string? GivenName = null,
    string? DocumentNumber = null,
    int? CountryId = null,
    int? StopListSourceId = null,
    string? SourceDocument = null,
    string? Info = null,
    DateTime? DateOfBirthFrom = null,
    DateTime? DateOfBirthTo = null,
    DateTime? SetUpDateFrom = null,
    DateTime? SetUpDateTo = null,
    DateTime? CancelDateFrom = null,
    DateTime? CancelDateTo = null,
    bool ShowCancelled = false);

/// <summary>Empreinte stockée sur une entrée de liste d'alerte, avec score éventuel.</summary>
public sealed record StopListFingerprintMatch(int PersonStopListId, short FingerNumber, int? Score);

/// <summary>
/// Gestion des listes d'alerte personnes et documents (héritage : PersonsStopListForm,
/// DocumentStopListForm, StopListSourceForm) : création, modification, annulation
/// motivée (source + document), autorisation de lever l'alerte, empreintes de liste
/// et identification d'une empreinte capturée contre ces empreintes.
///
/// Fidélité : une entrée dont la date d'annulation est future reste active
/// (prédicat hérité <c>CancelDate IS NULL OR CancelDate &gt; GETDATE()</c>).
/// </summary>
public class WatchlistService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IFingerprintMatcher _matcher;

    public WatchlistService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit, IFingerprintMatcher matcher)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _matcher = matcher;
    }

    // ---- Recherche -----------------------------------------------------------

    public async Task<IReadOnlyList<PersonStopList>> SearchPersonsAsync(PersonStopListSearch search, int take = 200, CancellationToken ct = default)
    {
        var query = _db.Set<PersonStopList>()
            .Include(p => p.Citizenship!).ThenInclude(c => c.Cultures)
            .Include(p => p.StopListSource!).ThenInclude(s => s.Cultures)
            .Include(p => p.MatchInStopListAction!).ThenInclude(a => a.Cultures)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search.Surname))
        {
            var upper = search.Surname.ToUpper();
            query = query.Where(p => (p.Surname != null && p.Surname.ToUpper().Contains(upper))
                                     || (p.SurnameAlt != null && p.SurnameAlt.ToUpper().Contains(upper)));
        }
        if (!string.IsNullOrWhiteSpace(search.GivenName))
        {
            var upper = search.GivenName.ToUpper();
            query = query.Where(p => (p.GivenName != null && p.GivenName.ToUpper().Contains(upper))
                                     || (p.GivenNameAlt != null && p.GivenNameAlt.ToUpper().Contains(upper)));
        }
        if (!string.IsNullOrWhiteSpace(search.DocumentNumber))
        {
            var upper = search.DocumentNumber.ToUpper();
            query = query.Where(p => p.DocumentNumber != null && p.DocumentNumber.ToUpper().Contains(upper));
        }
        if (search.CitizenshipId is { } citizenship) query = query.Where(p => p.CitizenshipId == citizenship);
        if (search.StopListSourceId is { } source) query = query.Where(p => p.StopListSourceId == source);
        if (!string.IsNullOrWhiteSpace(search.SourceDocument))
        {
            var upper = search.SourceDocument.ToUpper();
            query = query.Where(p => p.SourceDocument != null && p.SourceDocument.ToUpper().Contains(upper));
        }
        if (!string.IsNullOrWhiteSpace(search.Info))
        {
            var upper = search.Info.ToUpper();
            query = query.Where(p => p.Info != null && p.Info.ToUpper().Contains(upper));
        }
        if (search.DateOfBirthFrom is { } dobFrom) query = query.Where(p => p.DateOfBirth >= dobFrom.Date);
        if (search.DateOfBirthTo is { } dobTo) query = query.Where(p => p.DateOfBirth <= dobTo.Date.AddDays(1).AddSeconds(-1));
        if (search.SetUpDateFrom is { } setUpFrom) query = query.Where(p => p.SetUpDate >= setUpFrom.Date);
        if (search.SetUpDateTo is { } setUpTo) query = query.Where(p => p.SetUpDate <= setUpTo.Date.AddDays(1).AddSeconds(-1));
        if (search.CancelDateFrom is { } cancelFrom) query = query.Where(p => p.CancelDate >= cancelFrom.Date);
        if (search.CancelDateTo is { } cancelTo) query = query.Where(p => p.CancelDate <= cancelTo.Date.AddDays(1).AddSeconds(-1));
        if (search.IsAuthorized is { } authorized) query = query.Where(p => p.IsAuthorized == authorized);
        if (!search.ShowCancelled) query = query.Where(p => p.CancelDate == null);

        return await query
            .OrderByDescending(p => p.UpdateDate)
            .ThenBy(p => p.Surname)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentStopList>> SearchDocumentsAsync(DocumentStopListSearch search, int take = 200, CancellationToken ct = default)
    {
        var query = _db.Set<DocumentStopList>()
            .Include(d => d.Country!).ThenInclude(c => c.Cultures)
            .Include(d => d.StopListSource!).ThenInclude(s => s.Cultures)
            .Include(d => d.MatchInStopListAction!).ThenInclude(a => a.Cultures)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search.Surname))
        {
            var upper = search.Surname.ToUpper();
            query = query.Where(d => d.Surname != null && d.Surname.ToUpper().Contains(upper));
        }
        if (!string.IsNullOrWhiteSpace(search.GivenName))
        {
            var upper = search.GivenName.ToUpper();
            query = query.Where(d => d.GivenName != null && d.GivenName.ToUpper().Contains(upper));
        }
        if (!string.IsNullOrWhiteSpace(search.DocumentNumber))
        {
            var upper = search.DocumentNumber.ToUpper();
            query = query.Where(d => d.DocumentNumber != null && d.DocumentNumber.ToUpper().Contains(upper));
        }
        if (search.CountryId is { } country) query = query.Where(d => d.CountryId == country);
        if (search.StopListSourceId is { } source) query = query.Where(d => d.StopListSourceId == source);
        if (!string.IsNullOrWhiteSpace(search.SourceDocument))
        {
            var upper = search.SourceDocument.ToUpper();
            query = query.Where(d => d.SourceDocument != null && d.SourceDocument.ToUpper().Contains(upper));
        }
        if (!string.IsNullOrWhiteSpace(search.Info))
        {
            var upper = search.Info.ToUpper();
            query = query.Where(d => d.Info != null && d.Info.ToUpper().Contains(upper));
        }
        if (search.DateOfBirthFrom is { } dobFrom) query = query.Where(d => d.DateOfBirth >= dobFrom.Date);
        if (search.DateOfBirthTo is { } dobTo) query = query.Where(d => d.DateOfBirth <= dobTo.Date.AddDays(1).AddSeconds(-1));
        if (search.SetUpDateFrom is { } setUpFrom) query = query.Where(d => d.SetUpDate >= setUpFrom.Date);
        if (search.SetUpDateTo is { } setUpTo) query = query.Where(d => d.SetUpDate <= setUpTo.Date.AddDays(1).AddSeconds(-1));
        if (search.CancelDateFrom is { } cancelFrom) query = query.Where(d => d.CancelDate >= cancelFrom.Date);
        if (search.CancelDateTo is { } cancelTo) query = query.Where(d => d.CancelDate <= cancelTo.Date.AddDays(1).AddSeconds(-1));
        if (!search.ShowCancelled) query = query.Where(d => d.CancelDate == null);

        return await query
            .OrderByDescending(d => d.UpdateDate)
            .ThenBy(d => d.DocumentNumber)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<PersonStopList?> GetPersonAsync(int id, CancellationToken ct = default)
        => _db.Set<PersonStopList>()
            .Include(p => p.Citizenship!).ThenInclude(c => c.Cultures)
            .Include(p => p.StopListSource!).ThenInclude(s => s.Cultures)
            .Include(p => p.CancelStopListSource!).ThenInclude(s => s.Cultures)
            .Include(p => p.MatchInStopListAction!).ThenInclude(a => a.Cultures)
            .Include(p => p.Fingerprints)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<DocumentStopList?> GetDocumentAsync(int id, CancellationToken ct = default)
        => _db.Set<DocumentStopList>()
            .Include(d => d.Country!).ThenInclude(c => c.Cultures)
            .Include(d => d.StopListSource!).ThenInclude(s => s.Cultures)
            .Include(d => d.CancelStopListSource!).ThenInclude(s => s.Cultures)
            .Include(d => d.MatchInStopListAction!).ThenInclude(a => a.Cultures)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    // ---- Création / modification --------------------------------------------

    /// <summary>Crée une entrée de liste d'alerte personnes. Au moins un identifiant
    /// est exigé (nom, numéro de document ou numéro personnel).</summary>
    public async Task<int> CreatePersonAsync(PersonStopListRequest request, CancellationToken ct = default)
    {
        EnsurePersonIdentifier(request);

        var now = _clock.Now;
        var entry = new PersonStopList { UpdateDate = now, SetUpDate = now, TimeStamp = now };
        ApplyPerson(entry, request);
        entry.WhoSetUp ??= _currentUser.UserId;
        entry.LastUpdateUser = _currentUser.UserId;

        _db.Set<PersonStopList>().Add(entry);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(PersonStopList), entry.Id.ToString(),
            $"{entry.Surname} {entry.GivenName}".Trim(), ct);
        return entry.Id;
    }

    public async Task UpdatePersonAsync(int id, PersonStopListRequest request, CancellationToken ct = default)
    {
        EnsurePersonIdentifier(request);

        var entry = await _db.Set<PersonStopList>().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new DomainValidationException("WL_Error_EntryNotFound");

        ApplyPerson(entry, request);
        entry.UpdateDate = _clock.Now;
        entry.TimeStamp = _clock.Now;
        entry.LastUpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(PersonStopList), id.ToString(),
            $"{entry.Surname} {entry.GivenName}".Trim(), ct);
    }

    /// <summary>Annule une entrée avec motif (source d'annulation + document) —
    /// héritage : CancelStopListSourceID / CancelSourceDocument / CancelDate.</summary>
    public async Task CancelPersonAsync(int id, int? cancelSourceId, string? cancelDocument, DateTime? cancelDate = null, CancellationToken ct = default)
    {
        var entry = await _db.Set<PersonStopList>().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new DomainValidationException("WL_Error_EntryNotFound");
        if (entry.CancelDate != null && entry.CancelDate <= _clock.Now)
            throw new DomainValidationException("WL_Error_AlreadyCancelled");

        entry.CancelDate = cancelDate ?? _clock.Now;
        entry.CancelStopListSourceId = cancelSourceId;
        entry.CancelSourceDocument = cancelDocument;
        entry.UpdateDate = _clock.Now;
        entry.LastUpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(PersonStopList), id.ToString(),
            $"{entry.Surname} {entry.GivenName}".Trim(), ct);
    }

    /// <summary>Marque l'alerte comme levée/autorisée (héritage : IsAuthorized).</summary>
    public async Task SetPersonAuthorizedAsync(int id, bool authorized, CancellationToken ct = default)
    {
        var entry = await _db.Set<PersonStopList>().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new DomainValidationException("WL_Error_EntryNotFound");

        entry.IsAuthorized = authorized;
        entry.UpdateDate = _clock.Now;
        entry.LastUpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(PersonStopList), id.ToString(),
            authorized ? "alerte autorisée" : "autorisation retirée", ct);
    }

    public async Task<int> CreateDocumentAsync(DocumentStopListRequest request, CancellationToken ct = default)
    {
        EnsureDocumentIdentifier(request);

        var now = _clock.Now;
        var entry = new DocumentStopList { UpdateDate = now, SetUpDate = now, TimeStamp = now };
        ApplyDocument(entry, request);
        entry.LastUpdateUser = _currentUser.UserId;

        _db.Set<DocumentStopList>().Add(entry);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(DocumentStopList), entry.Id.ToString(),
            entry.DocumentNumber, ct);
        return entry.Id;
    }

    public async Task UpdateDocumentAsync(int id, DocumentStopListRequest request, CancellationToken ct = default)
    {
        EnsureDocumentIdentifier(request);

        var entry = await _db.Set<DocumentStopList>().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DomainValidationException("WL_Error_EntryNotFound");

        ApplyDocument(entry, request);
        entry.UpdateDate = _clock.Now;
        entry.TimeStamp = _clock.Now;
        entry.LastUpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(DocumentStopList), id.ToString(), entry.DocumentNumber, ct);
    }

    public async Task CancelDocumentAsync(int id, int? cancelSourceId, string? cancelDocument, DateTime? cancelDate = null, CancellationToken ct = default)
    {
        var entry = await _db.Set<DocumentStopList>().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DomainValidationException("WL_Error_EntryNotFound");
        if (entry.CancelDate != null && entry.CancelDate <= _clock.Now)
            throw new DomainValidationException("WL_Error_AlreadyCancelled");

        entry.CancelDate = cancelDate ?? _clock.Now;
        entry.CancelStopListSourceId = cancelSourceId;
        entry.CancelSourceDocument = cancelDocument;
        entry.UpdateDate = _clock.Now;
        entry.LastUpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(DocumentStopList), id.ToString(), entry.DocumentNumber, ct);
    }

    // ---- Empreintes de liste d'alerte (héritage : StopListFingerprints) ------

    public async Task<IReadOnlyList<StopListFingerprint>> GetFingerprintsAsync(int personStopListId, CancellationToken ct = default)
        => await _db.Set<StopListFingerprint>()
            .AsNoTracking()
            .Where(f => f.PersonStopListId == personStopListId)
            .OrderBy(f => f.FingerNumber)
            .ToListAsync(ct);

    /// <summary>Rattache (ou remplace) l'empreinte d'un doigt sur une entrée de liste.</summary>
    public async Task AddFingerprintAsync(int personStopListId, short fingerNumber, byte[]? image, byte[]? template, CancellationToken ct = default)
    {
        if (fingerNumber is < 1 or > 10)
            throw new DomainValidationException("WL_Error_FingerNumberRange", fingerNumber.ToString());
        if (template is null or { Length: 0 })
            throw new DomainValidationException("BIO_Error_NoTemplate");

        var entryExists = await _db.Set<PersonStopList>().AnyAsync(p => p.Id == personStopListId, ct);
        if (!entryExists) throw new DomainValidationException("WL_Error_EntryNotFound");

        var existing = await _db.Set<StopListFingerprint>()
            .FirstOrDefaultAsync(f => f.PersonStopListId == personStopListId && f.FingerNumber == fingerNumber, ct);

        if (existing == null)
        {
            _db.Set<StopListFingerprint>().Add(new StopListFingerprint
            {
                PersonStopListId = personStopListId,
                FingerNumber = fingerNumber,
                FingerprintImage = image,
                Template = template
            });
        }
        else
        {
            existing.FingerprintImage = image;
            existing.Template = template;
        }
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(StopListFingerprint),
            $"{personStopListId}-{fingerNumber}", $"doigt {fingerNumber}", ct);
    }

    public async Task RemoveFingerprintAsync(int personStopListId, short fingerNumber, CancellationToken ct = default)
    {
        var existing = await _db.Set<StopListFingerprint>()
            .FirstOrDefaultAsync(f => f.PersonStopListId == personStopListId && f.FingerNumber == fingerNumber, ct);
        if (existing == null) return;

        _db.Set<StopListFingerprint>().Remove(existing);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(StopListFingerprint),
            $"{personStopListId}-{fingerNumber}", $"doigt {fingerNumber}", ct);
    }

    /// <summary>Identifie une empreinte capturée contre les empreintes de la liste
    /// d'alerte (héritage : vérification sur StopListFingerTemplates avant le
    /// registre). Renvoie les entrées dont le score passe le comparateur.</summary>
    public async Task<IReadOnlyList<StopListFingerprintMatch>> IdentifyFingerprintAsync(byte[] template, CancellationToken ct = default)
    {
        if (template is null or { Length: 0 }) return Array.Empty<StopListFingerprintMatch>();

        var prints = await _db.Set<StopListFingerprint>()
            .AsNoTracking()
            .Where(f => f.Template != null)
            .ToListAsync(ct);

        return prints
            .Where(f => _matcher.IsMatch(f.Template!, template))
            .Select(f => new StopListFingerprintMatch(f.PersonStopListId, f.FingerNumber, _matcher.Score(f.Template!, template)))
            .OrderByDescending(m => m.Score)
            .ToList();
    }

    // ---- Interne -------------------------------------------------------------

    private static void EnsurePersonIdentifier(PersonStopListRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Surname)
            && string.IsNullOrWhiteSpace(request.DocumentNumber)
            && string.IsNullOrWhiteSpace(request.PersonalNumber))
            throw new DomainValidationException("WL_Error_IdentifierRequired");
    }

    private static void EnsureDocumentIdentifier(DocumentStopListRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentNumber) && string.IsNullOrWhiteSpace(request.PersonalNumber))
            throw new DomainValidationException("WL_Error_IdentifierRequired");
    }

    private static void ApplyPerson(PersonStopList entry, PersonStopListRequest r)
    {
        entry.Surname = r.Surname?.Trim();
        entry.GivenName = r.GivenName?.Trim();
        entry.FathersName = r.FathersName?.Trim();
        entry.SurnameAlt = r.SurnameAlt?.Trim();
        entry.GivenNameAlt = r.GivenNameAlt?.Trim();
        entry.FathersNameAlt = r.FathersNameAlt?.Trim();
        entry.DateOfBirth = r.DateOfBirth?.Date;
        entry.SexId = r.SexId;
        entry.CitizenshipId = r.CitizenshipId;
        if (!string.IsNullOrWhiteSpace(r.WhoSetUp)) entry.WhoSetUp = r.WhoSetUp.Trim();
        entry.StopListSourceId = r.StopListSourceId;
        entry.SourceDocument = r.SourceDocument?.Trim();
        entry.Info = r.Info?.Trim();
        if (r.Photo is { Length: > 0 }) entry.Photo = r.Photo;
        entry.DocumentTypeId = r.DocumentTypeId;
        entry.DocumentNumber = r.DocumentNumber?.Trim();
        entry.PersonalNumber = r.PersonalNumber?.Trim();
        entry.ActionRequired = r.ActionRequired?.Trim();
        entry.MatchInStopListActionId = r.MatchInStopListActionId;
    }

    private static void ApplyDocument(DocumentStopList entry, DocumentStopListRequest r)
    {
        entry.DocumentTypeId = r.DocumentTypeId;
        entry.DocumentNumber = r.DocumentNumber?.Trim();
        entry.PersonalNumber = r.PersonalNumber?.Trim();
        entry.CountryId = r.CountryId;
        entry.Surname = r.Surname?.Trim();
        entry.GivenName = r.GivenName?.Trim();
        entry.FathersName = r.FathersName?.Trim();
        entry.DateOfBirth = r.DateOfBirth?.Date;
        entry.SexId = r.SexId;
        entry.StopListSourceId = r.StopListSourceId;
        entry.SourceDocument = r.SourceDocument?.Trim();
        entry.Info = r.Info?.Trim();
        entry.ActionRequired = r.ActionRequired?.Trim();
        entry.MatchInStopListActionId = r.MatchInStopListActionId;
    }
}
