using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Création/mise à jour d'une compagnie (héritage : CarrierForm).</summary>
public sealed record CarrierRequest(string Code, string? Fr, string? En, string? TransportTypeCode,
    int? CountryId = null, int SortOrder = 1000);

/// <summary>Création/mise à jour d'un terminal (héritage : TerminalForm).</summary>
public sealed record TerminalRequest(string Code, string? Fr, string? En, string? TransportTypeCode,
    int? CountryId = null, int? CityId = null, int SortOrder = 1000);

/// <summary>Création/mise à jour d'un navire (héritage : VesselsForm).</summary>
public sealed record VesselRequest(
    string Name,
    string RegistrationNumber,
    string? FormerName = null,
    int? HomePortCountryId = null,
    int? NationalityCountryId = null,
    string? Dimension = null,
    string? Color = null,
    string? VesselTypeCode = null,
    string? RadioCallSign = null,
    string? Frequencies = null,
    string? Note = null,
    DateTime? RegistrationDate = null);

/// <summary>Ligne de la liste des navires.</summary>
public sealed record VesselListItem(int Id, string Name, string? FormerName, string RegistrationNumber,
    string? TypeFr, string? NationalityFr, string? HomePortFr, string? RadioCallSign, DateTime? RegistrationDate);

/// <summary>
/// Référentiels du transport (héritage : CarrierForm, TerminalForm, VesselsForm) :
/// compagnies (lCarriers — 822 codes IATA), terminaux (lTerminals — aéroports,
/// gares, ports) et navires (Vessels).
/// </summary>
public class TransportReferentialService
{
    private readonly DbContext _db;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public TransportReferentialService(DbContext db, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    // ---- Compagnies ----------------------------------------------------------

    public async Task<IReadOnlyList<Carrier>> SearchCarriersAsync(string? search = null, string? transportTypeCode = null,
        bool includeRevoked = false, int take = 300, CancellationToken ct = default)
    {
        var query = _db.Set<Carrier>().AsNoTracking()
            .Include(c => c.Cultures)
            .Include(c => c.Country!).ThenInclude(x => x.Cultures)
            .Include(c => c.TransportType)
            .Where(c => includeRevoked || !c.Revoke);

        if (!string.IsNullOrWhiteSpace(transportTypeCode))
            query = query.Where(c => c.TransportType!.Code == transportTypeCode);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var upper = search.ToUpper();
            query = query.Where(c => c.Code.ToUpper().Contains(upper)
                                     || c.Cultures.Any(x => x.Description.ToUpper().Contains(upper)));
        }

        return await query.OrderBy(c => c.Code).Take(take).ToListAsync(ct);
    }

