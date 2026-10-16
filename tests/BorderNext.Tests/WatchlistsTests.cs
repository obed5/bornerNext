using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Devices;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Référentiels et données communes aux tests des listes de surveillance (Lot 5).</summary>
internal static class WatchlistSeed
{
    public const int NationalCountry = 180;   // réglage hérité Settings.Country (RDC)
    public const int ForeignCountry = 250;    // pays étranger de test

    public static void SeedReferentials(BorderNextDbContext db)
    {
        DossierSeed.SeedReferentials(db);

        db.LookupTypes.Add(new LookupType
        {
            Id = 50, Code = "MATCH_STOPLIST_ACTION", NameFr = "Actions sur correspondance", NameEn = "Match actions",
            Items =
            {
                new Lookup { Id = 50, Code = "ZP100", SortOrder = 0, Cultures = { Fr("ZP100"), En("ZP100") } },
                new Lookup { Id = 51, Code = "ZP200", SortOrder = 1, Cultures = { Fr("ZP200"), En("ZP200") } }
            }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 51, Code = "STOP_LIST_SOURCE", NameFr = "Sources des listes", NameEn = "Stop list sources",
            Items =
            {
                new Lookup { Id = 52, Code = "POL", Cultures = { Fr("Police"), En("Police") } },
                new Lookup { Id = 53, Code = "INT", Cultures = { Fr("Interpol"), En("Interpol") } }
            }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 52, Code = "CHECKING_REASON", NameFr = "Motifs de contrôle", NameEn = "Checking reasons",
            Items =
            {
                new Lookup { Id = 54, Code = "FDP", Cultures = { Fr("Trouvé sur liste d'alerte des personnes"), En("Found on person alert list") } },
                new Lookup { Id = 55, Code = "SSC", Cultures = { Fr("Envoyé en seconde inspection"), En("Sent for secondary inspection") } }
            }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 53, Code = "ACTION", NameFr = "Actions", NameEn = "Actions",
            Items =
            {
                new Lookup { Id = 56, Code = "Permit", Cultures = { Fr("Autorisé"), En("Permitted") } },
                new Lookup { Id = 57, Code = "Refuse", Cultures = { Fr("Refuser"), En("Refuse") } }
            }
        });

        db.Settings.Add(new Setting { Type = "Country", Value = NationalCountry, Description = "Pays de référence" });
        db.Settings.Add(new Setting { Type = "CheckInDoubtablePersons", Value = 0 });
        db.SaveChanges();
    }

    /// <summary>Entrée de liste d'alerte personnes prête à l'emploi.</summary>
    public static PersonStopList Person(string surname = "MBALA", string givenName = "JEAN",
        DateTime? dob = null, int? citizenship = ForeignCountry, string? actionCode = null,
        string? documentNumber = null, string? personalNumber = null, string? surnameAlt = null)
        => new()
        {
            Surname = surname,
            GivenName = givenName,
            DateOfBirth = dob ?? new DateTime(1990, 5, 20),
            CitizenshipId = citizenship,
            DocumentNumber = documentNumber,
            PersonalNumber = personalNumber,
            SurnameAlt = surnameAlt,
            MatchInStopListActionId = actionCode switch { "ZP100" => 50, "ZP200" => 51, _ => null },
            SetUpDate = new DateTime(2026, 1, 1),
            UpdateDate = new DateTime(2026, 1, 1)
        };

    public static DocumentStopList Document(string number = "OP123456", string surname = "MBALA",
        string givenName = "JEAN", DateTime? dob = null, int countryId = ForeignCountry, string? actionCode = null)
        => new()
        {
            DocumentNumber = number,
            CountryId = countryId,
            Surname = surname,
            GivenName = givenName,
            DateOfBirth = dob ?? new DateTime(1990, 5, 20),
            MatchInStopListActionId = actionCode switch { "ZP100" => 50, "ZP200" => 51, _ => null },
            SetUpDate = new DateTime(2026, 1, 1),
            UpdateDate = new DateTime(2026, 1, 1)
        };

    private static LookupCulture Fr(string description) => new() { Culture = "fr", Description = description };
    private static LookupCulture En(string description) => new() { Culture = "en", Description = description };
}

