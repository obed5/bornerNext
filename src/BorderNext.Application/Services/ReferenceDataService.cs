using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Vue d'un type de nomenclature avec son nombre d'éléments actifs.</summary>
public sealed record LookupTypeDto(int Id, string Code, string NameFr, string NameEn, int ItemCount);

/// <summary>Vue d'un élément de nomenclature (libellés fr/en, édition admin).</summary>
public sealed record LookupItemDto(int Id, string? Code, int SortOrder, bool Revoke, string Fr, string En);

/// <summary>Vue d'un contrôle d'interface (droits).</summary>
public sealed record ControlDto(int Id, string Code, int SortOrder, bool Revoke, string Fr, string En);

/// <summary>
/// Gestion des nomenclatures génériques (héritage : tables l* + l*Cultures) et des
/// contrôles d'interface (héritage : lControls). CRUD avec traductions fr/en,
/// révocation logique et journal d'audit.
/// </summary>
public class ReferenceDataService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public ReferenceDataService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    // ---- Nomenclatures -------------------------------------------------------

    public async Task<IReadOnlyList<LookupTypeDto>> GetTypesAsync(CancellationToken ct = default)
    {
        var types = await _db.Set<LookupType>()
            .Include(t => t.Items)
            .Where(t => !t.Revoke)
            .ToListAsync(ct);

        return types
            .OrderBy(t => t.NameFr)
            .Select(t => new LookupTypeDto(t.Id, t.Code, t.NameFr, t.NameEn, t.Items.Count(i => !i.Revoke)))
            .ToList();
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetItemsAsync(string typeCode, bool includeRevoked = false, string? search = null, CancellationToken ct = default)
    {
        var items = await _db.Set<Lookup>()
            .Include(l => l.Cultures)
            .Where(l => l.Type!.Code == typeCode && (includeRevoked || !l.Revoke))
            .ToListAsync(ct);

        return items
            .Where(l => Matches(l, search))
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => Description(l, "fr"), StringComparer.CurrentCultureIgnoreCase)
            .Select(ToDto)
            .ToList();
    }

    public async Task<int> CreateItemAsync(string typeCode, string? code, int sortOrder, string? fr, string? en, CancellationToken ct = default)
    {
        var type = await _db.Set<LookupType>().SingleOrDefaultAsync(t => t.Code == typeCode, ct)
            ?? throw new DomainValidationException("Admin_Error_TypeNotFound", typeCode);

        (fr, en) = RequireAtLeastOneName(fr, en);
        code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        if (code is not null && await _db.Set<Lookup>().AnyAsync(l => l.LookupTypeId == type.Id && l.Code == code, ct))
            throw new DomainValidationException("Admin_Error_DuplicateCode", code);

        var lookup = new Lookup
        {
            LookupTypeId = type.Id,
            Code = code,
            SortOrder = sortOrder,
            UpdateDate = _clock.Now,
            UpdateUser = _currentUser.UserId
        };
        AddNames(lookup, fr, en);
        _db.Set<Lookup>().Add(lookup);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Lookup), lookup.Id.ToString(), $"{typeCode} « {fr} »", ct);
        return lookup.Id;
    }

    public async Task UpdateItemAsync(int id, string? code, int sortOrder, string? fr, string? en, CancellationToken ct = default)
    {
        var lookup = await _db.Set<Lookup>().Include(l => l.Cultures).SingleOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        (fr, en) = RequireAtLeastOneName(fr, en);
        code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        if (code is not null && await _db.Set<Lookup>().AnyAsync(l => l.LookupTypeId == lookup.LookupTypeId && l.Code == code && l.Id != id, ct))
            throw new DomainValidationException("Admin_Error_DuplicateCode", code);

        lookup.Code = code;
        lookup.SortOrder = sortOrder;
        lookup.UpdateDate = _clock.Now;
        lookup.UpdateUser = _currentUser.UserId;
        SetNames(lookup.Cultures, fr, en);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Lookup), lookup.Id.ToString(), $"« {fr} »", ct);
    }

    public async Task SetItemRevokedAsync(int id, bool revoke, CancellationToken ct = default)
    {
        var lookup = await _db.Set<Lookup>().Include(l => l.Cultures).SingleOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        lookup.Revoke = revoke;
        lookup.RevokeDate = revoke ? _clock.Now : null;
        lookup.UpdateDate = _clock.Now;
        lookup.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Lookup), lookup.Id.ToString(),
            $"{(revoke ? "Révocation" : "Réactivation")} « {Description(lookup, "fr")} »", ct);
    }

    // ---- Contrôles (droits) --------------------------------------------------

    public async Task<IReadOnlyList<ControlDto>> GetControlsAsync(bool includeRevoked = false, CancellationToken ct = default)
    {
        var controls = await _db.Set<Control>()
            .Include(c => c.Cultures)
            .Where(c => includeRevoked || !c.Revoke)
            .ToListAsync(ct);

        return controls.OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
            .Select(c => new ControlDto(c.Id, c.Code, c.SortOrder, c.Revoke,
                CultureOf(c.Cultures, "fr", cc => cc.Description), CultureOf(c.Cultures, "en", cc => cc.Description)))
            .ToList();
    }

    public async Task UpdateControlAsync(int id, int sortOrder, string? fr, string? en, CancellationToken ct = default)
    {
        var control = await _db.Set<Control>().Include(c => c.Cultures).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        (fr, en) = RequireAtLeastOneName(fr, en);
        control.SortOrder = sortOrder;
        control.UpdateDate = _clock.Now;
        control.UpdateUser = _currentUser.UserId;
        SetNames(control.Cultures, fr, en);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Control), control.Id.ToString(), control.Code, ct);
    }

    // ---- Aides ---------------------------------------------------------------

    private static bool Matches(Lookup l, string? search)
        => string.IsNullOrWhiteSpace(search)
           || (l.Code?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
           || l.Cultures.Any(c => c.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

    private static LookupItemDto ToDto(Lookup l)
        => new(l.Id, l.Code, l.SortOrder, l.Revoke,
            CultureOf(l.Cultures, "fr", c => c.Description), CultureOf(l.Cultures, "en", c => c.Description));

    private static string Description(Lookup l, string culture) => CultureOf(l.Cultures, culture, c => c.Description);

    internal static string CultureOf<T>(IEnumerable<T> cultures, string culture, Func<T, string> selector)
        where T : ITranslatedRow
        => cultures.FirstOrDefault(c => c.Culture == culture) is { } row ? selector(row) : string.Empty;

    private static (string Fr, string En) RequireAtLeastOneName(string? fr, string? en)
    {
        fr = (fr ?? string.Empty).Trim();
        en = (en ?? string.Empty).Trim();
        if (fr.Length == 0 && en.Length == 0) throw new DomainValidationException("Admin_Error_NameRequired");
        if (fr.Length == 0) fr = en;
        return (fr, en);
    }

    private static void AddNames(Lookup lookup, string fr, string en)
    {
        lookup.Cultures.Add(new LookupCulture { Culture = "fr", Description = fr });
        lookup.Cultures.Add(new LookupCulture { Culture = "en", Description = string.IsNullOrEmpty(en) ? fr : en });
    }

    private static void SetNames<T>(ICollection<T> cultures, string fr, string en)
        where T : class, IDescribedRow, new()
    {
        foreach (var (culture, value) in new[] { ("fr", fr), ("en", string.IsNullOrEmpty(en) ? fr : en) })
        {
            var existing = cultures.FirstOrDefault(c => c.Culture == culture);
            if (existing is null)
            {
                existing = new T { Culture = culture };
                cultures.Add(existing);
            }
            existing.Description = value;
        }
    }
}
