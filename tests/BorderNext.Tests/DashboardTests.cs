using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Jeu de données du tableau de bord : mouvements du jour, années précédentes,
/// inspecteurs, douteux, vols, refus et amendes.</summary>
internal static class DashboardSeed
{
    public const short FihBorderPoint = 2;   // déjà semé par DossierSeed (FIH)
    public const short LufuBorderPoint = 3;

    public static void Seed(BorderNextDbContext db)
    {
        DossierSeed.SeedReferentials(db);

        db.Countries.Add(new Country
        {
            Id = 700, Alpha3 = "COD",
            Cultures = { new CountryCulture { Culture = "fr", Name = "CONGO, LA R.D.C." } }
        });
        // Le poste FIH existe déjà (DossierSeed) : on précise son libellé et on ajoute Lufu.
        var fih = db.BorderPoints.Include(b => b.Cultures).Single(b => b.Id == FihBorderPoint);
        fih.Cultures.First().Name = "Kinshasa";
        db.BorderPoints.Add(new BorderPoint
        {
            Id = LufuBorderPoint, ExternalId = "LUFU", SortOrder = 1,
            Cultures = { new BorderPointCulture { Culture = "fr", Name = "Lufu" } }
        });
        db.Settings.Add(new Setting { Type = "Terminal", Value = 10 });
        db.SaveChanges();
    }

    /// <summary>Mouvement daté avec sens, inspecteur, poste et nationalité.</summary>
    public static Movement Movement(DateTime travelDate, string directionCode, string inspector,
        short borderPointId, string documentNumber, bool granted = true, int? tripCrossingId = null, int personId = 1)
        => new()
        {
            PersonId = personId,
            DocumentId = 0,   // posé par l'appelant après création du document
            TravelDate = travelDate.Date,
            DirectionLookupId = directionCode == "Entry" ? 1 : 2,
            Granted = granted,
            Inspector = inspector,
            BorderPointId = borderPointId,
            RegistrationTime = travelDate.Date,
            TripCrossingId = tripCrossingId,
            Document = new TravelDocument
            {
                PersonId = personId, DocumentTypeId = 3, DocumentNumber = documentNumber,
                Surname = "MBALA", GivenName = "JEAN", DateOfBirth = new DateTime(1990, 5, 20),
                CitizenshipId = 700
            }
        };
}

