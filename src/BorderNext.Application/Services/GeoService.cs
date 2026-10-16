using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Vue d'un pays (édition admin).</summary>
public sealed record CountryDto(int Id, string? Alpha2, string? Alpha3, string? RegionCode, int SortOrder, bool Revoke, string Fr, string En);

/// <summary>Vue d'une province.</summary>
public sealed record ProvinceDto(int Id, int CountryId, string CountryFr, string? Code, int SortOrder, bool Revoke, string Fr, string En, string? Abbreviation);

/// <summary>Vue d'une ville.</summary>
public sealed record CityDto(int Id, int CountryId, string CountryFr, int? ProvinceId, string? Code, int SortOrder, bool Revoke, string Fr, string En, string? Abbreviation);

/// <summary>Vue d'un point frontière (édition admin).</summary>
public sealed record BorderPointDto(short Id, string Code, int SortOrder, bool Revoke, string Fr, string En, string? RegionalSiteFr, string? RegionalSiteEn);

/// <summary>
/// Gestion de la géographie et des postes frontières
/// (héritage : lCountries, lProvinces, lCities, lBorderPoints + cultures).
/// </summary>
public class GeoService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public GeoService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    // ---- Pays ----------------------------------------------------------------

    public async Task<IReadOnlyList<CountryDto>> GetCountriesAsync(bool includeRevoked = false, string? search = null, CancellationToken ct = default)
    {
        var countries = await _db.Set<Country>()
            .Include(c => c.Cultures).Include(c => c.Region)
            .Where(c => includeRevoked || !c.Revoke)
            .ToListAsync(ct);

        return countries
            .Where(c => string.IsNullOrWhiteSpace(search)
                        || NameOf(c.Cultures, "fr").Contains(search, StringComparison.OrdinalIgnoreCase)
                        || NameOf(c.Cultures, "en").Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (c.Alpha2 ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (c.Alpha3 ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => NameOf(c.Cultures, "fr"), StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new CountryDto(c.Id, c.Alpha2, c.Alpha3, c.Region?.Code, c.SortOrder, c.Revoke,
                NameOf(c.Cultures, "fr"), NameOf(c.Cultures, "en")))
            .ToList();
    }

    public async Task<int> CreateCountryAsync(string? alpha2, string? alpha3, string? regionCode, int sortOrder, string? fr, string? en, CancellationToken ct = default)
    {
        (fr, en) = RequireName(fr, en);
        var country = new Country
        {
            Alpha2 = Clean(alpha2), Alpha3 = Clean(alpha3), SortOrder = sortOrder,
            RegionLookupId = await ResolveRegionAsync(regionCode, ct),
            UpdateDate = _clock.Now, UpdateUser = _currentUser.UserId
        };
        AddNames(country.Cultures, fr, en);
        _db.Set<Country>().Add(country);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Country), country.Id.ToString(), $"« {fr} »", ct);
        return country.Id;
    }

    public async Task UpdateCountryAsync(int id, string? alpha2, string? alpha3, string? regionCode, int sortOrder, string? fr, string? en, CancellationToken ct = default)
    {
        var country = await _db.Set<Country>().Include(c => c.Cultures).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        (fr, en) = RequireName(fr, en);
        country.Alpha2 = Clean(alpha2);
        country.Alpha3 = Clean(alpha3);
        country.SortOrder = sortOrder;
        country.RegionLookupId = await ResolveRegionAsync(regionCode, ct);
        country.UpdateDate = _clock.Now;
        country.UpdateUser = _currentUser.UserId;
        SetNames(country.Cultures, fr, en);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Country), country.Id.ToString(), $"« {fr} »", ct);
    }

    public async Task SetCountryRevokedAsync(int id, bool revoke, CancellationToken ct = default)
    {
        var country = await _db.Set<Country>().Include(c => c.Cultures).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        if (revoke && await _db.Set<City>().AnyAsync(c => c.CountryId == id && !c.Revoke, ct))
            throw new DomainValidationException("Admin_Error_CountryInUse", NameOf(country.Cultures, "fr"));

        country.Revoke = revoke;
        country.RevokeDate = revoke ? _clock.Now : null;
        country.UpdateDate = _clock.Now;
        country.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Country), country.Id.ToString(),
            $"{(revoke ? "Révocation" : "Réactivation")} « {NameOf(country.Cultures, "fr")} »", ct);
    }

    // ---- Provinces -----------------------------------------------------------

    public async Task<IReadOnlyList<ProvinceDto>> GetProvincesAsync(int? countryId = null, bool includeRevoked = false, CancellationToken ct = default)
    {
        var provinces = await _db.Set<Province>()
            .Include(p => p.Cultures).Include(p => p.Country!).ThenInclude(c => c.Cultures)
            .Where(p => (countryId == null || p.CountryId == countryId) && (includeRevoked || !p.Revoke))
            .ToListAsync(ct);

        return provinces
            .OrderBy(p => NameOf(p.Country!.Cultures, "fr"), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.SortOrder)
            .Select(p => new ProvinceDto(p.Id, p.CountryId, NameOf(p.Country!.Cultures, "fr"), p.ExternalId, p.SortOrder, p.Revoke,
                NameOf(p.Cultures, "fr"), NameOf(p.Cultures, "en"), AbbreviationOf(p.Cultures)))
            .ToList();
    }

    public async Task<int> CreateProvinceAsync(int countryId, string? code, int sortOrder, string? fr, string? en, string? abbreviation, CancellationToken ct = default)
    {
        await RequireCountryAsync(countryId, ct);
        (fr, en) = RequireName(fr, en);
        var province = new Province
        {
            CountryId = countryId, ExternalId = Clean(code), SortOrder = sortOrder,
            UpdateDate = _clock.Now, UpdateUser = _currentUser.UserId
        };
        AddNames(province.Cultures, fr, en, abbreviation);
        _db.Set<Province>().Add(province);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Province), province.Id.ToString(), $"« {fr} »", ct);
        return province.Id;
    }

    public async Task UpdateProvinceAsync(int id, int countryId, string? code, int sortOrder, string? fr, string? en, string? abbreviation, CancellationToken ct = default)
    {
        var province = await _db.Set<Province>().Include(p => p.Cultures).SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        await RequireCountryAsync(countryId, ct);
        (fr, en) = RequireName(fr, en);
        province.CountryId = countryId;
        province.ExternalId = Clean(code);
        province.SortOrder = sortOrder;
        province.UpdateDate = _clock.Now;
        province.UpdateUser = _currentUser.UserId;
        SetNames(province.Cultures, fr, en, abbreviation);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Province), province.Id.ToString(), $"« {fr} »", ct);
    }

    public async Task SetProvinceRevokedAsync(int id, bool revoke, CancellationToken ct = default)
    {
        var province = await _db.Set<Province>().Include(p => p.Cultures).SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        province.Revoke = revoke;
        province.RevokeDate = revoke ? _clock.Now : null;
        province.UpdateDate = _clock.Now;
        province.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Province), province.Id.ToString(),
            $"{(revoke ? "Révocation" : "Réactivation")} « {NameOf(province.Cultures, "fr")} »", ct);
    }

    // ---- Villes --------------------------------------------------------------

    public async Task<IReadOnlyList<CityDto>> GetCitiesAsync(int? countryId = null, bool includeRevoked = false, string? search = null, CancellationToken ct = default)
    {
        var cities = await _db.Set<City>()
            .Include(c => c.Cultures).Include(c => c.Country!).ThenInclude(c => c.Cultures)
            .Where(c => (countryId == null || c.CountryId == countryId) && (includeRevoked || !c.Revoke))
            .ToListAsync(ct);

        return cities
            .Where(c => string.IsNullOrWhiteSpace(search)
                        || NameOf(c.Cultures, "fr").Contains(search, StringComparison.OrdinalIgnoreCase)
                        || NameOf(c.Cultures, "en").Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (c.ExternalId ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => NameOf(c.Country!.Cultures, "fr"), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.SortOrder)
            .Select(c => new CityDto(c.Id, c.CountryId, NameOf(c.Country!.Cultures, "fr"), c.ProvinceId, c.ExternalId, c.SortOrder, c.Revoke,
                NameOf(c.Cultures, "fr"), NameOf(c.Cultures, "en"), AbbreviationOf(c.Cultures)))
            .ToList();
    }

    public async Task<int> CreateCityAsync(int countryId, int? provinceId, string? code, int sortOrder, string? fr, string? en, string? abbreviation, CancellationToken ct = default)
    {
        await RequireCountryAsync(countryId, ct);
        (fr, en) = RequireName(fr, en);
        var city = new City
        {
            CountryId = countryId, ProvinceId = provinceId, ExternalId = Clean(code), SortOrder = sortOrder,
            UpdateDate = _clock.Now, UpdateUser = _currentUser.UserId
        };
        AddNames(city.Cultures, fr, en, abbreviation);
        _db.Set<City>().Add(city);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(City), city.Id.ToString(), $"« {fr} »", ct);
        return city.Id;
    }

    public async Task UpdateCityAsync(int id, int countryId, int? provinceId, string? code, int sortOrder, string? fr, string? en, string? abbreviation, CancellationToken ct = default)
    {
        var city = await _db.Set<City>().Include(c => c.Cultures).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        await RequireCountryAsync(countryId, ct);
        (fr, en) = RequireName(fr, en);
        city.CountryId = countryId;
        city.ProvinceId = provinceId;
        city.ExternalId = Clean(code);
        city.SortOrder = sortOrder;
        city.UpdateDate = _clock.Now;
        city.UpdateUser = _currentUser.UserId;
        SetNames(city.Cultures, fr, en, abbreviation);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(City), city.Id.ToString(), $"« {fr} »", ct);
    }

    public async Task SetCityRevokedAsync(int id, bool revoke, CancellationToken ct = default)
    {
        var city = await _db.Set<City>().Include(c => c.Cultures).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        city.Revoke = revoke;
        city.RevokeDate = revoke ? _clock.Now : null;
        city.UpdateDate = _clock.Now;
        city.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(City), city.Id.ToString(),
            $"{(revoke ? "Révocation" : "Réactivation")} « {NameOf(city.Cultures, "fr")} »", ct);
    }

    // ---- Postes frontières ---------------------------------------------------

    public async Task<IReadOnlyList<BorderPointDto>> GetBorderPointsAsync(bool includeRevoked = false, CancellationToken ct = default)
    {
        var points = await _db.Set<BorderPoint>()
            .Include(b => b.Cultures)
            .Where(b => includeRevoked || !b.Revoke)
            .ToListAsync(ct);

        return points
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.ExternalId)
            .Select(b => new BorderPointDto(b.Id, b.ExternalId, b.SortOrder ?? 0, b.Revoke,
                NameOf(b.Cultures, "fr"), NameOf(b.Cultures, "en"),
                b.Cultures.FirstOrDefault(c => c.Culture == "fr")?.RegionalSite,
                b.Cultures.FirstOrDefault(c => c.Culture == "en")?.RegionalSite))
            .ToList();
    }

    public async Task<short> CreateBorderPointAsync(string code, int sortOrder, string? fr, string? en, string? siteFr, string? siteEn, CancellationToken ct = default)
    {
        code = (code ?? string.Empty).Trim();
        if (code.Length == 0) throw new DomainValidationException("Admin_Error_NameRequired");
        if (await _db.Set<BorderPoint>().AnyAsync(b => b.ExternalId == code, ct))
            throw new DomainValidationException("Admin_Error_DuplicateCode", code);

        (fr, en) = RequireName(fr, en);
        var point = new BorderPoint
        {
            ExternalId = code,
            SortOrder = (short)sortOrder,
            UpdateDate = _clock.Now
        };
        point.Cultures.Add(new BorderPointCulture { Culture = "fr", Name = fr, RegionalSite = Clean(siteFr) });
        point.Cultures.Add(new BorderPointCulture { Culture = "en", Name = string.IsNullOrEmpty(en) ? fr : en, RegionalSite = Clean(siteEn) });
        _db.Set<BorderPoint>().Add(point);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(BorderPoint), point.Id.ToString(), $"« {code} »", ct);
        return point.Id;
    }

    public async Task UpdateBorderPointAsync(short id, string code, int sortOrder, string? fr, string? en, string? siteFr, string? siteEn, CancellationToken ct = default)
    {
        var point = await _db.Set<BorderPoint>().Include(b => b.Cultures).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        code = (code ?? string.Empty).Trim();
        if (code.Length == 0) throw new DomainValidationException("Admin_Error_NameRequired");
        if (await _db.Set<BorderPoint>().AnyAsync(b => b.ExternalId == code && b.Id != id, ct))
            throw new DomainValidationException("Admin_Error_DuplicateCode", code);

        (fr, en) = RequireName(fr, en);
        point.ExternalId = code;
        point.SortOrder = (short)sortOrder;
        point.UpdateDate = _clock.Now;
        SetBorderPointCulture(point, "fr", fr, siteFr);
        SetBorderPointCulture(point, "en", string.IsNullOrEmpty(en) ? fr : en, siteEn);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(BorderPoint), point.Id.ToString(), $"« {code} »", ct);
    }

    public async Task SetBorderPointRevokedAsync(short id, bool revoke, CancellationToken ct = default)
    {
        var point = await _db.Set<BorderPoint>().SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        if (revoke && await _db.Set<User>().AnyAsync(u => u.BorderPointId == id && !u.Revoke, ct))
            throw new DomainValidationException("Admin_Error_BorderPointInUse", point.ExternalId);

        point.Revoke = revoke;
        point.RevokeDate = revoke ? _clock.Now : null;
        point.UpdateDate = _clock.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(BorderPoint), point.Id.ToString(),
            $"{(revoke ? "Révocation" : "Réactivation")} « {point.ExternalId} »", ct);
    }

    // ---- Aides ---------------------------------------------------------------

    private async Task RequireCountryAsync(int countryId, CancellationToken ct)
    {
        if (!await _db.Set<Country>().AnyAsync(c => c.Id == countryId, ct))
            throw new DomainValidationException("Admin_Error_CountryRequired");
    }

    private async Task<int?> ResolveRegionAsync(string? regionCode, CancellationToken ct)
    {
        regionCode = Clean(regionCode);
        if (regionCode is null) return null;
        var region = await _db.Set<Lookup>().SingleOrDefaultAsync(l => l.Type!.Code == "REGION" && l.Code == regionCode, ct);
        return region?.Id;
    }

    private static void SetBorderPointCulture(BorderPoint point, string culture, string name, string? site)
    {
        var row = point.Cultures.FirstOrDefault(c => c.Culture == culture);
        if (row is null)
        {
            row = new BorderPointCulture { Culture = culture };
            point.Cultures.Add(row);
        }
        row.Name = name;
        row.RegionalSite = Clean(site);
    }

    private static string NameOf<T>(IEnumerable<T> rows, string culture) where T : INamedRow
        => rows.FirstOrDefault(r => r.Culture == culture)?.Name ?? string.Empty;

    private static string? AbbreviationOf(IEnumerable<ProvinceCulture> rows)
        => rows.FirstOrDefault(r => r.Culture == "fr")?.Abbreviation;

    private static string? AbbreviationOf(IEnumerable<CityCulture> rows)
        => rows.FirstOrDefault(r => r.Culture == "fr")?.Abbreviation;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (string Fr, string En) RequireName(string? fr, string? en)
    {
        fr = (fr ?? string.Empty).Trim();
        en = (en ?? string.Empty).Trim();
        if (fr.Length == 0 && en.Length == 0) throw new DomainValidationException("Admin_Error_NameRequired");
        if (fr.Length == 0) fr = en;
        return (fr, en);
    }

    private static void AddNames(ICollection<CountryCulture> cultures, string fr, string en)
    {
        cultures.Add(new CountryCulture { Culture = "fr", Name = fr });
        cultures.Add(new CountryCulture { Culture = "en", Name = string.IsNullOrEmpty(en) ? fr : en });
    }

    private static void AddNames(ICollection<ProvinceCulture> cultures, string fr, string en, string? abbreviation)
    {
        cultures.Add(new ProvinceCulture { Culture = "fr", Name = fr, Abbreviation = Clean(abbreviation) });
        cultures.Add(new ProvinceCulture { Culture = "en", Name = string.IsNullOrEmpty(en) ? fr : en, Abbreviation = Clean(abbreviation) });
    }

    private static void AddNames(ICollection<CityCulture> cultures, string fr, string en, string? abbreviation)
    {
        cultures.Add(new CityCulture { Culture = "fr", Name = fr, Abbreviation = Clean(abbreviation) });
        cultures.Add(new CityCulture { Culture = "en", Name = string.IsNullOrEmpty(en) ? fr : en, Abbreviation = Clean(abbreviation) });
    }

    private static void SetNames(ICollection<CountryCulture> cultures, string fr, string en)
    {
        SetName(cultures, "fr", fr);
        SetName(cultures, "en", string.IsNullOrEmpty(en) ? fr : en);
    }

    private static void SetNames(ICollection<ProvinceCulture> cultures, string fr, string en, string? abbreviation)
    {
        SetName(cultures, "fr", fr, abbreviation);
        SetName(cultures, "en", string.IsNullOrEmpty(en) ? fr : en, abbreviation);
    }

    private static void SetNames(ICollection<CityCulture> cultures, string fr, string en, string? abbreviation)
    {
        SetName(cultures, "fr", fr, abbreviation);
        SetName(cultures, "en", string.IsNullOrEmpty(en) ? fr : en, abbreviation);
    }

    private static void SetName(ICollection<CountryCulture> cultures, string culture, string name)
    {
        var row = cultures.FirstOrDefault(c => c.Culture == culture);
        if (row is null) { row = new CountryCulture { Culture = culture }; cultures.Add(row); }
        row.Name = name;
    }

    private static void SetName(ICollection<ProvinceCulture> cultures, string culture, string name, string? abbreviation)
    {
        var row = cultures.FirstOrDefault(c => c.Culture == culture);
        if (row is null) { row = new ProvinceCulture { Culture = culture }; cultures.Add(row); }
        row.Name = name;
        row.Abbreviation = Clean(abbreviation);
    }

    private static void SetName(ICollection<CityCulture> cultures, string culture, string name, string? abbreviation)
    {
        var row = cultures.FirstOrDefault(c => c.Culture == culture);
        if (row is null) { row = new CityCulture { Culture = culture }; cultures.Add(row); }
        row.Name = name;
        row.Abbreviation = Clean(abbreviation);
    }
}
