using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Persistence.Seeding;
using BorderNext.Migration;
using BorderNext.Migration.Legacy;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;

namespace BorderNext.Tests;

/// <summary>
/// Tests du moteur de reprise d'historique (Lot 9) : rapprochement des nomenclatures,
/// création des documents à partir des mouvements, rattachement différé des visas,
/// rejets motivés, idempotence et mode simulation. La source d'origine est simulée
/// (aucune base réelle n'est nécessaire pour ces tests).
/// </summary>
public class MigrationTests : IDisposable
{
    private readonly BorderNextDbContext _db;

    public MigrationTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"mig-{Guid.NewGuid()}")
            .Options);
    }

    private static MigrationOptions Options(bool dryRun = false, params string[] sections)
        => new(PageSize: 2, DryRun: dryRun) { Sections = sections };

    private async Task<HistoryMigrator> MigrateAsync(FakeLegacySource source, MigrationOptions? options = null)
    {
        // Référentiels de l'application en place (comme sur un poste déjà installé).
        await ReferenceDataSeeder.SeedAsync(_db);
        var migrator = new HistoryMigrator(source, _db, options ?? Options());
        await migrator.RunAsync();
        return migrator;
    }

    [Fact]
    public async Task Migrate_ReconcilesLookups_AndCreatesMissingItems()
    {
        var source = new FakeLegacySource();
        // Document type « OP » existe déjà côté cible ; « TST » est propre à l'origine.
        source.Lookups[("lDocumentTypes", "DOCUMENT_TYPE")] = new[]
        {
            Lookup(1, "OP", ("fr", "Passeport ordinaire"), ("en", "Ordinary passport")),
            Lookup(9, "TST", ("fr", "Document d'essai"), ("en", "Fixture document")),
        };


        var migrator = await MigrateAsync(source);

        var counters = migrator.Counters.For("DOCUMENT_TYPE");
        Assert.Equal(1, counters.Written);   // TST créé
        Assert.Equal(1, counters.Skipped);   // OP rapproché

        var created = await _db.Lookups.Include(l => l.Cultures)
            .SingleAsync(l => l.Code == "TST" && l.Type!.Code == "DOCUMENT_TYPE");
        Assert.Contains(created.Cultures, c => c.Culture == "fr" && c.Description == "Document d'essai");
        Assert.Contains(created.Cultures, c => c.Culture == "en" && c.Description == "Fixture document");
    }

    [Fact]
    public async Task Migrate_DossierChain_PersonDocumentMovement()
    {
        var source = new FakeLegacySource();
        source.Persons.Add(new LegacyPerson("P1", "legacy", new DateTime(2026, 9, 1), 2));
        source.Documents["D1"] = Document("D1", "OP000001", "MARTIN", "CLAIRE", 1, 1);
        source.Movements.Add(Movement("R1", "D1", "P1", granted: true));

        var migrator = await MigrateAsync(source);

        var person = await _db.Persons.SingleAsync(p => p.LegacyId == "P1");
        var document = await _db.TravelDocuments.SingleAsync(d => d.LegacyId == "D1");
        var movement = await _db.Movements.SingleAsync(m => m.LegacyId == "R1");

        Assert.Equal(person.Id, document.PersonId);
        Assert.Equal(document.Id, movement.DocumentId);
        Assert.Equal(person.Id, movement.PersonId);
        Assert.True(movement.Granted);
        Assert.Equal(1, migrator.Counters.For("Documents").Written);
        Assert.Equal(1, migrator.Counters.For("PassportRegistrationData").Written);
    }

    [Fact]
    public async Task Migrate_Refusal_KeepsJustificationAndDirection()
    {
        var source = new FakeLegacySource();
        source.Persons.Add(new LegacyPerson("P1", null, null, 2));
        source.Documents["D1"] = Document("D1", "OP000001", "ESSOR", "LINE", 1, 3);
        source.Movements.Add(Movement("R1", "D1", "P1", granted: false) with { DeportationReasonId = 4 });

        await MigrateAsync(source);

        var movement = await _db.Movements.SingleAsync(m => m.LegacyId == "R1");
        Assert.False(movement.Granted);
        Assert.NotNull(movement.DeportationReasonId);
    }

    [Fact]
    public async Task Migrate_VisaIsLinkedToMovement_AfterVisaPass()
    {
        var source = new FakeLegacySource();
        source.Persons.Add(new LegacyPerson("P1", null, null, 2));
        source.Documents["D1"] = Document("D1", "OP000001", "DUBOIS", "PATRICK", 2, 2);
        source.Movements.Add(Movement("R1", "D1", "P1", granted: true) with { VisaId = "V1" });
        source.Visas.Add(new LegacyVisa("V1", "D1", 1, 1, "VISA-2026-000001", new DateTime(2026, 8, 1),
            new DateTime(2027, 2, 1), 180, null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, "A02", false, null, new DateTime(2026, 8, 1), "legacy",
            new DateTime(2026, 8, 1), "legacy", 2));

        await MigrateAsync(source);

        var movement = await _db.Movements.SingleAsync(m => m.LegacyId == "R1");
        var visa = await _db.Visas.SingleAsync(v => v.LegacyId == "V1");
        Assert.Equal(visa.Id, movement.VisaId);
    }

    [Fact]
    public async Task Migrate_UnknownReferences_AreRejectedWithReason()
    {
        var source = new FakeLegacySource();
        source.Persons.Add(new LegacyPerson("P1", null, null, 2));
        // Mouvement dont le document n'existe pas dans l'origine, et document orphelin.
        source.Movements.Add(Movement("R1", "ABSENT", "P1", granted: true));

        var migrator = await MigrateAsync(source);

        var counters = migrator.Counters.For("PassportRegistrationData");
        Assert.Equal(1, counters.Rejected);
        Assert.Contains("document absent de la cible", counters.RejectReasons.Keys);
        Assert.Equal(0, await _db.Movements.CountAsync());
    }

    [Fact]
    public async Task Migrate_IsIdempotent()
    {
        var source = new FakeLegacySource();
        source.Persons.Add(new LegacyPerson("P1", null, null, 2));
        source.Documents["D1"] = Document("D1", "OP000001", "MARTIN", "CLAIRE", 1, 1);
        source.Movements.Add(Movement("R1", "D1", "P1", granted: true));
        await MigrateAsync(source);

        var second = new HistoryMigrator(source, _db, Options());
        await second.RunAsync();

        Assert.Equal(0, second.Counters.TotalWritten);
        Assert.Equal(1, await _db.Persons.CountAsync(p => p.LegacyId != null));
        Assert.Equal(1, await _db.Movements.CountAsync(m => m.LegacyId != null));
    }

    [Fact]
    public async Task Migrate_DryRun_WritesNothing()
    {
        var source = new FakeLegacySource();
        source.Persons.Add(new LegacyPerson("P1", null, null, 2));
        source.Documents["D1"] = Document("D1", "OP000001", "MARTIN", "CLAIRE", 1, 1);
        source.Movements.Add(Movement("R1", "D1", "P1", granted: true));

        await MigrateAsync(source, Options(dryRun: true));

        Assert.Equal(0, await _db.Persons.CountAsync(p => p.LegacyId != null));
        Assert.Equal(0, await _db.Movements.CountAsync());
    }

    [Fact]
    public async Task Migrate_SectionFilter_LimitsScope()
    {
        var source = new FakeLegacySource();
        source.Persons.Add(new LegacyPerson("P1", null, null, 2));
        source.Documents["D1"] = Document("D1", "OP000001", "MARTIN", "CLAIRE", 1, 1);
        source.Movements.Add(Movement("R1", "D1", "P1", granted: true));

        await MigrateAsync(source, Options(false, "persons"));

        Assert.Equal(1, await _db.Persons.CountAsync(p => p.LegacyId != null));
        Assert.Equal(0, await _db.Movements.CountAsync());
    }

    [Fact]
    public async Task Migrate_StopListsAndDoubtfulPersons_KeepLegacyLinks()
    {
        var source = new FakeLegacySource();
        source.Persons.Add(new LegacyPerson("P1", null, null, 2));
        source.Documents["D1"] = Document("D1", "OP000001", "ESSOR", "LINE", 1, 3);
        source.Movements.Add(Movement("R1", "D1", "P1", granted: false));
        source.PersonStopList.Add(new LegacyPersonStopList("S1", null, "ESSOR", "LINE", null, null, null, null,
            new DateTime(1995, 7, 21), 1, 3, "legacy", new DateTime(2026, 8, 1), null, null, null,
            "Signalement", null, null, "TST000003", null, "Signaler", null, new DateTime(2026, 8, 1), "legacy",
            null, null, null, true));
        source.Doubtful.Add(new LegacyDoubtablePerson("R1", "Superviseur", new DateTime(2026, 9, 3, 10, 30, 0),
            "A09", 2, "S1", null, "Note inspecteur", "Note superviseur", null, null, null, null, null, null, null,
            null, null));

        await MigrateAsync(source);

        var stopList = await _db.PersonsStopList.SingleAsync(s => s.LegacyId == "S1");
        Assert.True(stopList.IsAuthorized);
        var doubtful = await _db.Set<DoubtablePerson>().SingleAsync();
        Assert.Equal("Note inspecteur", doubtful.InspectorNote);
        Assert.Equal("Note superviseur", doubtful.SupervisorNote);
        Assert.Equal("Superviseur", doubtful.Supervisor);
        Assert.Equal("A09", doubtful.SupervisorPcNumber);
    }

    [Fact]
    public async Task Migrate_PermitsAndMedicalHistory_AreCarried()
    {
        var source = new FakeLegacySource();
        source.Persons.Add(new LegacyPerson("P1", null, null, 2));
        source.Documents["D1"] = Document("D1", "OP000001", "DUBOIS", "PATRICK", 2, 2);
        source.Movements.Add(Movement("R1", "D1", "P1", granted: true));
        source.Permits.Add(new LegacyPermit("PM1", "D1", null, null, "PERM-2026-0001",
            new DateTime(2026, 8, 15), new DateTime(2027, 8, 14), null, null, null, "Commerçant", null, null,
            "Direction générale", null, new DateTime(2026, 8, 15), "legacy", new DateTime(2026, 8, 15), "legacy",
            "Permis d'essai", "A02"));
        source.MedicalHistories.Add(new LegacyMedicalHistory("PM1", null, "Aucune", null,
            MalariaSuffered: false, YellowFeverSuffered: false, TuberculosisSuffered: false, MeaslesSuffered: true,
            CholeraSuffered: false, TyphoidSuffered: false, JaundiceSuffered: false, ChronicCoughSuffered: false,
            OtherSuffered: null, YellowFeverVaccinated: true, MMRVaccinated: false, DiphtheriaVaccinated: true,
            PolioVaccinated: true, HealthCentreVisited: true, ChronicDisease: false, InfectiousDisease: false,
            IsPublicHealthCentre: true, UpdateDate: new DateTime(2026, 8, 15), UpdateUser: "legacy"));

        await MigrateAsync(source);

        var permit = await _db.Permits.SingleAsync(p => p.LegacyId == "PM1");
        var history = await _db.Set<PermitMedicalHistory>().SingleAsync(h => h.PermitId == permit.Id);
        Assert.True(history.YellowFeverVaccinated);
        Assert.True(history.IsPublicHealthCentre);
        Assert.Equal("Aucune", history.CurrentIllness);
    }

    // -----------------------------------------------------------------------
    // Source simulée (aucune base d'origine nécessaire)
    // -----------------------------------------------------------------------

    private static LegacyLookupItem Lookup(short id, string code, params (string Culture, string Label)[] labels)
        => new(id, code, 10, false, null, labels.ToDictionary(l => l.Culture, l => l.Label));

    private static LegacyDocument Document(string id, string number, string surname, string givenName,
        short documentTypeId, short citizenship)
        => new(id, documentTypeId, number, 2, new DateTime(2022, 5, 20), new DateTime(2032, 5, 19),
            surname, givenName, null, new DateTime(1990, 4, 12), 1, citizenship,
            new DateTime(2026, 9, 1), 2, "legacy", null, new DateTime(2022, 5, 20), "legacy");

    private static LegacyMovement Movement(string id, string documentId, string personId, bool granted)
        => new(id, documentId, new DateTime(2026, 9, 2, 9, 20, 0), null, null, 1, false, personId, null, granted,
            null, false, false, null, 0, null, 1, 2, null, null, null, 10, "legacy", "A01",
            new DateTime(2026, 9, 2, 9, 30, 0), new DateTime(2026, 9, 2, 9, 30, 0), 2);

    private sealed class FakeLegacySource : ILegacySource
    {
        public FakeLegacySource()
        {
            // Nomenclatures d'origine : un poste réel en a toujours (les codes sont ceux
            // du système précédent, repris par les référentiels de l'application).
            Lookups[("lDocumentTypes", "DOCUMENT_TYPE")] = new[]
            {
                Lookup(1, "OP", ("fr", "Passeport ordinaire"), ("en", "Ordinary passport")),
                Lookup(2, "PD", ("fr", "Passeport diplomatique"), ("en", "Diplomatic passport")),
            };
            Lookups[("lMovementDirections", "MOVEMENT_DIRECTION")] = new[]
            {
                Lookup(1, "Entry", ("fr", "Entrée"), ("en", "Entry")),
                Lookup(2, "Exit", ("fr", "Sortie"), ("en", "Exit")),
            };
            Lookups[("lSex", "SEX")] = new[]
            {
                Lookup(1, "F", ("fr", "Féminin"), ("en", "Female")),
                Lookup(2, "M", ("fr", "Masculin"), ("en", "Male")),
            };
            Lookups[("lVisaTypes", "VISA_TYPE")] = new[]
            {
                Lookup(1, "Tourist Visa", ("fr", "Visa de tourisme"), ("en", "Tourist visa")),
            };
            Lookups[("lDeportationReasons", "DEPORTATION_REASON")] = new[]
            {
                Lookup(4, "Visa absence", ("fr", "Absence de visa"), ("en", "No visa")),
            };
            Lookups[("lRefusalJustifications", "REFUSAL_JUSTIFICATION")] = new[]
            {
                Lookup(2, "RefusalOfEntry", ("fr", "Refus d'entrée"), ("en", "Refusal of entry")),
            };
        }

        public Dictionary<(string Table, string TypeCode), IReadOnlyList<LegacyLookupItem>> Lookups { get; } = new();
        public List<LegacyPerson> Persons { get; } = new();
        public Dictionary<string, LegacyDocument> Documents { get; } = new(StringComparer.Ordinal);
        public List<LegacyMovement> Movements { get; } = new();
        public List<LegacyVisa> Visas { get; } = new();
        public List<LegacyPersonStopList> PersonStopList { get; } = new();
        public List<LegacyPermit> Permits { get; } = new();
        public List<LegacyMedicalHistory> MedicalHistories { get; } = new();
        public List<LegacyDoubtablePerson> Doubtful { get; } = new();

        public Task<IReadOnlyList<string>> GetCulturesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "fr", "en" });

        public Task<IReadOnlyList<LegacyLookupItem>> ReadLookupAsync(LegacyLookup table, CancellationToken ct)
            => Task.FromResult(Lookups.TryGetValue((table.LegacyTable, table.LookupTypeCode), out var items)
                ? items
                : Array.Empty<LegacyLookupItem>());

        public Task<IReadOnlyList<LegacyCountry>> ReadCountriesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<LegacyCountry>>(Array.Empty<LegacyCountry>());

        public Task<IReadOnlyList<LegacyBorderPoint>> ReadBorderPointsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<LegacyBorderPoint>>(new[]
            {
                new LegacyBorderPoint(2, "FIH", 10, false, new Dictionary<string, string>
                {
                    ["fr"] = "Kinshasa-N'djili",
                    ["en"] = "Kinshasa-N'djili",
                }),
            });

        public Task<long> CountAsync(LegacyTable table, CancellationToken ct) => Task.FromResult(0L);

        public Task<bool> TableExistsAsync(LegacyTable table, CancellationToken ct) => Task.FromResult(true);

        private static async IAsyncEnumerable<IReadOnlyList<T>> Page<T>(IReadOnlyList<T> rows, int pageSize)
        {
            await Task.CompletedTask;
            for (var i = 0; i < rows.Count; i += pageSize)
                yield return rows.Skip(i).Take(pageSize).ToList();
            if (rows.Count == 0) yield break;
        }

        public async IAsyncEnumerable<IReadOnlyList<LegacyPerson>> ReadPersonsAsync(int pageSize, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(Persons.ToList(), pageSize)) yield return page;
        }

        public Task<IReadOnlyList<LegacyDocument>> ReadDocumentsByIdsAsync(IReadOnlyCollection<string> ids,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<LegacyDocument>>(
                ids.Where(Documents.ContainsKey).Select(id => Documents[id]).ToList());

        public Task<IReadOnlyList<LegacyPhoto>> ReadPhotosAsync(IReadOnlyCollection<string> documentIds,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<LegacyPhoto>>(Array.Empty<LegacyPhoto>());

        public async IAsyncEnumerable<IReadOnlyList<LegacyMovement>> ReadMovementsAsync(int pageSize,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(Movements.ToList(), pageSize)) yield return page;
        }

        public async IAsyncEnumerable<IReadOnlyList<LegacyChild>> ReadChildrenAsync(int pageSize, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(Array.Empty<LegacyChild>(), pageSize)) yield return page;
        }

        public async IAsyncEnumerable<IReadOnlyList<LegacyVisa>> ReadVisasAsync(int pageSize, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(Visas.ToList(), pageSize)) yield return page;
        }

        public async IAsyncEnumerable<IReadOnlyList<LegacyVisaChild>> ReadVisaChildrenAsync(int pageSize,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(Array.Empty<LegacyVisaChild>(), pageSize)) yield return page;
        }

        public async IAsyncEnumerable<IReadOnlyList<LegacyPersonStopList>> ReadPersonStopListAsync(int pageSize,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(PersonStopList.ToList(), pageSize)) yield return page;
        }

        public async IAsyncEnumerable<IReadOnlyList<LegacyDocumentStopList>> ReadDocumentStopListAsync(int pageSize,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(Array.Empty<LegacyDocumentStopList>(), pageSize)) yield return page;
        }

        public async IAsyncEnumerable<IReadOnlyList<LegacyDoubtablePerson>> ReadDoubtablePersonsAsync(int pageSize,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(Doubtful.ToList(), pageSize)) yield return page;
        }

        public async IAsyncEnumerable<IReadOnlyList<LegacyPermit>> ReadPermitsAsync(int pageSize, [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(Permits.ToList(), pageSize)) yield return page;
        }

        public async IAsyncEnumerable<IReadOnlyList<LegacyMedicalHistory>> ReadMedicalHistoriesAsync(int pageSize,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var page in Page(MedicalHistories.ToList(), pageSize)) yield return page;
        }
    }

    public void Dispose() => _db.Dispose();
}
