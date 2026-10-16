using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Référentiels et données communes aux tests des opérations frontière (Lot 6).</summary>
internal static class BorderOpsSeed
{
    public const int FihTerminal = 100;    // terminal du poste (réglage hérité « Terminal »)
    public const int OryTerminal = 101;    // terminal distant
    public const int AirFrance = 200;
    public const int CongoAirways = 201;

    public static void SeedReferentials(BorderNextDbContext db)
    {
        WatchlistSeed.SeedReferentials(db);

        db.Countries.Add(new Country { Id = 900, Alpha3 = "COD", Cultures = { new CountryCulture { Culture = "fr", Name = "CONGO, LA R.D.C." } } });
        db.Cities.Add(new City { Id = 901, CountryId = 900, ExternalId = "FIH", Cultures = { new CityCulture { Culture = "fr", Name = "Kinshasa" } } });

        // Le jeu de test du Lot 2 n'a que l'item « Flight » : compléter avec « Ship ».
        var transportType = db.LookupTypes.Include(t => t.Items).Single(t => t.Code == "TRANSPORT_TYPE");
        var flight = transportType.Items.Single(i => i.Code == "Flight");
        var ship = transportType.Items.FirstOrDefault(i => i.Code == "Ship");
        if (ship is null)
        {
            ship = new Lookup
            {
                Code = "Ship",
                Cultures = { new LookupCulture { Culture = "fr", Description = "Bateau" }, new LookupCulture { Culture = "en", Description = "Ship" } }
            };
            transportType.Items.Add(ship);
            db.SaveChanges();
        }

        db.Terminals.AddRange(
            new Terminal
            {
                Id = FihTerminal, Code = "FIH", TransportTypeId = flight.Id, CountryId = 900, CityId = 901,
                SortOrder = 1000, Cultures = { new TerminalCulture { Culture = "fr", Description = "NDjili" } }
            },
            new Terminal
            {
                Id = OryTerminal, Code = "ORY", TransportTypeId = flight.Id, SortOrder = 1000,
                Cultures = { new TerminalCulture { Culture = "fr", Description = "Orly" } }
            });

        db.Carriers.AddRange(
            new Carrier
            {
                Id = AirFrance, Code = "AF", TransportTypeId = flight.Id, SortOrder = 1000,
                Cultures = { new CarrierCulture { Culture = "fr", Description = "Air France" } }
            },
            new Carrier
            {
                Id = CongoAirways, Code = "XX", TransportTypeId = flight.Id, SortOrder = 1000,
                Cultures = { new CarrierCulture { Culture = "fr", Description = "Congo Airways" } }
            });

        if (!db.LookupTypes.Any(t => t.Code == "VESSEL_TYPE"))
        {
            db.LookupTypes.Add(new LookupType
            {
                Id = 60, Code = "VESSEL_TYPE", NameFr = "Types de navires", NameEn = "Vessel types",
                Items =
                {
                    new Lookup { Id = 60, Code = "Ferry", Cultures = { new LookupCulture { Culture = "fr", Description = "Bac" } } },
                    new Lookup { Id = 61, Code = "Boat", Cultures = { new LookupCulture { Culture = "fr", Description = "Bateau" } } }
                }
            });
        }

        db.Settings.Add(new Setting { Type = "Terminal", Value = FihTerminal });
        db.SaveChanges();
    }

    /// <summary>Voyage + passage prêts à l'emploi (AF100 Paris → Kinshasa, arrivée).</summary>
    public static TripRequest Arrival(string name = "AF100", DateTime? date = null, bool open = true)
        => new(name, "Flight", date ?? new DateTime(2026, 10, 16), MovementService.EntryCode,
            FromTerminalId: OryTerminal, ToTerminalId: FihTerminal, CarrierId: AirFrance,
            ExternalNumber: name, Description: "Paris — Kinshasa", ScheduledTime: new TimeSpan(14, 30, 0), Open: open);
}