/// <summary>Tableau de bord du poste (héritage : schéma dashboard).</summary>
public class DashboardServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 16, 9, 30, 0));
    private readonly DashboardService _service;

    public DashboardServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"dash-{Guid.NewGuid()}").Options);
        DashboardSeed.Seed(_db);
        _service = new DashboardService(_db, _currentUser, _clock);
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 2, BorderPointId = DashboardSeed.FihBorderPoint }, "SHIFT_OFF");
    }

    private async Task AddTripsAsync()
    {
        var flight = _db.Lookups.Single(l => l.Type!.Code == "TRANSPORT_TYPE" && l.Code == "Flight");
        var terminal = new Terminal { Id = 10, Code = "FIH", TransportTypeId = flight.Id, Cultures = { new TerminalCulture { Culture = "fr", Description = "NDjili" } } };
        var terminalTo = new Terminal { Id = 11, Code = "ORY", TransportTypeId = flight.Id, Cultures = { new TerminalCulture { Culture = "fr", Description = "Orly" } } };
        _db.Terminals.AddRange(terminal, terminalTo);
        var trip = new Trip { Name = "AF100", TransportTypeId = flight.Id, FromTerminalId = 11, ToTerminalId = 10, UpdateDate = _clock.Now };
        _db.Trips.Add(trip);
        await _db.SaveChangesAsync();

        var open = new TripCrossing { TripId = trip.Id, DirectionLookupId = 1, Date = _clock.Now.Date,
            IsOpen = true, OpenTime = _clock.Now.TimeOfDay, OpenUser = "U001", UpdateDate = _clock.Now };
        var closed = new TripCrossing { TripId = trip.Id, DirectionLookupId = 2, Date = _clock.Now.Date,
            IsOpen = false, CloseTime = _clock.Now.TimeOfDay, CloseUser = "U001", UpdateDate = _clock.Now };
        _db.TripCrossings.AddRange(open, closed);
        await _db.SaveChangesAsync();

        // Mouvements du jour rattachés au passage ouvert (deux passagers, un seul vol)
        _db.Movements.AddRange(
            DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP1", tripCrossingId: open.Id, personId: 11),
            DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP2", tripCrossingId: open.Id, personId: 12));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Summary_CountsTodayAndComparesThreeYears()
    {
        _db.Movements.AddRange(
            DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP1"),
            DashboardSeed.Movement(_clock.Now, "Exit", "kabila", DashboardSeed.FihBorderPoint, "OP2"),
            DashboardSeed.Movement(_clock.Now.AddYears(-1), "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP3"),
            DashboardSeed.Movement(_clock.Now.AddYears(-2), "Exit", "jdoe", DashboardSeed.LufuBorderPoint, "OP4"),
            DashboardSeed.Movement(_clock.Now.AddDays(-3), "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP5"));
        await _db.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(new DateTime(2026, 10, 16), summary.Date);
        Assert.Equal(2, summary.TransactionsToday);
        Assert.Equal(1, summary.TransactionsYearAgo);
        Assert.Equal(1, summary.TransactionsTwoYearsAgo);
        Assert.Equal(1, summary.DeltaYearAgo);
        Assert.Equal(1, summary.EntriesToday);
        Assert.Equal(1, summary.ExitsToday);
        Assert.Equal("FIH — Kinshasa", summary.StationName);
    }

    [Fact]
    public async Task Summary_CountsActiveInspectorsByDirection()
    {
        _db.Movements.AddRange(
            DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP1"),
            DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP2"),
            DashboardSeed.Movement(_clock.Now, "Exit", "kabila", DashboardSeed.FihBorderPoint, "OP3"),
            DashboardSeed.Movement(_clock.Now.AddDays(-1), "Entry", "hier", DashboardSeed.FihBorderPoint, "OP4"));
        await _db.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(2, summary.ActiveInspectors);        // jdoe + kabila (pas celui d'hier)
        Assert.Equal(1, summary.ArrivalInspectors);
        Assert.Equal(1, summary.ExitInspectors);
    }

    [Fact]
    public async Task Summary_CountsDoubtfulPendingAndResolvedToday()
    {
        var movement = DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP1");
        _db.Movements.Add(movement);
        await _db.SaveChangesAsync();

        _db.DoubtablePersons.AddRange(
            new DoubtablePerson { MovementId = movement.Id, WorkflowStatus = DoubtfulWorkflowStatus.Pending, CreateDate = _clock.Now },
            new DoubtablePerson { MovementId = movement.Id, WorkflowStatus = DoubtfulWorkflowStatus.Processed, CreateDate = _clock.Now, PerformedActionId = 5 });
        await _db.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(1, summary.DoubtfulPending);
        Assert.Equal(1, summary.DoubtfulResolvedToday);
    }

    [Fact]
    public async Task Summary_CountsOpenTripsAndFlightsToday()
    {
        await AddTripsAsync();

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(1, summary.OpenTrips);      // un seul passage ouvert
        Assert.Equal(1, summary.FlightsToday);   // deux passagers, un seul vol
    }

    [Fact]
    public async Task Summary_CountsRefusalsAndPenaltiesToday()
    {
        var refused = DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP1", granted: false);
        _db.Movements.Add(refused);
        await _db.SaveChangesAsync();
        _db.PenaltyTickets.Add(new PenaltyTicket
        {
            MovementId = refused.Id, PenaltyReasonId = 1, Amount = 250m, CreateDate = _clock.Now, UserId = "U001"
        });
        _db.PenaltyTickets.Add(new PenaltyTicket
        {
            MovementId = refused.Id, PenaltyReasonId = 1, Amount = 100m, CreateDate = _clock.Now.AddDays(-2), UserId = "U001"
        });
        await _db.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(1, summary.RefusalsToday);
        Assert.Equal(1, summary.PenaltiesToday);
    }

    [Fact]
    public async Task Summary_BreaksTransactionsDownByBorderPoint()
    {
        _db.Movements.AddRange(
            DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP1"),
            DashboardSeed.Movement(_clock.Now, "Exit", "jdoe", DashboardSeed.LufuBorderPoint, "OP2"),
            DashboardSeed.Movement(_clock.Now.AddYears(-1), "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP3"));
        await _db.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync();

        var fih = summary.PerBorderPoint.Single(p => p.Code == "FIH");
        Assert.Equal(1, fih.Today);
        Assert.Equal(1, fih.YearAgo);
        Assert.Equal(0, fih.TwoYearsAgo);

        var lufu = summary.PerBorderPoint.Single(p => p.Code == "LUFU");
        Assert.Equal(1, lufu.Today);
        Assert.Equal("Lufu", lufu.NameFr);
    }

    [Fact]
    public async Task Summary_RanksTopNationalities()
    {
        _db.Movements.AddRange(
            DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP1"),
            DashboardSeed.Movement(_clock.Now, "Entry", "jdoe", DashboardSeed.FihBorderPoint, "OP2"),
            DashboardSeed.Movement(_clock.Now, "Exit", "jdoe", DashboardSeed.FihBorderPoint, "OP3"));
        await _db.SaveChangesAsync();

        var summary = await _service.GetSummaryAsync();

        var top = Assert.Single(summary.TopNationalities);
        Assert.Equal("COD", top.Alpha3);
        Assert.Equal(3, top.Count);
        Assert.Equal("CONGO, LA R.D.C.", top.NameFr);
    }

    [Fact]
    public async Task Summary_EmptyDay_ReturnsZerosAndNoStationNameWhenUnset()
    {
        _currentUser.Set(new User { Id = "U002", UserName = "alone", UserRoleId = 2 }, "SHIFT_OFF");

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(0, summary.TransactionsToday);
        Assert.Equal(0, summary.DoubtfulPending);
        Assert.Equal(0, summary.OpenTrips);
        Assert.Empty(summary.TopNationalities);
        Assert.Equal(string.Empty, summary.StationName);
    }

    public void Dispose() => _db.Dispose();

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime Now { get; } = now;
    }
}
