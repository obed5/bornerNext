using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Demande de création/mise à jour d'un voyage et de son passage
/// (héritage : CreateTrip / UpdateTrip).</summary>
public sealed record TripRequest(
    string Name,
    string TransportTypeCode,
    DateTime Date,
    string DirectionCode,
    int? FromTerminalId = null,
    int? ToTerminalId = null,
    int? CarrierId = null,
    int? TerminalId = null,
    string? ExternalNumber = null,
    string? Description = null,
    TimeSpan? ScheduledTime = null,
    int? StationFromCityId = null,
    int? StationToCityId = null,
    short? BorderPointId = null,
    bool Open = true);

/// <summary>Passage ouvert à l'enregistrement (héritage : GetOpenedTrips —
/// l'enregistrement des personnes se fait « dans » un passage ouvert).</summary>
public sealed record OpenTripItem(
    int CrossingId,
    int TripId,
    string TripName,
    string Name,
    DateTime Date,
    TimeSpan? OpenTime,
    string? CarrierName,
    string DirectionCode,
    string DirectionFr,
    string TransportTypeCode,
    short? BorderPointId);

/// <summary>Ligne d'historique des passages (héritage : TransportCrossedBorder).</summary>
public sealed record TripCrossingItem(
    int CrossingId,
    int TripId,
    string Name,
    string TransportTypeCode,
    string TypeFr,
    DateTime Date,
    string DirectionCode,
    string DirectionFr,
    string? FromTerminal,
    string? ToTerminal,
    string? CarrierName,
    bool IsOpen,
    TimeSpan? OpenTime,
    string? OpenUser,
    TimeSpan? CloseTime,
    string? CloseUser,
    int PassengerCount,
    short? BorderPointId,
    int? FromTerminalId = null,
    int? ToTerminalId = null,
    int? CarrierId = null,
    int? TerminalId = null,
    string? ExternalNumber = null,
    string? Description = null,
    TimeSpan? ScheduledTime = null);

/// <summary>Passager (mouvement) rattaché à un passage.</summary>
public sealed record TripPassengerItem(
    int MovementId,
    string? DocumentNumber,
    string? HolderName,
    DateTime TravelDate,
    string? DirectionFr,
    bool Granted);

