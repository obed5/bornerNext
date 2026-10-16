using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Ligne « transactions par poste frontière » (héritage : dashboard.TotalTransactionsByBCP —
/// comparaison sur trois ans, jour courant, il y a un an, il y a deux ans).</summary>
public sealed record BorderPointTransactions(
    short BorderPointId,
    string Code,
    string NameFr,
    int Today,
    int YearAgo,
    int TwoYearsAgo);

/// <summary>Nationalité la plus représentée dans la journée (héritage : carte des nationalités
/// rpt.DashboardCountryGPSCoordinates — représentée ici par un classement).</summary>
public sealed record NationalityCount(string Alpha3, string NameFr, int Count);

/// <summary>Point de la carte des nationalités du jour (coordonnées de référence des pays).</summary>
public sealed record NationalityMapPoint(string Name, string? Iso2, string? Iso3, decimal Latitude,
    decimal Longitude, int Count);

/// <summary>Synthèse du tableau de bord.</summary>
public sealed record DashboardSummary(
    DateTime Date,
    string StationName,
    int TransactionsToday,
    int TransactionsYearAgo,
    int TransactionsTwoYearsAgo,
    int EntriesToday,
    int ExitsToday,
    int ActiveInspectors,
    int ArrivalInspectors,
    int ExitInspectors,
    int DoubtfulPending,
    int DoubtfulResolvedToday,
    int OpenTrips,
    int FlightsToday,
    int RefusalsToday,
    int PenaltiesToday,
    IReadOnlyList<BorderPointTransactions> PerBorderPoint,
    IReadOnlyList<NationalityCount> TopNationalities)
{
    /// <summary>Écart du jour par rapport au même jour de l'année précédente (héritage : comparaison 3 ans).</summary>
    public int DeltaYearAgo => TransactionsToday - TransactionsYearAgo;
}

/// <summary>
/// Tableau de bord du poste (héritage : schéma <c>dashboard</c> — TotalTransactionsByDay /
/// ByMovmentDirection / ByBCP, NumberOfActiveLogins, NumberOfDoubtablePersons,
/// NumberOfFlights) : transactions du jour et comparaison sur trois ans, arrivées/départs,
/// inspecteurs actifs, personnes douteuses, voyages ouverts et vols, refus et amendes,
/// nationalités les plus représentées.
///
/// Différence assumée : l'héritage filtrait <c>TravelDate &gt; aujourd'hui</c> (dates horodatées) ;
/// les mouvements BorderNext portent une date sans heure, la fenêtre du jour est donc
/// <c>[aujourd'hui, demain[</c>.
/// </summary>
public class DashboardService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public DashboardService(DbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var date = _clock.Now.Date;
        var next = date.AddDays(1);
        var yearAgo = date.AddYears(-1);
        var twoYearsAgo = date.AddYears(-2);

        var movements = _db.Set<Movement>().AsNoTracking();
        var todayMovements = movements.Where(m => m.TravelDate >= date && m.TravelDate < next);

        var entryId = await DirectionIdAsync(MovementService.EntryCode, ct);
        var exitId = await DirectionIdAsync(MovementService.ExitCode, ct);

        var transactionsToday = await todayMovements.CountAsync(ct);
        var entriesToday = entryId is { } entry ? await todayMovements.CountAsync(m => m.DirectionLookupId == entry, ct) : 0;
        var exitsToday = exitId is { } exit ? await todayMovements.CountAsync(m => m.DirectionLookupId == exit, ct) : 0;

        var transactionsYearAgo = await movements.CountAsync(m => m.TravelDate >= yearAgo && m.TravelDate < next.AddYears(-1), ct);
        var transactionsTwoYearsAgo = await movements.CountAsync(m => m.TravelDate >= twoYearsAgo && m.TravelDate < next.AddYears(-2), ct);

        // Inspecteurs actifs du jour (héritage : NumberOfActiveLogins — inspecteurs distincts
        // ayant enregistré un mouvement, répartis entrée/sortie).
        var inspectors = await todayMovements
            .Where(m => m.Inspector != null)
            .Select(m => new { m.Inspector, m.DirectionLookupId })
            .Distinct()
            .ToListAsync(ct);
        var activeInspectors = inspectors.Select(i => i.Inspector).Distinct().Count();
        var arrivalInspectors = entryId is { } e2 ? inspectors.Where(i => i.DirectionLookupId == e2).Select(i => i.Inspector).Distinct().Count() : 0;
        var exitInspectors = exitId is { } x2 ? inspectors.Where(i => i.DirectionLookupId == x2).Select(i => i.Inspector).Distinct().Count() : 0;

        // Personnes douteuses (héritage : NumberOfDoubtablePersons — actifs / résolus).
        var doubtfulPending = await _db.Set<DoubtablePerson>().AsNoTracking()
            .CountAsync(d => d.WorkflowStatus == DoubtfulWorkflowStatus.Pending, ct);
        var todayMovementIds = await todayMovements.Select(m => m.Id).ToListAsync(ct);
        var doubtfulResolvedToday = await _db.Set<DoubtablePerson>().AsNoTracking()
            .CountAsync(d => d.WorkflowStatus != DoubtfulWorkflowStatus.Pending
                             && todayMovementIds.Contains(d.MovementId), ct);

        // Voyages : passages ouverts touchant le terminal du poste + vols du jour
        // (héritage : NumberOfFlights — vols distincts des mouvements du jour).
        var openTrips = await OpenTripsCountAsync(ct);
        var flightsToday = await _db.Set<Movement>().AsNoTracking()
            .Where(m => m.TripCrossingId != null && m.TravelDate >= date && m.TravelDate < next
                        && m.TripCrossing!.Trip!.TransportType!.Code == "Flight")
            .Select(m => m.TripCrossingId)
            .Distinct()
            .CountAsync(ct);

        var refusalsToday = await todayMovements.CountAsync(m => !m.Granted, ct);
        var penaltiesToday = await _db.Set<PenaltyTicket>().AsNoTracking()
            .CountAsync(p => p.CreateDate >= date && p.CreateDate < next && !p.Revoke, ct);

        var perBorderPoint = await PerBorderPointAsync(date, next, yearAgo, twoYearsAgo, ct);
        var topNationalities = await TopNationalitiesAsync(date, next, ct);

        return new DashboardSummary(
            date,
            await StationNameAsync(ct),
            transactionsToday, transactionsYearAgo, transactionsTwoYearsAgo,
            entriesToday, exitsToday,
            activeInspectors, arrivalInspectors, exitInspectors,
            doubtfulPending, doubtfulResolvedToday,
            openTrips, flightsToday,
            refusalsToday, penaltiesToday,
            perBorderPoint, topNationalities);
    }

    // ---- Interne -------------------------------------------------------------

    private async Task<int?> DirectionIdAsync(string code, CancellationToken ct)
        => await _db.Set<Lookup>().AsNoTracking()
            .Where(l => l.Type!.Code == "MOVEMENT_DIRECTION" && l.Code == code)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);

    private async Task<int> OpenTripsCountAsync(CancellationToken ct)
    {
        var stationTerminal = await StationSettings.GetTerminalIdAsync(_db, ct);

        var open = _db.Set<TripCrossing>().AsNoTracking().Where(c => c.IsOpen);
        if (stationTerminal is { } terminal)
            open = open.Where(c => c.Trip!.FromTerminalId == terminal || c.Trip!.ToTerminalId == terminal);
        return await open.CountAsync(ct);
    }

    private async Task<IReadOnlyList<BorderPointTransactions>> PerBorderPointAsync(
        DateTime date, DateTime next, DateTime yearAgo, DateTime twoYearsAgo, CancellationToken ct)
    {
        var borderPoints = await _db.Set<BorderPoint>().AsNoTracking()
            .Include(b => b.Cultures)
            .Where(b => !b.Revoke)
            .OrderByDescending(b => b.SortOrder)
            .ToListAsync(ct);

        var movements = await _db.Set<Movement>().AsNoTracking()
            .Where(m => m.BorderPointId != null
                        && m.TravelDate >= twoYearsAgo && m.TravelDate < next)
            .Select(m => new { m.BorderPointId, m.TravelDate })
            .ToListAsync(ct);

        return borderPoints.Select(bp => new BorderPointTransactions(
            bp.Id, bp.ExternalId, NameOf(bp.Cultures),
            movements.Count(m => m.BorderPointId == bp.Id && m.TravelDate >= date && m.TravelDate < next),
            movements.Count(m => m.BorderPointId == bp.Id && m.TravelDate >= yearAgo && m.TravelDate < next.AddYears(-1)),
            movements.Count(m => m.BorderPointId == bp.Id && m.TravelDate >= twoYearsAgo && m.TravelDate < next.AddYears(-2))))
            .ToList();
    }

    private async Task<IReadOnlyList<NationalityCount>> TopNationalitiesAsync(DateTime date, DateTime next, CancellationToken ct)
    {
        var rows = await _db.Set<Movement>().AsNoTracking()
            .Where(m => m.TravelDate >= date && m.TravelDate < next && m.Document!.CitizenshipId != null)
            .Select(m => new { m.Document!.CitizenshipId, m.Document.Citizenship!.Alpha3, m.Document.Citizenship.Cultures })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new { r.CitizenshipId, r.Alpha3 })
            .Select(g => new NationalityCount(g.Key.Alpha3 ?? "?", NameOf(g.First().Cultures), g.Count()))
            .OrderByDescending(n => n.Count)
            .ThenBy(n => n.NameFr)
            .Take(10)
            .ToList();
    }

    /// <summary>Nationalités du jour localisées, pour la carte du tableau de bord
    /// (héritage : carte des nationalités, coordonnées rpt.DashboardCountryGPSCoordinates).</summary>
    public async Task<IReadOnlyList<NationalityMapPoint>> GetNationalityMapAsync(CancellationToken ct = default)
    {
        var date = _clock.Now.Date;
        var next = date.AddDays(1);

        var movements = await _db.Set<Movement>().AsNoTracking()
            .Where(m => m.TravelDate >= date && m.TravelDate < next && m.Document!.CitizenshipId != null)
            .Select(m => new { m.Document!.CitizenshipId, m.Document.Citizenship!.Alpha2, m.Document.Citizenship.Alpha3,
                m.Document.Citizenship.Cultures })
            .ToListAsync(ct);

        var points = await _db.Set<CountryGeoPoint>().AsNoTracking().ToListAsync(ct);
        var byIso3 = points.Where(p => !string.IsNullOrEmpty(p.Iso3))
            .GroupBy(p => p.Iso3!).ToDictionary(g => g.Key, g => g.First());

        return movements
            .GroupBy(m => m.CitizenshipId)
            .Select(g =>
            {
                var sample = g.First();
                if (sample.Alpha3 is not { } alpha3 || !byIso3.TryGetValue(alpha3, out var point)) return null;
                var name = NameOf(sample.Cultures);
                return new NationalityMapPoint(
                    string.IsNullOrWhiteSpace(name) ? alpha3 : name,
                    sample.Alpha2, alpha3, point.Latitude, point.Longitude, g.Count());
            })
            .Where(p => p is not null)
            .Select(p => p!)
            .OrderByDescending(p => p.Count)
            .ThenBy(p => p.Name)
            .ToList();
    }

    private async Task<string> StationNameAsync(CancellationToken ct)
    {
        if (await StationSettings.GetBorderPointIdAsync(_db, _currentUser, ct) is not { } id) return string.Empty;
        var borderPoint = await _db.Set<BorderPoint>().AsNoTracking()
            .Include(b => b.Cultures)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (borderPoint is null) return string.Empty;
        // Les libellés hérités des postes reprennent souvent le code (« FIH ») :
        // on évite alors le doublon d'affichage.
        var name = NameOf(borderPoint.Cultures);
        return string.IsNullOrWhiteSpace(name) || name == borderPoint.ExternalId
            ? borderPoint.ExternalId
            : $"{borderPoint.ExternalId} — {name}";
    }

    private static string NameOf(IEnumerable<BorderPointCulture> cultures)
        => cultures.FirstOrDefault(c => c.Culture == "fr")?.Name
           ?? cultures.FirstOrDefault()?.Name
           ?? string.Empty;

    private static string NameOf(IEnumerable<CountryCulture> cultures)
        => cultures.FirstOrDefault(c => c.Culture == "fr")?.Name
           ?? cultures.FirstOrDefault()?.Name
           ?? string.Empty;
}