/// <summary>Découpage des noms et distance de Levenshtein (héritage : SplitName, udf_GetLevenshteinDistance).</summary>
public class NameMatchingTests
{
    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("mbala", "mbala", 0)]
    [InlineData("mabala", "mbala", 1)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    public void Levenshtein_ComputesClassicDistances(string left, string right, int expected)
        => Assert.Equal(expected, NameMatching.Levenshtein(left, right));

    [Fact]
    public void Levenshtein_NullOperand_IsNull()
    {
        Assert.Null(NameMatching.Levenshtein(null, "abc"));
        Assert.Null(NameMatching.Levenshtein("abc", null));
    }

    [Fact]
    public void LevenshteinFolded_IgnoresCase()
        => Assert.Equal(0, NameMatching.LevenshteinFolded("MBALA", "mbala"));

    [Fact]
    public void SplitName_HeritageOrder_FullThenFirstThenRest()
    {
        Assert.Equal(new[] { "KABASELE WA MUKENDI", "KABASELE", "WA MUKENDI" },
            NameMatching.SplitName("KABASELE WA MUKENDI"));
        Assert.Equal(new[] { "MBALA" }, NameMatching.SplitName("MBALA"));
        Assert.Empty(NameMatching.SplitName(""));
        Assert.Empty(NameMatching.SplitName(null));
        Assert.Equal(new[] { " J", "", "J" }, NameMatching.SplitName(" J"));
    }

    [Fact]
    public void WithinBirthYearWindow_ExcludesFiveYears()
    {
        var reference = new DateTime(1990, 5, 20);
        Assert.True(NameMatching.WithinBirthYearWindow(new DateTime(1994, 1, 1), reference));
        Assert.True(NameMatching.WithinBirthYearWindow(new DateTime(1986, 12, 31), reference));
        Assert.False(NameMatching.WithinBirthYearWindow(new DateTime(1995, 1, 1), reference));
        Assert.False(NameMatching.WithinBirthYearWindow(null, reference));
    }
}

/// <summary>Moteur de correspondance (héritage : fn_CheckPersonIn*StopListWithSimulationOption).</summary>
public class StopListMatcherTests : IDisposable
{
    private static readonly DateTime Dob = new(1990, 5, 20);

    private readonly BorderNextDbContext _db;
    private readonly StopListMatcher _matcher;
    private readonly SystemClock _clock = new();