/// <summary>
/// Opérations frontière — voyages et passages (héritage : RegisterTripForm,
/// RegistratedTripsForm, TripsForm, PAXChangeFlight) :
///  - <c>CreateTrip</c> : un voyage (Transports) + un passage (TransportCrossedBorder)
///    + l'état de la salle d'enregistrement (TransportCrossInfo) ;
///  - <c>GetOpenedTrips</c> : passages ouverts touchant le terminal du poste
///    (réglage hérité « Terminal »), nom composé « NOM/JJ.MM  A - B » orienté
///    selon le sens et le terminal local ;
///  - <c>UpdateTrip</c> / <c>DeleteTrip</c> (suppression refusée si des personnes
///    sont rattachées — l'héritage les aurait orphelines, la clé étrangère du
///    modèle reconstruit l'interdit) ;
///  - réaffectation d'un passager à un autre passage (PAXChangeFlight).
/// </summary>
public class TripService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public TripService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>Terminal du poste (réglage hérité « Terminal », ex. FIH).</summary>
    public Task<int?> GetStationTerminalIdAsync(CancellationToken ct = default)
        => StationSettings.GetTerminalIdAsync(_db, ct);

    /// <summary>Passages ouverts touchant le terminal du poste (héritage : GetOpenedTrips ;
    /// filtre de type optionnel : Flight, Ship, Train — comme les listes héritées
    /// FlightsList / VesselsList / TrainsList).</summary>
    public async Task<IReadOnlyList<OpenTripItem>> GetOpenTripsAsync(string? transportTypeCode = null,
        CancellationToken ct = default)
    {
        var stationTerminal = await GetStationTerminalIdAsync(ct);

        var query = _db.Set<TripCrossing>().AsNoTracking()
            .Include(c => c.Trip!).ThenInclude(t => t.TransportType)
            .Include(c => c.Trip!).ThenInclude(t => t.FromTerminal!).ThenInclude(t => t.Cultures)
            .Include(c => c.Trip!).ThenInclude(t => t.ToTerminal!).ThenInclude(t => t.Cultures)
            .Include(c => c.Trip!).ThenInclude(t => t.Carrier!).ThenInclude(c2 => c2.Cultures)
            .Include(c => c.Direction!).ThenInclude(d => d.Cultures)
            .Where(c => c.IsOpen);

        if (stationTerminal is { } station)
            query = query.Where(c => c.Trip!.FromTerminalId == station || c.Trip!.ToTerminalId == station);
        if (!string.IsNullOrWhiteSpace(transportTypeCode))
            query = query.Where(c => c.Trip!.TransportType!.Code == transportTypeCode);

        var crossings = await query.OrderBy(c => c.Date).ThenBy(c => c.OpenTime).ToListAsync(ct);
        return crossings.Select(c => new OpenTripItem(
            c.Id,
            c.TripId,
            ComposeTripName(c, stationTerminal),
            c.Trip!.Name,
            c.Date,
            c.OpenTime,
            c.Trip.Carrier is null ? null : DescriptionOf(c.Trip.Carrier.Cultures, "fr"),
            c.Direction?.Code ?? string.Empty,
            c.Direction is null ? string.Empty : DescriptionOf(c.Direction.Cultures, "fr"),
            c.Trip.TransportType?.Code ?? string.Empty,
            c.BorderPointId)).ToList();
    }

    /// <summary>Historique des passages (héritage : RegistratedTripsForm).</summary>
    public async Task<IReadOnlyList<TripCrossingItem>> GetCrossingsAsync(DateTime? from = null, DateTime? to = null,
        string? transportTypeCode = null, bool? onlyOpen = null, int take = 200, CancellationToken ct = default)
    {
        var query = _db.Set<TripCrossing>().AsNoTracking()
            .Include(c => c.Trip!).ThenInclude(t => t.TransportType)
            .Include(c => c.Trip!).ThenInclude(t => t.FromTerminal!).ThenInclude(t => t.Cultures)
            .Include(c => c.Trip!).ThenInclude(t => t.ToTerminal!).ThenInclude(t => t.Cultures)
            .Include(c => c.Trip!).ThenInclude(t => t.Carrier!).ThenInclude(c2 => c2.Cultures)
            .Include(c => c.Direction!).ThenInclude(d => d.Cultures)
            .AsQueryable();

        if (from is not null) query = query.Where(c => c.Date >= from.Value.Date);
        if (to is not null) query = query.Where(c => c.Date <= to.Value.Date.AddDays(1).AddSeconds(-1));
        if (!string.IsNullOrWhiteSpace(transportTypeCode))
            query = query.Where(c => c.Trip!.TransportType!.Code == transportTypeCode);
        if (onlyOpen is { } open) query = query.Where(c => c.IsOpen == open);

        var crossings = await query
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Take(take)
            .ToListAsync(ct);

        var ids = crossings.Select(c => c.Id).ToList();
        var counts = (await _db.Set<Movement>().AsNoTracking()
                .Where(m => m.TripCrossingId != null && ids.Contains(m.TripCrossingId.Value))
                .GroupBy(m => m.TripCrossingId!.Value)
                .Select(g => new { CrossingId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.CrossingId, x => x.Count);

        return crossings.Select(c => ToItem(c, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<TripCrossingItem?> GetCrossingAsync(int crossingId, CancellationToken ct = default)
    {
        var crossing = await _db.Set<TripCrossing>().AsNoTracking()
            .Include(c => c.Trip!).ThenInclude(t => t.TransportType)
            .Include(c => c.Trip!).ThenInclude(t => t.FromTerminal!).ThenInclude(t => t.Cultures)
            .Include(c => c.Trip!).ThenInclude(t => t.ToTerminal!).ThenInclude(t => t.Cultures)
            .Include(c => c.Trip!).ThenInclude(t => t.Carrier!).ThenInclude(c2 => c2.Cultures)
            .Include(c => c.Direction!).ThenInclude(d => d.Cultures)
            .FirstOrDefaultAsync(c => c.Id == crossingId, ct);
        if (crossing == null) return null;

        var count = await _db.Set<Movement>().AsNoTracking()
            .CountAsync(m => m.TripCrossingId == crossingId, ct);
        return ToItem(crossing, count);
    }

    /// <summary>Crée un voyage et son passage (héritage : CreateTrip).</summary>
    public async Task<int> RegisterAsync(TripRequest request, CancellationToken ct = default)
    {
        var (typeId, directionId) = await ValidateAsync(request, ct);

        var now = _clock.Now;
        var trip = new Trip
        {
            Name = request.Name.Trim(),
            TransportTypeId = typeId,
            FromTerminalId = request.FromTerminalId,
            ToTerminalId = request.ToTerminalId,
            CarrierId = request.CarrierId,
            TerminalId = request.TerminalId ?? await GetStationTerminalIdAsync(ct),
            ExternalNumber = request.ExternalNumber?.Trim(),
            Description = request.Description?.Trim(),
            ScheduledTime = request.ScheduledTime,
            StationFromCityId = request.StationFromCityId,
            StationToCityId = request.StationToCityId,
            BorderPointId = request.BorderPointId ?? await StationSettings.GetBorderPointIdAsync(_db, _currentUser, ct),
            LastUpdateUser = _currentUser.UserId,
            UpdateDate = now
        };

        var crossing = new TripCrossing
        {
            Trip = trip,
            DirectionLookupId = directionId,
            Date = request.Date.Date,
            BorderPointId = request.BorderPointId ?? await StationSettings.GetBorderPointIdAsync(_db, _currentUser, ct),
            LastUpdateUser = _currentUser.UserId,
            UpdateDate = now,
            IsOpen = request.Open,
            OpenTime = request.Open ? now.TimeOfDay : null,
            OpenUser = request.Open ? _currentUser.UserId : null
        };

        _db.Set<TripCrossing>().Add(crossing);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Trip), trip.Id.ToString(),
            $"{trip.Name} ({request.Date:dd/MM/yyyy}, {request.DirectionCode})", ct);
        return crossing.Id;
    }

    /// <summary>Met à jour le voyage et son passage (héritage : UpdateTrip — le numéro
    /// externe et la description du voyage ne font pas partie de la mise à jour héritée).</summary>
    public async Task UpdateAsync(int crossingId, TripRequest request, CancellationToken ct = default)
    {
        var (typeId, directionId) = await ValidateAsync(request, ct);

        var crossing = await _db.Set<TripCrossing>().Include(c => c.Trip)
            .FirstOrDefaultAsync(c => c.Id == crossingId, ct)
            ?? throw new DomainValidationException("BO_Error_CrossingNotFound");

        var trip = crossing.Trip!;
        var now = _clock.Now;
        trip.Name = request.Name.Trim();
        trip.TransportTypeId = typeId;
        trip.FromTerminalId = request.FromTerminalId;
        trip.ToTerminalId = request.ToTerminalId;
        trip.CarrierId = request.CarrierId;
        trip.TerminalId = request.TerminalId ?? trip.TerminalId;
        trip.ScheduledTime = request.ScheduledTime;
        trip.BorderPointId = request.BorderPointId ?? trip.BorderPointId;
        trip.LastUpdateUser = _currentUser.UserId;
        trip.UpdateDate = now;

        crossing.DirectionLookupId = directionId;
        crossing.Date = request.Date.Date;
        crossing.BorderPointId = request.BorderPointId ?? crossing.BorderPointId;
        crossing.LastUpdateUser = _currentUser.UserId;
        crossing.UpdateDate = now;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Trip), trip.Id.ToString(), trip.Name, ct);
    }

    /// <summary>Ouvre ou ferme la salle d'enregistrement du passage
    /// (héritage : TransportCrossInfo OpenStatus/OpenTime/CloseTime).</summary>
    public async Task SetOpenAsync(int crossingId, bool open, CancellationToken ct = default)
    {
        var crossing = await _db.Set<TripCrossing>().FirstOrDefaultAsync(c => c.Id == crossingId, ct)
            ?? throw new DomainValidationException("BO_Error_CrossingNotFound");

        var now = _clock.Now;
        crossing.IsOpen = open;
        if (open)
        {
            crossing.OpenTime = now.TimeOfDay;
            crossing.OpenUser = _currentUser.UserId;
            crossing.CloseTime = null;
            crossing.CloseUser = null;
        }
        else
        {
            crossing.OpenTime = null;
            crossing.OpenUser = null;
            crossing.CloseTime = now.TimeOfDay;
            crossing.CloseUser = _currentUser.UserId;
        }
        crossing.LastUpdateUser = _currentUser.UserId;
        crossing.UpdateDate = now;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(TripCrossing), crossingId.ToString(),
            open ? "passage ouvert" : "passage fermé", ct);
    }

    /// <summary>Supprime le passage — et le voyage s'il n'a plus de passage
    /// (héritage : DeleteTrip). Refusé si des personnes y sont rattachées.</summary>
    public async Task DeleteAsync(int crossingId, CancellationToken ct = default)
    {
        var crossing = await _db.Set<TripCrossing>().Include(c => c.Trip)
            .FirstOrDefaultAsync(c => c.Id == crossingId, ct)
            ?? throw new DomainValidationException("BO_Error_CrossingNotFound");

        var passengers = await _db.Set<Movement>().CountAsync(m => m.TripCrossingId == crossingId, ct);
        if (passengers > 0)
            throw new DomainValidationException("BO_Error_TripHasPassengers", passengers.ToString());

        var trip = crossing.Trip!;
        _db.Set<TripCrossing>().Remove(crossing);
        var otherCrossings = await _db.Set<TripCrossing>()
            .CountAsync(c => c.TripId == trip.Id && c.Id != crossingId, ct);
        if (otherCrossings == 0) _db.Set<Trip>().Remove(trip);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Trip), trip.Id.ToString(),
            $"{trip.Name} ({crossing.Date:dd/MM/yyyy})", ct);
    }

    /// <summary>Personnes enregistrées sur un passage (héritage : passagers du voyage).</summary>
    public async Task<IReadOnlyList<TripPassengerItem>> GetPassengersAsync(int crossingId, CancellationToken ct = default)
    {
        var movements = await _db.Set<Movement>().AsNoTracking()
            .Include(m => m.Document)
            .Include(m => m.Direction).ThenInclude(d => d.Cultures)
            .Where(m => m.TripCrossingId == crossingId)
            .OrderBy(m => m.TravelDate)
            .ToListAsync(ct);

        return movements.Select(m => new TripPassengerItem(
            m.Id,
            m.Document?.DocumentNumber,
            m.Document is null ? null : $"{m.Document.Surname} {m.Document.GivenName}".Trim(),
            m.TravelDate,
            m.Direction is null ? null : DescriptionOf(m.Direction.Cultures, "fr"),
            m.Granted)).ToList();
    }

    /// <summary>Réaffecte une personne à un autre passage (héritage : PAXChangeFlight —
    /// réaffectation d'un passager sur un autre vol).</summary>
    public async Task ReassignMovementAsync(int movementId, int targetCrossingId, CancellationToken ct = default)
    {
        var movement = await _db.Set<Movement>().FirstOrDefaultAsync(m => m.Id == movementId, ct)
            ?? throw new DomainValidationException("PAL_Error_MovementNotFound", movementId.ToString());
        if (!await _db.Set<TripCrossing>().AnyAsync(c => c.Id == targetCrossingId, ct))
            throw new DomainValidationException("BO_Error_CrossingNotFound");

        movement.TripCrossingId = targetCrossingId;
        movement.UpdateDate = _clock.Now;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Movement), movementId.ToString(),
            $"réaffecté au passage {targetCrossingId}", ct);
    }

    // ---- Interne -------------------------------------------------------------

    private async Task<(int? TypeId, int DirectionId)> ValidateAsync(TripRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainValidationException("BO_Error_NameRequired");
        if (request.FromTerminalId == null && request.ToTerminalId == null)
            throw new DomainValidationException("BO_Error_TerminalRequired");

        int? typeId = null;
        if (!string.IsNullOrWhiteSpace(request.TransportTypeCode))
        {
            typeId = await _db.Set<Lookup>()
                .Where(l => l.Type!.Code == "TRANSPORT_TYPE" && l.Code == request.TransportTypeCode && !l.Revoke)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(ct)
                ?? throw new DomainValidationException("BO_Error_TransportTypeUnknown", request.TransportTypeCode);
        }

        var directionId = await _db.Set<Lookup>()
            .Where(l => l.Type!.Code == "MOVEMENT_DIRECTION" && l.Code == request.DirectionCode && !l.Revoke)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainValidationException("BO_Error_DirectionUnknown", request.DirectionCode);

        foreach (var terminalId in new[] { request.FromTerminalId, request.ToTerminalId, request.TerminalId })
        {
            if (terminalId is { } id && !await _db.Set<Terminal>().AnyAsync(t => t.Id == id, ct))
                throw new DomainValidationException("BO_Error_TerminalUnknown", id.ToString());
        }
        if (request.CarrierId is { } carrierId && !await _db.Set<Carrier>().AnyAsync(c => c.Id == carrierId, ct))
            throw new DomainValidationException("BO_Error_CarrierUnknown", carrierId.ToString());

        return (typeId, directionId);
    }

    /// <summary>Nom composé hérité (GetOpenedTrips) : « NOM/JJ.MM  A - B » — l'ordre
    /// des terminaux suit le sens et le terminal local du poste.</summary>
    private static string ComposeTripName(TripCrossing crossing, int? stationTerminal)
    {
        var trip = crossing.Trip!;
        var from = trip.FromTerminal?.Code ?? "?";
        var to = trip.ToTerminal?.Code ?? "?";
        var direction = crossing.Direction?.Code ?? string.Empty;

        string Order()
        {
            if (stationTerminal is not { } station) return $"{from} - {to}";
            return direction == "Entry"
                ? (trip.ToTerminalId == station ? $"{from} - {to}" : $"{to} - {from}")
                : (trip.FromTerminalId == station ? $"{from} - {to}" : $"{to} - {from}");
        }

        return $"{trip.Name}/{crossing.Date:dd.MM}  {Order()}";
    }

    private static TripCrossingItem ToItem(TripCrossing c, int passengerCount)
        => new(c.Id, c.TripId, c.Trip?.Name ?? string.Empty,
            c.Trip?.TransportType?.Code ?? string.Empty,
            c.Trip?.TransportType is null ? string.Empty : DescriptionOf(c.Trip.TransportType.Cultures, "fr"),
            c.Date,
            c.Direction?.Code ?? string.Empty,
            c.Direction is null ? string.Empty : DescriptionOf(c.Direction.Cultures, "fr"),
            c.Trip?.FromTerminal is null ? null : DescriptionOf(c.Trip.FromTerminal.Cultures, "fr"),
            c.Trip?.ToTerminal is null ? null : DescriptionOf(c.Trip.ToTerminal.Cultures, "fr"),
            c.Trip?.Carrier is null ? null : DescriptionOf(c.Trip.Carrier.Cultures, "fr"),
            c.IsOpen, c.OpenTime, c.OpenUser, c.CloseTime, c.CloseUser,
            passengerCount, c.BorderPointId,
            c.Trip?.FromTerminalId, c.Trip?.ToTerminalId, c.Trip?.CarrierId, c.Trip?.TerminalId,
            c.Trip?.ExternalNumber, c.Trip?.Description, c.Trip?.ScheduledTime);

    private static string DescriptionOf(IEnumerable<LookupCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Description
           ?? cultures?.FirstOrDefault()?.Description
           ?? string.Empty;

    private static string DescriptionOf(IEnumerable<TerminalCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Description
           ?? cultures?.FirstOrDefault()?.Description
           ?? string.Empty;

    private static string DescriptionOf(IEnumerable<CarrierCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Description
           ?? cultures?.FirstOrDefault()?.Description
           ?? string.Empty;
}