/// <summary>Voyages et passages (héritage : CreateTrip, UpdateTrip, DeleteTrip, GetOpenedTrips).</summary>
public class TripServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly TripService _service;

    public TripServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"bo-{Guid.NewGuid()}").Options);
        BorderOpsSeed.SeedReferentials(_db);
        _service = new TripService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 2, BorderPointId = 2 }, "SHIFT_OFF");
        _currentUser.SetPcNumber("A01");
    }

    private async Task<(int TripId, int CrossingId)> RegisterAsync(TripRequest? request = null)
    {
        var crossingId = await _service.RegisterAsync(request ?? BorderOpsSeed.Arrival());
        var crossing = await _db.TripCrossings.SingleAsync(c => c.Id == crossingId);
        return (crossing.TripId, crossingId);
    }

    [Fact]
    public async Task Register_CreatesTripCrossingAndOpenState()
    {
        var (tripId, crossingId) = await RegisterAsync();

        var trip = await _db.Trips.SingleAsync(t => t.Id == tripId);
        Assert.Equal("AF100", trip.Name);
        Assert.Equal(BorderOpsSeed.OryTerminal, trip.FromTerminalId);
        Assert.Equal(BorderOpsSeed.FihTerminal, trip.ToTerminalId);
        Assert.Equal(BorderOpsSeed.AirFrance, trip.CarrierId);
        Assert.Equal(new TimeSpan(14, 30, 0), trip.ScheduledTime);
        Assert.Equal("U001", trip.LastUpdateUser);

        var crossing = await _db.TripCrossings.SingleAsync(c => c.Id == crossingId);
        Assert.True(crossing.IsOpen);
        Assert.NotNull(crossing.OpenTime);
        Assert.Equal("U001", crossing.OpenUser);
        Assert.Null(crossing.CloseTime);
        Assert.Equal((short)2, crossing.BorderPointId);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(Trip));
    }

    [Fact]
    public async Task Register_WithInvalidData_IsRejected()
    {
        var anonymous = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(BorderOpsSeed.Arrival("")));
        Assert.Equal("BO_Error_NameRequired", anonymous.ErrorKey);

        var noTerminal = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(BorderOpsSeed.Arrival() with { FromTerminalId = null, ToTerminalId = null }));
        Assert.Equal("BO_Error_TerminalRequired", noTerminal.ErrorKey);

        var unknownType = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(BorderOpsSeed.Arrival() with { TransportTypeCode = "UnknownType" }));
        Assert.Equal("BO_Error_TransportTypeUnknown", unknownType.ErrorKey);
    }

    [Fact]
    public async Task Register_WithUnknownDirectionOrTerminalOrCarrier_IsRejected()
    {
        var direction = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(BorderOpsSeed.Arrival() with { DirectionCode = "ZZZ" }));
        Assert.Equal("BO_Error_DirectionUnknown", direction.ErrorKey);

        var terminal = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(BorderOpsSeed.Arrival() with { ToTerminalId = 9999 }));
        Assert.Equal("BO_Error_TerminalUnknown", terminal.ErrorKey);

        var carrier = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(BorderOpsSeed.Arrival() with { CarrierId = 9999 }));
        Assert.Equal("BO_Error_CarrierUnknown", carrier.ErrorKey);
    }

    [Fact]
    public async Task GetOpenTrips_FiltersStationTerminalAndComposesLegacyName()
    {
        await RegisterAsync();                                                     // ORY → FIH, arrivée (touche le poste)
        await RegisterAsync(BorderOpsSeed.Arrival("XX200") with
        {
            FromTerminalId = BorderOpsSeed.FihTerminal,
            ToTerminalId = BorderOpsSeed.OryTerminal,
            DirectionCode = MovementService.ExitCode,
            CarrierId = BorderOpsSeed.CongoAirways
        });
        await RegisterAsync(BorderOpsSeed.Arrival("AF999", open: false));           // fermé : exclu

        var open = await _service.GetOpenTripsAsync();

        Assert.Equal(2, open.Count);
        var arrival = open.Single(t => t.Name == "AF100");
        // Arrivée : le terminal local est celui d'arrivée → « ORY - FIH ».
        Assert.Equal("AF100/16.10  ORY - FIH", arrival.TripName);
        Assert.Equal("Air France", arrival.CarrierName);
        Assert.Equal("Arrivée", arrival.DirectionFr);

        var departure = open.Single(t => t.Name == "XX200");
        // Départ : le terminal local est celui de départ → « FIH - ORY ».
        Assert.Equal("XX200/16.10  FIH - ORY", departure.TripName);
    }

    [Fact]
    public async Task GetOpenTrips_TypeFilterSelectsFlights()
    {
        await RegisterAsync();

        Assert.Single(await _service.GetOpenTripsAsync("Flight"));
        Assert.Empty(await _service.GetOpenTripsAsync("Ship"));
    }

    [Fact]
    public async Task GetCrossings_CountsPassengers()
    {
        var (tripId, crossingId) = await RegisterAsync();
        await AddMovementAsync(1, crossingId);

        var crossings = await _service.GetCrossingsAsync();

        var item = Assert.Single(crossings);
        Assert.Equal(tripId, item.TripId);
        Assert.Equal(1, item.PassengerCount);
        Assert.True(item.IsOpen);
        Assert.Equal("NDjili", item.ToTerminal);
    }

    [Fact]
    public async Task Update_ChangesTripAndCrossing_ButKeepsExternalNumber()
    {
        var (tripId, crossingId) = await RegisterAsync();

        await _service.UpdateAsync(crossingId, BorderOpsSeed.Arrival("AF101") with
        {
            Date = new DateTime(2026, 10, 17),
            DirectionCode = MovementService.ExitCode,
            CarrierId = BorderOpsSeed.CongoAirways,
            ExternalNumber = "IGNORE-LEgacy"
        });

        var trip = await _db.Trips.SingleAsync(t => t.Id == tripId);
        // Fidélité UpdateTrip : le numéro externe et la description ne sont pas modifiés.
        Assert.Equal("AF100", trip.ExternalNumber);
        Assert.Equal("Paris — Kinshasa", trip.Description);
        Assert.Equal("AF101", trip.Name);
        Assert.Equal(BorderOpsSeed.CongoAirways, trip.CarrierId);
        Assert.Equal("U001", trip.LastUpdateUser);

        var crossing = await _db.TripCrossings.SingleAsync(c => c.Id == crossingId);
        Assert.Equal(new DateTime(2026, 10, 17), crossing.Date);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowUpdated && a.Entity == nameof(Trip));
    }

    [Fact]
    public async Task SetOpen_CloseClearsOpenFields_OpenClearsCloseFields()
    {
        var (_, crossingId) = await RegisterAsync();

        await _service.SetOpenAsync(crossingId, false);
        var closed = await _db.TripCrossings.SingleAsync(c => c.Id == crossingId);
        Assert.False(closed.IsOpen);
        Assert.Null(closed.OpenTime);      // fidélité : OpenTime remis à NULL à la fermeture
        Assert.Null(closed.OpenUser);
        Assert.NotNull(closed.CloseTime);
        Assert.Equal("U001", closed.CloseUser);

        await _service.SetOpenAsync(crossingId, true);
        var reopened = await _db.TripCrossings.SingleAsync(c => c.Id == crossingId);
        Assert.True(reopened.IsOpen);
        Assert.NotNull(reopened.OpenTime);
        Assert.Null(reopened.CloseTime);
    }

    [Fact]
    public async Task Delete_RefusesWhenPassengersLinked()
    {
        var (_, crossingId) = await RegisterAsync();
        await AddMovementAsync(1, crossingId);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.DeleteAsync(crossingId));
        Assert.Equal("BO_Error_TripHasPassengers", ex.ErrorKey);
    }

    [Fact]
    public async Task Delete_RemovesCrossingAndTripOnlyWhenUnused()
    {
        var (tripId, firstCrossing) = await RegisterAsync();
        // Second passage du même voyage (départ).
        var secondCrossing = await _service.RegisterAsync(BorderOpsSeed.Arrival() with
        {
            Date = new DateTime(2026, 10, 20),
            DirectionCode = MovementService.ExitCode
        });
        // Rapprocher le second passage du même voyage (comme l'héritage : CreateTrip crée un
        // voyage par passage ; ici on rattache explicitement pour tester la garde).
        var second = await _db.TripCrossings.SingleAsync(c => c.Id == secondCrossing);
        second.TripId = tripId;
        await _db.SaveChangesAsync();

        await _service.DeleteAsync(firstCrossing);
        Assert.True(await _db.Trips.AnyAsync(t => t.Id == tripId));           // autre passage : conservé

        await _service.DeleteAsync(secondCrossing);
        Assert.False(await _db.Trips.AnyAsync(t => t.Id == tripId));          // plus de passage : supprimé
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowRevoked && a.Entity == nameof(Trip));
    }

    [Fact]
    public async Task Passengers_AreListedAndReassigned()
    {
        var (_, firstCrossing) = await RegisterAsync();
        var secondCrossing = await _service.RegisterAsync(BorderOpsSeed.Arrival("AF200"));
        var movementId = await AddMovementAsync(1, firstCrossing);

        var passengers = await _service.GetPassengersAsync(firstCrossing);
        var passenger = Assert.Single(passengers);
        Assert.Equal("MBALA JEAN", passenger.HolderName);
        Assert.Equal("OP123456", passenger.DocumentNumber);

        await _service.ReassignMovementAsync(movementId, secondCrossing);

        Assert.Empty(await _service.GetPassengersAsync(firstCrossing));
        Assert.Single(await _service.GetPassengersAsync(secondCrossing));
        Assert.Contains(_db.AuditLogs, a => a.Entity == nameof(Movement) && a.Details!.Contains("réaffecté"));
    }

    [Fact]
    public async Task Reassign_WithUnknownIds_IsRejected()
    {
        var (_, crossingId) = await RegisterAsync();

        var movement = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.ReassignMovementAsync(9999, crossingId));
        Assert.Equal("PAL_Error_MovementNotFound", movement.ErrorKey);

        var movementId = await AddMovementAsync(2, crossingId);
        var crossing = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.ReassignMovementAsync(movementId, 9999));
        Assert.Equal("BO_Error_CrossingNotFound", crossing.ErrorKey);
    }

    private async Task<int> AddMovementAsync(int personSeed, int crossingId)
    {
        var person = new Person { Id = personSeed };
        if (!await _db.Persons.AnyAsync(p => p.Id == personSeed)) _db.Persons.Add(person);
        var document = new TravelDocument
        {
            PersonId = personSeed, DocumentTypeId = 3, DocumentNumber = "OP123456",
            Surname = "MBALA", GivenName = "JEAN", DateOfBirth = new DateTime(1990, 5, 20)
        };
        _db.TravelDocuments.Add(document);
        await _db.SaveChangesAsync();

        var movement = new Movement
        {
            PersonId = personSeed, DocumentId = document.Id, TravelDate = DateTime.Today,
            DirectionLookupId = 1, RegistrationTime = DateTime.Today, Granted = true,
            TripCrossingId = crossingId
        };
        _db.Movements.Add(movement);
        await _db.SaveChangesAsync();
        return movement.Id;
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>Référentiels du transport : transporteurs, terminaux, navires.</summary>
public class TransportReferentialServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly TransportReferentialService _service;

    public TransportReferentialServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"bor-{Guid.NewGuid()}").Options);
        BorderOpsSeed.SeedReferentials(_db);
        _service = new TransportReferentialService(_db, _clock, new AuditWriter(_db, _currentUser, _clock));
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 1, BorderPointId = 2 }, "ADMIN");
    }

    [Fact]
    public async Task Carriers_SearchByCodeNameAndType()
    {
        Assert.Equal(2, (await _service.SearchCarriersAsync()).Count);
        Assert.Single(await _service.SearchCarriersAsync("af"));
        Assert.Single(await _service.SearchCarriersAsync("congo"));
        Assert.Equal(2, (await _service.SearchCarriersAsync(transportTypeCode: "Flight")).Count);
        Assert.Empty(await _service.SearchCarriersAsync(transportTypeCode: "Ship"));
    }

    [Fact]
    public async Task Carriers_CreateNormalizesCodeAndRejectsDuplicates()
    {
        var id = await _service.CreateCarrierAsync(new CarrierRequest("sn", "SABENA", null, "Flight", 900));

        var carrier = await _db.Carriers.SingleAsync(c => c.Id == id);
        Assert.Equal("SN", carrier.Code);
        Assert.Equal("SABENA", carrier.DisplayName);
        Assert.Contains(_db.AuditLogs, a => a.Entity == nameof(Carrier));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateCarrierAsync(new CarrierRequest("SN", "Autre", null, "Flight")));
        Assert.Equal("BO_Error_DuplicateCode", ex.ErrorKey);

        var missing = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateCarrierAsync(new CarrierRequest("", "Sans code", null, "Flight")));
        Assert.Equal("BO_Error_CodeRequired", missing.ErrorKey);
    }

    [Fact]
    public async Task Carriers_RevokeIsRefusedWhenUsedByTrip()
    {
        var tripService = new TripService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        await tripService.RegisterAsync(BorderOpsSeed.Arrival());

        var inUse = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.SetCarrierRevokedAsync(BorderOpsSeed.AirFrance, true));
        Assert.Equal("BO_Error_CarrierInUse", inUse.ErrorKey);

        await _service.SetCarrierRevokedAsync(BorderOpsSeed.CongoAirways, true);
        var revoked = await _db.Carriers.SingleAsync(c => c.Id == BorderOpsSeed.CongoAirways);
        Assert.True(revoked.Revoke);
        Assert.NotNull(revoked.RevokeDate);
        Assert.Single(await _service.SearchCarriersAsync());                       // révoqué masqué
        Assert.Equal(2, (await _service.SearchCarriersAsync(includeRevoked: true)).Count);
    }

    [Fact]
    public async Task Carriers_UpdateChangesNameAndType()
    {
        await _service.UpdateCarrierAsync(BorderOpsSeed.AirFrance,
            new CarrierRequest("AF", "Air France Cargo", null, "Ship"));

        var carrier = await _db.Carriers.Include(c => c.Cultures).SingleAsync(c => c.Id == BorderOpsSeed.AirFrance);
        Assert.Equal("Air France Cargo", carrier.DisplayName);
        Assert.Equal("Ship", (await _db.Lookups.SingleAsync(l => l.Id == carrier.TransportTypeId)).Code);
    }

    [Fact]
    public async Task Terminals_CreateWithCityAndRevokeGuard()
    {
        var id = await _service.CreateTerminalAsync(new TerminalRequest("fdu", "Bandundu Airport", null, "Flight", 900, 901));

        var terminal = await _db.Terminals.SingleAsync(t => t.Id == id);
        Assert.Equal("FDU", terminal.Code);
        Assert.Equal(901, terminal.CityId);

        var tripService = new TripService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        await tripService.RegisterAsync(BorderOpsSeed.Arrival());

        var inUse = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.SetTerminalRevokedAsync(BorderOpsSeed.FihTerminal, true));
        Assert.Equal("BO_Error_TerminalInUse", inUse.ErrorKey);

        await _service.SetTerminalRevokedAsync(id, true);
        Assert.True((await _db.Terminals.SingleAsync(t => t.Id == id)).Revoke);
    }

    [Fact]
    public async Task Terminals_SearchByCodeNameAndType()
    {
        Assert.Equal(2, (await _service.SearchTerminalsAsync()).Count);
        Assert.Single(await _service.SearchTerminalsAsync("ndjili"));
        Assert.Single(await _service.SearchTerminalsAsync("ORY"));
        Assert.Empty(await _service.SearchTerminalsAsync(transportTypeCode: "Ship"));
    }

    [Fact]
    public async Task Vessels_CreateUpdateSearch_WithValidations()
    {
        var id = await _service.CreateVesselAsync(new VesselRequest("Shamis", "QW12",
            FormerName: "Kadiye", NationalityCountryId: 900, VesselTypeCode: "Boat", Color: "Bleu"));

        var vessel = await _db.Vessels.SingleAsync(v => v.Id == id);
        Assert.Equal("Shamis", vessel.Name);
        Assert.Equal("Boat", (await _db.Lookups.SingleAsync(l => l.Id == vessel.VesselTypeId)).Code);

        Assert.Single(await _service.SearchVesselsAsync("sham"));
        Assert.Single(await _service.SearchVesselsAsync("QW12"));
        Assert.Single(await _service.SearchVesselsAsync("kadi"));   // ancien nom
        var item = (await _service.SearchVesselsAsync()).Single();
        Assert.Equal("Bateau", item.TypeFr);

        await _service.UpdateVesselAsync(id, new VesselRequest("Shamis II", "QW13"));
        Assert.Equal("Shamis II", (await _db.Vessels.SingleAsync(v => v.Id == id)).Name);

        var noName = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateVesselAsync(new VesselRequest("", "AA1")));
        Assert.Equal("BO_Error_NameRequired", noName.ErrorKey);

        var noRegistration = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateVesselAsync(new VesselRequest("Sans immat", "")));
        Assert.Equal("BO_Error_VesselRegistrationRequired", noRegistration.ErrorKey);
    }

    public void Dispose() => _db.Dispose();
}