    public async Task<int> CreateCarrierAsync(CarrierRequest request, CancellationToken ct = default)
    {
        var code = NormalizeCode(request.Code);
        if (await _db.Set<Carrier>().AnyAsync(c => c.Code == code, ct))
            throw new DomainValidationException("BO_Error_DuplicateCode", code);
        if (string.IsNullOrWhiteSpace(request.Fr) && string.IsNullOrWhiteSpace(request.En))
            throw new DomainValidationException("BO_Error_NameRequired");

        var carrier = new Carrier
        {
            Code = code,
            TransportTypeId = await ResolveTransportTypeAsync(request.TransportTypeCode, ct),
            CountryId = request.CountryId,
            SortOrder = request.SortOrder,
            UpdateDate = _clock.Now
        };
        carrier.Cultures.Add(new CarrierCulture { Culture = "fr", Description = (request.Fr ?? request.En)!.Trim() });
        carrier.Cultures.Add(new CarrierCulture { Culture = "en", Description = (request.En ?? request.Fr)!.Trim() });

        _db.Set<Carrier>().Add(carrier);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Carrier), carrier.Id.ToString(), code, ct);
        return carrier.Id;
    }

    public async Task UpdateCarrierAsync(int id, CarrierRequest request, CancellationToken ct = default)
    {
        var carrier = await _db.Set<Carrier>().Include(c => c.Cultures)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new DomainValidationException("BO_Error_EntryNotFound");

        carrier.TransportTypeId = await ResolveTransportTypeAsync(request.TransportTypeCode, ct);
        carrier.CountryId = request.CountryId;
        carrier.SortOrder = request.SortOrder;
        carrier.UpdateDate = _clock.Now;
        SetCulture(carrier.Cultures, "fr", request.Fr ?? request.En);
        SetCulture(carrier.Cultures, "en", request.En ?? request.Fr);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Carrier), id.ToString(), carrier.Code, ct);
    }

    public async Task SetCarrierRevokedAsync(int id, bool revoked, CancellationToken ct = default)
    {
        var carrier = await _db.Set<Carrier>().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new DomainValidationException("BO_Error_EntryNotFound");

        if (revoked && await _db.Set<Trip>().AnyAsync(t => t.CarrierId == id, ct))
            throw new DomainValidationException("BO_Error_CarrierInUse", carrier.Code);

        carrier.Revoke = revoked;
        carrier.RevokeDate = revoked ? _clock.Now : null;
        carrier.UpdateDate = _clock.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Carrier), id.ToString(), carrier.Code, ct);
    }

    // ---- Terminaux -----------------------------------------------------------

    public async Task<IReadOnlyList<Terminal>> SearchTerminalsAsync(string? search = null, string? transportTypeCode = null,
        bool includeRevoked = false, int take = 300, CancellationToken ct = default)
    {
        var query = _db.Set<Terminal>().AsNoTracking()
            .Include(t => t.Cultures)
            .Include(t => t.Country!).ThenInclude(x => x.Cultures)
            .Include(t => t.City!).ThenInclude(x => x.Cultures)
            .Include(t => t.TransportType)
            .Where(t => includeRevoked || !t.Revoke);

        if (!string.IsNullOrWhiteSpace(transportTypeCode))
            query = query.Where(t => t.TransportType!.Code == transportTypeCode);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var upper = search.ToUpper();
            query = query.Where(t => t.Code.ToUpper().Contains(upper)
                                     || t.Cultures.Any(x => x.Description.ToUpper().Contains(upper)));
        }

        return await query.OrderBy(t => t.Code).Take(take).ToListAsync(ct);
    }

    public async Task<int> CreateTerminalAsync(TerminalRequest request, CancellationToken ct = default)
    {
        var code = NormalizeCode(request.Code);
        if (await _db.Set<Terminal>().AnyAsync(t => t.Code == code, ct))
            throw new DomainValidationException("BO_Error_DuplicateCode", code);
        if (string.IsNullOrWhiteSpace(request.Fr) && string.IsNullOrWhiteSpace(request.En))
            throw new DomainValidationException("BO_Error_NameRequired");

        var terminal = new Terminal
        {
            Code = code,
            TransportTypeId = await ResolveTransportTypeAsync(request.TransportTypeCode, ct),
            CountryId = request.CountryId,
            CityId = request.CityId,
            SortOrder = request.SortOrder,
            UpdateDate = _clock.Now
        };
        terminal.Cultures.Add(new TerminalCulture { Culture = "fr", Description = (request.Fr ?? request.En)!.Trim() });
        terminal.Cultures.Add(new TerminalCulture { Culture = "en", Description = (request.En ?? request.Fr)!.Trim() });

        _db.Set<Terminal>().Add(terminal);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Terminal), terminal.Id.ToString(), code, ct);
        return terminal.Id;
    }

    public async Task UpdateTerminalAsync(int id, TerminalRequest request, CancellationToken ct = default)
    {
        var terminal = await _db.Set<Terminal>().Include(t => t.Cultures)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainValidationException("BO_Error_EntryNotFound");

        terminal.TransportTypeId = await ResolveTransportTypeAsync(request.TransportTypeCode, ct);
        terminal.CountryId = request.CountryId;
        terminal.CityId = request.CityId;
        terminal.SortOrder = request.SortOrder;
        terminal.UpdateDate = _clock.Now;
        SetCulture(terminal.Cultures, "fr", request.Fr ?? request.En);
        SetCulture(terminal.Cultures, "en", request.En ?? request.Fr);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Terminal), id.ToString(), terminal.Code, ct);
    }

    public async Task SetTerminalRevokedAsync(int id, bool revoked, CancellationToken ct = default)
    {
        var terminal = await _db.Set<Terminal>().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainValidationException("BO_Error_EntryNotFound");

        if (revoked && await _db.Set<Trip>().AnyAsync(t => t.FromTerminalId == id || t.ToTerminalId == id || t.TerminalId == id, ct))
            throw new DomainValidationException("BO_Error_TerminalInUse", terminal.Code);

        terminal.Revoke = revoked;
        terminal.RevokeDate = revoked ? _clock.Now : null;
        terminal.UpdateDate = _clock.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Terminal), id.ToString(), terminal.Code, ct);
    }

    // ---- Navires -------------------------------------------------------------

    public async Task<IReadOnlyList<VesselListItem>> SearchVesselsAsync(string? search = null, int take = 200,
        CancellationToken ct = default)
    {
        var query = _db.Set<Vessel>().AsNoTracking()
            .Include(v => v.VesselType!).ThenInclude(t => t.Cultures)
            .Include(v => v.NationalityCountry!).ThenInclude(c => c.Cultures)
            .Include(v => v.HomePortCountry!).ThenInclude(c => c.Cultures)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var upper = search.ToUpper();
            query = query.Where(v => v.Name.ToUpper().Contains(upper)
                                     || v.RegistrationNumber.ToUpper().Contains(upper)
                                     || (v.FormerName != null && v.FormerName.ToUpper().Contains(upper)));
        }

        var vessels = await query.OrderBy(v => v.Name).Take(take).ToListAsync(ct);
        return vessels.Select(v => new VesselListItem(
            v.Id, v.Name, v.FormerName, v.RegistrationNumber,
            DescriptionOf(v.VesselType?.Cultures, "fr"),
            v.NationalityCountry is null ? null : DescriptionOf(v.NationalityCountry.Cultures, "fr"),
            v.HomePortCountry is null ? null : DescriptionOf(v.HomePortCountry.Cultures, "fr"),
            v.RadioCallSign, v.RegistrationDate)).ToList();
    }

    public async Task<Vessel?> GetVesselAsync(int id, CancellationToken ct = default)
        => await _db.Set<Vessel>().AsNoTracking()
            .Include(v => v.VesselType!).ThenInclude(t => t.Cultures)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<int> CreateVesselAsync(VesselRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainValidationException("BO_Error_NameRequired");
        if (string.IsNullOrWhiteSpace(request.RegistrationNumber))
            throw new DomainValidationException("BO_Error_VesselRegistrationRequired");

        var vessel = new Vessel { RegistrationNumber = request.RegistrationNumber.Trim() };
        Apply(vessel, request, await ResolveVesselTypeAsync(request.VesselTypeCode, ct));

        _db.Set<Vessel>().Add(vessel);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Vessel), vessel.Id.ToString(), vessel.Name, ct);
        return vessel.Id;
    }

    public async Task UpdateVesselAsync(int id, VesselRequest request, CancellationToken ct = default)
    {
        var vessel = await _db.Set<Vessel>().FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new DomainValidationException("BO_Error_EntryNotFound");
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainValidationException("BO_Error_NameRequired");
        if (string.IsNullOrWhiteSpace(request.RegistrationNumber))
            throw new DomainValidationException("BO_Error_VesselRegistrationRequired");

        Apply(vessel, request, await ResolveVesselTypeAsync(request.VesselTypeCode, ct));
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Vessel), id.ToString(), vessel.Name, ct);
    }

    // ---- Interne -------------------------------------------------------------

    private static void Apply(Vessel vessel, VesselRequest request, int? vesselTypeId)
    {
        vessel.Name = request.Name.Trim();
        vessel.RegistrationNumber = request.RegistrationNumber.Trim();
        vessel.FormerName = request.FormerName?.Trim();
        vessel.HomePortCountryId = request.HomePortCountryId;
        vessel.NationalityCountryId = request.NationalityCountryId;
        vessel.Dimension = request.Dimension?.Trim();
        vessel.Color = request.Color?.Trim();
        vessel.VesselTypeId = vesselTypeId;
        vessel.RadioCallSign = request.RadioCallSign?.Trim();
        vessel.Frequencies = request.Frequencies?.Trim();
        vessel.Note = request.Note?.Trim();
        vessel.RegistrationDate = request.RegistrationDate;
    }

    private Task<int?> ResolveTransportTypeAsync(string? code, CancellationToken ct)
        => string.IsNullOrWhiteSpace(code)
            ? Task.FromResult<int?>(null)
            : _db.Set<Lookup>().Where(l => l.Type!.Code == "TRANSPORT_TYPE" && l.Code == code && !l.Revoke)
                .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);

    private Task<int?> ResolveVesselTypeAsync(string? code, CancellationToken ct)
        => string.IsNullOrWhiteSpace(code)
            ? Task.FromResult<int?>(null)
            : _db.Set<Lookup>().Where(l => l.Type!.Code == "VESSEL_TYPE" && l.Code == code && !l.Revoke)
                .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);

    private static void SetCulture(ICollection<CarrierCulture> cultures, string culture, string? value)
    {
        var existing = cultures.FirstOrDefault(c => c.Culture == culture);
        if (existing is null) cultures.Add(new CarrierCulture { Culture = culture, Description = value ?? string.Empty });
        else existing.Description = value ?? string.Empty;
    }

    private static void SetCulture(ICollection<TerminalCulture> cultures, string culture, string? value)
    {
        var existing = cultures.FirstOrDefault(c => c.Culture == culture);
        if (existing is null) cultures.Add(new TerminalCulture { Culture = culture, Description = value ?? string.Empty });
        else existing.Description = value ?? string.Empty;
    }

    private static string NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 0)
            throw new DomainValidationException("BO_Error_CodeRequired");
        return normalized;
    }

    private static string? DescriptionOf(IEnumerable<LookupCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Description
           ?? cultures?.FirstOrDefault()?.Description;

    private static string? DescriptionOf(IEnumerable<CountryCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Name
           ?? cultures?.FirstOrDefault()?.Name;
}