    public StopListMatcherTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"wl-{Guid.NewGuid()}").Options);
        WatchlistSeed.SeedReferentials(_db);
        _matcher = new StopListMatcher(_db, _clock);
    }

    private StopListCriteria Criteria(string? surname = "MBALA", string? givenName = "JEAN",
        string? documentNumber = null, string? personalNumber = null, DateTime? dob = null, int? citizenship = null)
        => new(surname, givenName, documentNumber, personalNumber, dob ?? Dob, citizenship ?? WatchlistSeed.ForeignCountry);

    private async Task SeedAsync(params PersonStopList[] entries)
    {
        _db.PersonsStopList.AddRange(entries);
        await _db.SaveChangesAsync();
    }

    // ---- Critères personnes --------------------------------------------------

    [Fact]
    public async Task Criterion1_National_RequiresExactPersonalNumber()
    {
        _db.PersonsStopList.Add(WatchlistSeed.Person(citizenship: WatchlistSeed.NationalCountry, personalNumber: "PN-001"));
        await _db.SaveChangesAsync();

        var exact = await _matcher.CheckPersonAsync(Criteria(surname: "ZZZZZZ", givenName: "QQQQ",
            personalNumber: "pn-001", dob: new DateTime(1955, 1, 1), citizenship: WatchlistSeed.NationalCountry));
        Assert.NotNull(exact);
        Assert.Equal(1, exact!.Criterion);

        // National : la distance de 1 ne suffit plus (héritage : seuil 0 pour les nationaux).
        var close = await _matcher.CheckPersonAsync(Criteria(surname: "ZZZZZZ", givenName: "QQQQ",
            personalNumber: "PN-002", dob: new DateTime(1955, 1, 1), citizenship: WatchlistSeed.NationalCountry));
        Assert.Null(close);
    }

    [Fact]
    public async Task Criterion1_Foreigner_ToleratesOneDifferentCharacter()
    {
        await SeedAsync(WatchlistSeed.Person(citizenship: WatchlistSeed.ForeignCountry, personalNumber: "PN-001"));

        var close = await _matcher.CheckPersonAsync(Criteria(surname: "ZZZZZZ", givenName: "QQQQ",
            personalNumber: "PN-002", dob: new DateTime(1955, 1, 1)));
        Assert.NotNull(close);
        Assert.Equal(1, close!.Criterion);

        var far = await _matcher.CheckPersonAsync(Criteria(surname: "ZZZZZZ", givenName: "QQQQ",
            personalNumber: "PN-999", dob: new DateTime(1955, 1, 1)));
        Assert.Null(far);
    }

    [Fact]
    public async Task Criterion1_EmptyPersonalNumber_IsSkipped()
    {
        await SeedAsync(WatchlistSeed.Person(citizenship: WatchlistSeed.ForeignCountry, personalNumber: "PN-001"));

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "ZZZZZZ", givenName: "QQQQ",
            personalNumber: "", dob: new DateTime(1955, 1, 1)));
        Assert.Null(hit);
    }

    [Fact]
    public async Task Criterion2_DocumentNumber_WithCitizenship()
    {
        await SeedAsync(WatchlistSeed.Person(citizenship: WatchlistSeed.ForeignCountry, documentNumber: "OP123456"));

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "ZZZZZZ", givenName: "QQQQ",
            documentNumber: "OP123457", dob: new DateTime(1955, 1, 1)));
        Assert.NotNull(hit);
        Assert.Equal(2, hit!.Criterion);

        // Nationalité différente : pas de correspondance (le critère exige l'égalité de nationalité).
        var other = await _matcher.CheckPersonAsync(Criteria(surname: "ZZZZZZ", givenName: "QQQQ",
            documentNumber: "OP123457", dob: new DateTime(1955, 1, 1), citizenship: WatchlistSeed.NationalCountry));
        Assert.Null(other);
    }

    [Fact]
    public async Task Criterion3_GivenNameExact_SurnameClose_DateExact()
    {
        await SeedAsync(WatchlistSeed.Person());   // MBALA / JEAN, 1990-05-20

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "MABALA"));

        Assert.NotNull(hit);
        Assert.Equal(3, hit!.Criterion);
    }

    [Fact]
    public async Task Criterion3_UsesSplitNameParts()
    {
        await SeedAsync(WatchlistSeed.Person(surname: "KABASELE", givenName: "JEAN"));

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "KABASELE WA MUKENDI", givenName: "JEAN PIERRE"));

        Assert.NotNull(hit);
        Assert.Equal(3, hit!.Criterion);
    }

    [Fact]
    public async Task Criterion3_AltSurname_IsTested()
    {
        await SeedAsync(WatchlistSeed.Person(surname: "KABASELE", givenName: "JEAN", surnameAlt: "MBALA"));

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "MABALA"));

        Assert.NotNull(hit);
        Assert.Equal(3, hit!.Criterion);
    }

    [Fact]
    public async Task Criterion4_SurnameExact_GivenNameClose()
    {
        await SeedAsync(WatchlistSeed.Person());

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "MBALA", givenName: "JEAM"));

        Assert.NotNull(hit);
        Assert.Equal(4, hit!.Criterion);
    }

    [Fact]
    public async Task Criterion5_BothNamesClose_DateExact()
    {
        await SeedAsync(WatchlistSeed.Person());

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "MABALA", givenName: "JEAM"));

        Assert.NotNull(hit);
        Assert.Equal(5, hit!.Criterion);
    }

    [Fact]
    public async Task Criterion6_ExactNames_BirthYearWithinFourYears()
    {
        await SeedAsync(WatchlistSeed.Person());

        var hit = await _matcher.CheckPersonAsync(Criteria(dob: new DateTime(1994, 1, 1)));
        Assert.NotNull(hit);
        Assert.Equal(6, hit!.Criterion);

        var outside = await _matcher.CheckPersonAsync(Criteria(dob: new DateTime(1995, 1, 1)));
        Assert.Null(outside);
    }

    [Fact]
    public async Task Criterion7_InvertedNames_DateExact()
    {
        await SeedAsync(WatchlistSeed.Person(surname: "JEAN", givenName: "MBALA"));

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "MBALA", givenName: "JEAN"));

        Assert.NotNull(hit);
        Assert.Equal(7, hit!.Criterion);
    }

    [Fact]
    public async Task CriteriaPriority_DocumentBeatsNames()
    {
        _db.PersonsStopList.Add(WatchlistSeed.Person(documentNumber: "OP777"));
        _db.PersonsStopList.Add(WatchlistSeed.Person(surname: "MABALA"));
        await _db.SaveChangesAsync();

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "MABALA", documentNumber: "OP777"));

        Assert.NotNull(hit);
        Assert.Equal(2, hit!.Criterion);
    }

    [Fact]
    public async Task EntryPriority_ActionCodeThenIdentifier_NullActionFirst()
    {
        var withZP200 = WatchlistSeed.Person(actionCode: "ZP200");
        var withZP100 = WatchlistSeed.Person(actionCode: "ZP100");
        var withoutAction = WatchlistSeed.Person();
        await SeedAsync(withZP200, withZP100, withoutAction);

        var hit = await _matcher.CheckPersonAsync(Criteria());

        // ORDER BY ExternalID ASC hérité : valeurs nulles d'abord, puis ZP100 puis ZP200.
        Assert.NotNull(hit);
        Assert.Equal(withoutAction.Id, hit!.EntryId);
        Assert.Null(hit.ActionCode);
    }

    [Fact]
    public async Task EntryPriority_WithoutNullAction_UsesCodeOrder()
    {
        var withZP200 = WatchlistSeed.Person(actionCode: "ZP200");
        var withZP100 = WatchlistSeed.Person(actionCode: "ZP100");
        await SeedAsync(withZP200, withZP100);

        var hit = await _matcher.CheckPersonAsync(Criteria());

        Assert.NotNull(hit);
        Assert.Equal(withZP100.Id, hit!.EntryId);
        Assert.Equal("ZP100", hit.ActionCode);
    }

    [Fact]
    public async Task CancelledEntry_IsIgnored_ButFutureCancellationStaysActive()
    {
        var cancelled = WatchlistSeed.Person();
        cancelled.CancelDate = _clock.Now.AddDays(-1);
        var futureCancelled = WatchlistSeed.Person(surname: "MOYAMBE");
        futureCancelled.CancelDate = _clock.Now.AddDays(30);
        await SeedAsync(cancelled, futureCancelled);

        Assert.Null(await _matcher.CheckPersonAsync(Criteria()));
        Assert.NotNull(await _matcher.CheckPersonAsync(Criteria(surname: "MOYAMBE")));
    }

    [Fact]
    public async Task NullDateOfBirth_SkipsNameCriteria_ButKeepsNumberCriteria()
    {
        await SeedAsync(WatchlistSeed.Person(personalNumber: "PN-500", citizenship: WatchlistSeed.ForeignCountry));

        var withNullDob = new StopListCriteria("MBALA", "JEAN", null, null, null, WatchlistSeed.ForeignCountry);
        Assert.Null(await _matcher.CheckPersonAsync(withNullDob));

        var byNumber = new StopListCriteria("MBALA", "JEAN", null, "PN-500", null, WatchlistSeed.ForeignCountry);
        Assert.NotNull(await _matcher.CheckPersonAsync(byNumber));
    }

    [Fact]
    public async Task OnlyEntryId_RestrictsMatch()
    {
        var first = WatchlistSeed.Person(surname: "KABASELE", documentNumber: "OP111");
        var second = WatchlistSeed.Person(surname: "MOYAMBE", documentNumber: "OP222");
        await SeedAsync(first, second);

        var hit = await _matcher.CheckPersonAsync(Criteria(surname: "MOYAMBE", documentNumber: "OP222"), onlyEntryId: first.Id);
        Assert.Null(hit);

        var direct = await _matcher.CheckPersonAsync(Criteria(surname: "MOYAMBE", documentNumber: "OP222"), onlyEntryId: second.Id);
        Assert.NotNull(direct);
        Assert.Equal(second.Id, direct!.EntryId);
    }

    // ---- Critères documents --------------------------------------------------

    private async Task SeedDocumentsAsync(params DocumentStopList[] entries)
    {
        _db.DocumentsStopList.AddRange(entries);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Document_Criterion1_GivenNameExact_SurnameClose()
    {
        await SeedDocumentsAsync(WatchlistSeed.Document());

        var hit = await _matcher.CheckDocumentAsync(Criteria(surname: "MABALA"));

        Assert.NotNull(hit);
        Assert.Equal(1, hit!.Criterion);
        Assert.Equal(StopListKind.Document, hit.Kind);
    }

    [Fact]
    public async Task Document_Criterion2_SurnameExact_GivenNameClose()
    {
        await SeedDocumentsAsync(WatchlistSeed.Document());

        var hit = await _matcher.CheckDocumentAsync(Criteria(givenName: "JEAM"));

        Assert.NotNull(hit);
        Assert.Equal(2, hit!.Criterion);
    }

    [Fact]
    public async Task Document_Criterion3_BothNamesClose()
    {
        await SeedDocumentsAsync(WatchlistSeed.Document());

        var hit = await _matcher.CheckDocumentAsync(Criteria(surname: "MABALA", givenName: "JEAM"));

        Assert.NotNull(hit);
        Assert.Equal(3, hit!.Criterion);
    }

    [Fact]
    public async Task Document_Criterion4_ExactNames_BirthYearWindow()
    {
        await SeedDocumentsAsync(WatchlistSeed.Document());

        var hit = await _matcher.CheckDocumentAsync(Criteria(dob: new DateTime(1993, 8, 1)));
        Assert.NotNull(hit);
        Assert.Equal(4, hit!.Criterion);
    }

    [Fact]
    public async Task Document_Criterion5_InvertedNames()
    {
        await SeedDocumentsAsync(WatchlistSeed.Document(surname: "JEAN", givenName: "MBALA"));

        var hit = await _matcher.CheckDocumentAsync(Criteria(surname: "MBALA", givenName: "JEAN"));

        Assert.NotNull(hit);
        Assert.Equal(5, hit!.Criterion);
    }

    [Fact]
    public async Task Document_Criterion6_CountryAndNumber_ThresholdByNationality()
    {
        await SeedDocumentsAsync(WatchlistSeed.Document(number: "OP123456", countryId: WatchlistSeed.ForeignCountry));

        var noDob = new StopListCriteria("MBALA", "JEAN", "OP123457", null, null, WatchlistSeed.ForeignCountry);
        var hit = await _matcher.CheckDocumentAsync(noDob);
        Assert.NotNull(hit);
        Assert.Equal(6, hit!.Criterion);

        // National : correspondance exacte exigée (seuil 0).
        var national = new StopListCriteria("MBALA", "JEAN", "OP123457", null, null, WatchlistSeed.NationalCountry);
        Assert.Null(await _matcher.CheckDocumentAsync(national));

        var nationalExact = new StopListCriteria("MBALA", "JEAN", "OP123456", null, null, WatchlistSeed.NationalCountry);
        await SeedDocumentsAsync(WatchlistSeed.Document(number: "OP654321", countryId: WatchlistSeed.NationalCountry));
        Assert.NotNull(await _matcher.CheckDocumentAsync(nationalExact with { DocumentNumber = "OP654321" }));
    }

    [Fact]
    public async Task Document_CancelledEntry_IsIgnored()
    {
        var cancelled = WatchlistSeed.Document();
        cancelled.CancelDate = _clock.Now.AddDays(-1);
        await SeedDocumentsAsync(cancelled);

        Assert.Null(await _matcher.CheckDocumentAsync(Criteria()));
    }

    // ---- Recherche par type (simulateur) et contrôle inverse ------------------

    [Fact]
    public async Task SearchPersons_Type1_ExactNamesAndDate_Type5_YearWindow()
    {
        await SeedAsync(WatchlistSeed.Person(), WatchlistSeed.Person(surname: "KABASELE", dob: new DateTime(1975, 1, 1)));

        var exact = await _matcher.SearchPersonsAsync(1, Criteria());
        Assert.Single(exact);
        Assert.Equal("MBALA", exact[0].Surname);

        var window = await _matcher.SearchPersonsAsync(5, Criteria(surname: "KABASELE", dob: new DateTime(1973, 1, 1)));
        Assert.Single(window);
        Assert.Equal("KABASELE", window[0].Surname);
    }

    [Fact]
    public async Task SearchPersons_Type7_InvertedNames()
    {
        await SeedAsync(WatchlistSeed.Person(surname: "JEAN", givenName: "MBALA"));

        var results = await _matcher.SearchPersonsAsync(7, Criteria(surname: "MBALA", givenName: "JEAN"));

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchPersons_Type8_DocumentNumberClose()
    {
        await SeedAsync(WatchlistSeed.Person(citizenship: WatchlistSeed.ForeignCountry, documentNumber: "OP123456"));

        var results = await _matcher.SearchPersonsAsync(8, Criteria(documentNumber: "OP123457"));

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchDocuments_Type2_AndType7()
    {
        await SeedDocumentsAsync(WatchlistSeed.Document(),
            WatchlistSeed.Document(number: "OP999", surname: "JEAN", givenName: "MBALA"));

        var byNumber = await _matcher.SearchDocumentsAsync(2,
            new StopListCriteria(null, null, "OP123456", null, null, WatchlistSeed.ForeignCountry));
        Assert.Single(byNumber);

        var inverted = await _matcher.SearchDocumentsAsync(7, Criteria(surname: "MBALA", givenName: "JEAN"));
        Assert.Single(inverted);
        Assert.Equal("OP999", inverted[0].DocumentNumber);
    }

    [Fact]
    public async Task ReverseCheck_MatchesRegisteredDocument()
    {
        var entry = WatchlistSeed.Person(surname: "MABALA", givenName: "JEAN", citizenship: 1);
        await SeedAsync(entry);
        _db.TravelDocuments.Add(new TravelDocument
        {
            PersonId = 1, DocumentTypeId = 3, DocumentNumber = "OP123456",
            Surname = "MBALA", GivenName = "JEAN", DateOfBirth = Dob, CitizenshipId = 1,
            CreateDate = _clock.Now
        });
        await _db.SaveChangesAsync();

        var documents = await _matcher.MatchEntryAgainstRegisterAsync(entry.Id);

        Assert.Single(documents);
        Assert.Equal("OP123456", documents[0].DocumentNumber);
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>Gestion des entrées de liste d'alerte (héritage : PersonsStopListForm, DocumentStopListForm).</summary>
public class WatchlistServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly WatchlistService _service;

    public WatchlistServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"wls-{Guid.NewGuid()}").Options);
        WatchlistSeed.SeedReferentials(_db);
        _service = new WatchlistService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock),
            new SimulatedFingerprintMatcher());
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 4, BorderPointId = 2 }, "ALERT_OFF");
        _currentUser.SetPcNumber("A01");
    }

    private static PersonStopListRequest Request(string? surname = "MBALA", string? documentNumber = null,
        string? personalNumber = null)
        => new(Surname: surname, GivenName: "JEAN", DateOfBirth: new DateTime(1990, 5, 20),
            CitizenshipId: WatchlistSeed.ForeignCountry, StopListSourceId: 52, SourceDocument: "REF-01",
            DocumentNumber: documentNumber, PersonalNumber: personalNumber, MatchInStopListActionId: 50);

    [Fact]
    public async Task CreatePerson_WithoutIdentifier_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreatePersonAsync(new PersonStopListRequest(StopListSourceId: 52)));
        Assert.Equal("WL_Error_IdentifierRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task CreateAndUpdatePerson_SetsAuditAndRegistrationFields()
    {
        var id = await _service.CreatePersonAsync(Request());

        var entry = await _db.PersonsStopList.SingleAsync(p => p.Id == id);
        Assert.Equal("MBALA", entry.Surname);
        Assert.Equal("U001", entry.WhoSetUp);
        Assert.Equal("U001", entry.LastUpdateUser);
        Assert.NotNull(entry.SetUpDate);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(PersonStopList));

        await _service.UpdatePersonAsync(id, Request(surname: "MOYAMBE"));
        Assert.Equal("MOYAMBE", (await _db.PersonsStopList.SingleAsync(p => p.Id == id)).Surname);
    }

    [Fact]
    public async Task SearchPersons_MatchesAltNamesAndCancelledFilter()
    {
        var id = await _service.CreatePersonAsync(Request());   // MBALA / JEAN
        var altId = await _service.CreatePersonAsync(new PersonStopListRequest(
            Surname: "KABEYA", SurnameAlt: "MABALAINE", GivenName: "PAUL",
            CitizenshipId: WatchlistSeed.ForeignCountry, StopListSourceId: 52));
        await _service.CancelPersonAsync(altId, 52, "REF-CANCEL");

        // Recherche par nom principal : l'entrée active est trouvée.
        var active = await _service.SearchPersonsAsync(new PersonStopListSearch(Surname: "MBALA"));
        var onlyActive = Assert.Single(active);
        Assert.Equal(id, onlyActive.Id);

        // La variante « Alt » est cherchée comme le nom principal (héritage : LIKE sur les deux) :
        // l'entrée annulée n'apparaît que sur demande.
        Assert.Empty(await _service.SearchPersonsAsync(new PersonStopListSearch(Surname: "MABALAINE")));
        var withCancelled = await _service.SearchPersonsAsync(new PersonStopListSearch(Surname: "MABALAINE", ShowCancelled: true));
        Assert.Single(withCancelled);
        Assert.Equal(altId, withCancelled[0].Id);
    }

    [Fact]
    public async Task CancelPerson_SetsMotiveAndRejectsSecondCancellation()
    {
        var id = await _service.CreatePersonAsync(Request());

        await _service.CancelPersonAsync(id, 53, "REF-ANN");

        var entry = await _db.PersonsStopList.SingleAsync(p => p.Id == id);
        Assert.NotNull(entry.CancelDate);
        Assert.Equal(53, entry.CancelStopListSourceId);
        Assert.Equal("REF-ANN", entry.CancelSourceDocument);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.CancelPersonAsync(id, 53, null));
        Assert.Equal("WL_Error_AlreadyCancelled", ex.ErrorKey);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowRevoked && a.Entity == nameof(PersonStopList));
    }

    [Fact]
    public async Task SetPersonAuthorized_TogglesFlag()
    {
        var id = await _service.CreatePersonAsync(Request());

        await _service.SetPersonAuthorizedAsync(id, true);
        Assert.True((await _db.PersonsStopList.SingleAsync(p => p.Id == id)).IsAuthorized);

        await _service.SetPersonAuthorizedAsync(id, false);
        Assert.False((await _db.PersonsStopList.SingleAsync(p => p.Id == id)).IsAuthorized);
    }

    [Fact]
    public async Task Documents_CreateSearchCancel()
    {
        var id = await _service.CreateDocumentAsync(new DocumentStopListRequest(
            DocumentTypeId: 3, DocumentNumber: "OP123456", CountryId: WatchlistSeed.ForeignCountry,
            Surname: "MBALA", GivenName: "JEAN", StopListSourceId: 52, MatchInStopListActionId: 51));

        Assert.Single(await _service.SearchDocumentsAsync(new DocumentStopListSearch(DocumentNumber: "OP123")));

        await _service.CancelDocumentAsync(id, 52, "REF-DOC");
        Assert.Empty(await _service.SearchDocumentsAsync(new DocumentStopListSearch(DocumentNumber: "OP123")));
        Assert.Single(await _service.SearchDocumentsAsync(new DocumentStopListSearch(DocumentNumber: "OP123", ShowCancelled: true)));
    }

    [Fact]
    public async Task Documents_WithoutNumberOrPersonalNumber_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateDocumentAsync(new DocumentStopListRequest(Surname: "MBALA")));
        Assert.Equal("WL_Error_IdentifierRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task Fingerprints_AddReplaceListAndIdentify()
    {
        var id = await _service.CreatePersonAsync(Request());
        var scanner = new SimulatedFingerprintScanner { SelectedIdentity = "SIM-1 (démonstration)" };
        var capture = await scanner.CaptureAsync(1);

        await _service.AddFingerprintAsync(id, 1, capture.Image, capture.Template);
        Assert.Single(await _service.GetFingerprintsAsync(id));

        // Remplacement du même doigt (clé composite héritée StopListID + FingerNumber).
        var replacement = await scanner.CaptureAsync(1);
        await _service.AddFingerprintAsync(id, 1, replacement.Image, replacement.Template);
        Assert.Single(await _service.GetFingerprintsAsync(id));

        var matches = await _service.IdentifyFingerprintAsync(capture.Template);
        Assert.Single(matches);
        Assert.Equal(id, matches[0].PersonStopListId);
        Assert.Equal(1, matches[0].FingerNumber);

        scanner.SelectedIdentity = "SIM-2";
        var unknown = await scanner.CaptureAsync(1);
        Assert.Empty(await _service.IdentifyFingerprintAsync(unknown.Template));
    }

    [Fact]
    public async Task Fingerprints_InvalidFingerNumber_IsRejected()
    {
        var id = await _service.CreatePersonAsync(Request());
        var scanner = new SimulatedFingerprintScanner();
        var capture = await scanner.CaptureAsync(1);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.AddFingerprintAsync(id, 11, capture.Image, capture.Template));
        Assert.Equal("WL_Error_FingerNumberRange", ex.ErrorKey);
    }

    [Fact]
    public async Task Fingerprints_RemoveDeletesPrint()
    {
        var id = await _service.CreatePersonAsync(Request());
        var scanner = new SimulatedFingerprintScanner();
        var capture = await scanner.CaptureAsync(2);
        await _service.AddFingerprintAsync(id, 2, capture.Image, capture.Template);

        await _service.RemoveFingerprintAsync(id, 2);

        Assert.Empty(await _service.GetFingerprintsAsync(id));
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>Personnes douteuses : file superviseur, décisions, compteur (héritage : DoubtablePersons).</summary>
public class DoubtablePersonServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly DoubtablePersonService _service;
    private int _movementId;

    public DoubtablePersonServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"dp-{Guid.NewGuid()}").Options);
        WatchlistSeed.SeedReferentials(_db);
        _service = new DoubtablePersonService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3, BorderPointId = 2 }, "IMMIG_OFF");
        _currentUser.SetPcNumber("A01");

        var person = new Person();
        _db.Persons.Add(person);
        var document = new TravelDocument
        {
            PersonId = person.Id, DocumentTypeId = 3, DocumentNumber = "OP123456",
            Surname = "MBALA", GivenName = "JEAN", DateOfBirth = new DateTime(1990, 5, 20), CitizenshipId = 1
        };
        _db.TravelDocuments.Add(document);
        _db.SaveChanges();

        var movement = new Movement
        {
            PersonId = person.Id, DocumentId = document.Id, TravelDate = DateTime.Today,
            DirectionLookupId = 1, RegistrationTime = DateTime.Today, Granted = true
        };
        _db.Movements.Add(movement);
        _db.SaveChanges();
        _movementId = movement.Id;
    }

    [Fact]
    public async Task Register_CreatesPendingDoubtWithInspector()
    {
        var id = await _service.RegisterAsync(new DoubtablePersonRequest(_movementId, 54, "alerte liste personnes"));

        var entry = await _db.DoubtablePersons.SingleAsync(d => d.Id == id);
        Assert.Equal(DoubtfulWorkflowStatus.Pending, entry.WorkflowStatus);
        Assert.Equal("U001", entry.CreatedByUserId);
        Assert.Equal(54, entry.CheckingReasonId);
        Assert.Equal((short)2, entry.BorderPointId);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(DoubtablePerson));

        Assert.Equal(1, await _service.GetOpenCountAsync());
        var pending = await _service.GetPendingAsync();
        Assert.Single(pending);
        Assert.Equal("MBALA JEAN", pending[0].HolderName);
    }

    [Fact]
    public async Task Register_SecondDoubtForSameMovement_IsRejected()
    {
        await _service.RegisterAsync(new DoubtablePersonRequest(_movementId));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(new DoubtablePersonRequest(_movementId)));
        Assert.Equal("WL_Error_DoubtAlreadyExists", ex.ErrorKey);
    }

    [Fact]
    public async Task Process_SetsSupervisorDecisionAndMirrorsStatus()
    {
        var id = await _service.RegisterAsync(new DoubtablePersonRequest(_movementId));

        await _service.ProcessAsync(id, new DoubtableProcessRequest("refusé en seconde inspection", 57));

        var entry = await _db.DoubtablePersons.SingleAsync(d => d.Id == id);
        Assert.Equal(DoubtfulWorkflowStatus.Processed, entry.WorkflowStatus);
        Assert.Equal("jdoe", entry.Supervisor);
        Assert.Equal("U001", entry.SupervisorUserId);
        Assert.Equal("A01", entry.SupervisorPcNumber);
        Assert.NotNull(entry.SupervisorRegistrationTime);
        Assert.Equal(57, entry.PerformedActionId);
        Assert.Equal(57, entry.Status);   // colonne héritée jointe à lActions
        Assert.Equal(0, await _service.GetOpenCountAsync());

        var processed = await _service.SearchAsync(DoubtfulWorkflowStatus.Processed);
        Assert.Single(processed);
        Assert.Equal("Refuser", processed[0].PerformedActionFr);
    }

    [Fact]
    public async Task Process_UnknownAction_IsRejected()
    {
        var id = await _service.RegisterAsync(new DoubtablePersonRequest(_movementId));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.ProcessAsync(id, new DoubtableProcessRequest("note", 999)));
        Assert.Equal("WL_Error_ActionUnknown", ex.ErrorKey);
    }

    [Fact]
    public async Task Process_AlreadyProcessed_IsRejected()
    {
        var id = await _service.RegisterAsync(new DoubtablePersonRequest(_movementId));
        await _service.ProcessAsync(id, new DoubtableProcessRequest("ok", 56));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.ProcessAsync(id, new DoubtableProcessRequest("encore", 57)));
        Assert.Equal("WL_Error_NotPending", ex.ErrorKey);
    }

    [Fact]
    public async Task CancelDoubt_SetsStatusThree()
    {
        var id = await _service.RegisterAsync(new DoubtablePersonRequest(_movementId));

        await _service.CancelAsync(id, "doute levé par le superviseur");

        var entry = await _db.DoubtablePersons.SingleAsync(d => d.Id == id);
        Assert.Equal(DoubtfulWorkflowStatus.Cancelled, entry.WorkflowStatus);
        Assert.Equal("doute levé par le superviseur", entry.SupervisorNote);
        Assert.Equal(0, await _service.GetOpenCountAsync());
    }

    [Fact]
    public async Task Process_WithRefusalFields_RecordsRefusal()
    {
        var id = await _service.RegisterAsync(new DoubtablePersonRequest(_movementId));

        await _service.ProcessAsync(id, new DoubtableProcessRequest("refus", 57,
            RefusalOfficerName: "MUKENDI Paul", RefusalOfficerRank: "Capitaine", RefusalDocNumber: "AG-77",
            RefusalCountryId: WatchlistSeed.NationalCountry, RefusalJustificationId: null));

        var entry = await _db.DoubtablePersons.SingleAsync(d => d.Id == id);
        Assert.Equal("MUKENDI Paul", entry.RefusalOfficerName);
        Assert.Equal("Capitaine", entry.RefusalOfficerRank);
        Assert.NotNull(entry.RefusalCreateDate);
        var item = (await _service.SearchAsync()).Single();
        Assert.True(item.HasRefusal);
    }

    [Fact]
    public async Task HasPriorAction_RequiresActionBeyondPermit()
    {
        var id = await _service.RegisterAsync(new DoubtablePersonRequest(_movementId));
        // Status 1 = « Permit » : pas d'antécédent défavorable.
        await _service.ProcessAsync(id, new DoubtableProcessRequest("admis", 56));
        Assert.False(await _service.HasPriorActionAsync("MBALA", "JEAN", new DateTime(1990, 5, 20), "OP123456"));

        // Nouveau doute refusé (Status 2 > 1) : antécédent détecté.
        var movement2 = new Movement
        {
            PersonId = _db.Movements.Single(m => m.Id == _movementId).PersonId,
            DocumentId = _db.Movements.Single(m => m.Id == _movementId).DocumentId,
            TravelDate = DateTime.Today.AddDays(1), DirectionLookupId = 1,
            RegistrationTime = DateTime.Today.AddDays(1), Granted = false
        };
        _db.Movements.Add(movement2);
        await _db.SaveChangesAsync();
        var second = await _service.RegisterAsync(new DoubtablePersonRequest(movement2.Id));
        await _service.ProcessAsync(second, new DoubtableProcessRequest("refusé", 57));

        Assert.True(await _service.HasPriorActionAsync("mbala", "jean", new DateTime(1990, 5, 20), "op123456"));
        Assert.False(await _service.HasPriorActionAsync("AUTRE", "JEAN", new DateTime(1990, 5, 20), "OP123456"));
    }

    [Fact]
    public async Task AutoCheckSetting_TogglesValue()
    {
        Assert.False(await _service.IsAutoCheckEnabledAsync());

        await _service.SetAutoCheckAsync(true);

        Assert.True(await _service.IsAutoCheckEnabledAsync());
        Assert.Equal(1, _db.Settings.Single(s => s.Type == "CheckInDoubtablePersons").Value);
    }

    [Fact]
    public async Task CheckingReasonLookup_ResolvesCodeToIdentifier()
    {
        Assert.Equal(54, await _service.GetCheckingReasonIdAsync("FDP"));
        Assert.Null(await _service.GetCheckingReasonIdAsync("ZZZ"));
    }

    public void Dispose() => _db.Dispose();
}
