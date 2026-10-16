using BorderNext.Application.Abstractions;
using BorderNext.Application.Edi;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Référentiels et données communes aux tests des échanges (Lot 7).</summary>
internal static class ExchangeSeed
{
    public const int NationalCountry = 1;
    public const string MessageDirectorySetting = "Exchange_MessageDirectory";

    /// <summary>Répertoire temporaire isolé par test (fichiers échangés).</summary>
    public static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "bn-exchanges", Guid.NewGuid().ToString("N"));

    public static void SeedReferentials(BorderNextDbContext db)
    {
        // BorderOpsSeed inclut WatchlistSeed (listes d'alerte) qui inclut DossierSeed.
        BorderOpsSeed.SeedReferentials(db);

        db.Settings.Add(new Setting { Type = MessageDirectorySetting, StringValue = NewDirectory() });

        // Définitions d'échange (héritage : ImportExportManager) : mêmes canaux que le seed applicatif.
        db.ExchangeTasks.AddRange(
            new ExchangeTask
            {
                Name = "Import PAXLST (EDIFACT)", Channel = ExchangeChannels.Edifact,
                Direction = ExchangeDirections.Import, IsActive = true, Priority = 10,
            },
            new ExchangeTask
            {
                Name = "SIMIEC — registre de l'immigration", Channel = ExchangeChannels.Simiec,
                Direction = ExchangeDirections.Check, IsActive = true, Parameters = "MODE=MOCK", Priority = 40,
            },
            new ExchangeTask
            {
                Name = "Belize — cartes et permis de séjour", Channel = ExchangeChannels.Belize,
                Direction = ExchangeDirections.Check, IsActive = true, Parameters = "MODE=MOCK", Priority = 50,
            },
            new ExchangeTask
            {
                Name = "Système aérien — liste noire / dernier mouvement", Channel = ExchangeChannels.Air,
                Direction = ExchangeDirections.Check, IsActive = true, Parameters = "MODE=MOCK", Priority = 60,
            });
        db.SaveChanges();
    }

    /// <summary>Message PAXLST d'exemple (1 passager + 1 équipage), séparateurs standards.</summary>
    public const string SamplePaxlst =
        "UNA:+.? '\n" +
        "UNB+UNOA:1+BORDERNEXT+SITE2+261015:1430+000000001'\n" +
        "UNH+1+PAXLST:D:05B:UN:IATA'\n" +
        "BGM+745+1+9'\n" +
        "TDT+20+AF100+AF:Air France'\n" +
        "LOC+87+ORY'\n" +
        "LOC+125+FIH'\n" +
        "DTM+189:202610151430:203'\n" +
        "DTM+232:202610152230:203'\n" +
        "NAD+MS+++KINSHASA AIRPORT'\n" +
        "COM+243810000000:TE'\n" +
        "NAD+FL+++D?'ARC:JEAN:PAUL'\n" +
        "DTM+329:19900520:102'\n" +
        "ATT+2+M'\n" +
        "NAT+2:CD'\n" +
        "DOC+P+OP123456+CD:20240110:20300109'\n" +
        "RFF+ABO:REF001'\n" +
        "NAD+FM+++PILOTE:MARC'\n" +
        "DTM+174:19800101:102'\n" +
        "ATT+2+1'\n" +
        "CNT+41:1'\n" +
        "CNT+42:1'\n" +
        "UNT+21+1'\n" +
        "UNZ+1+000000001'";

    /// <summary>Modèle prêt à générer (équivalent du message d'exemple).</summary>
    public static PaxlstMessage Model(string flight = "AF100", string passenger = "D'ARC")
    {
        var message = new PaxlstMessage
        {
            Flight = new PaxlstFlight
            {
                Id = flight,
                CarrierCode = "AF",
                CarrierName = "Air France",
                DeparturePort = "ORY",
                ArrivalPort = "FIH",
                DepartureDate = new DateTime(2026, 10, 15, 14, 30, 0),
                ArrivalDate = new DateTime(2026, 10, 15, 22, 30, 0),
            },
            ReportingParty = new PaxlstReportingParty { Name = "KINSHASA AIRPORT", ContactNumber = "243810000000" },
        };

        message.Travellers.Add(new PaxlstTraveller
        {
            Type = PaxlstTravellerType.Passenger,
            Surname = passenger,
            FirstName = "JEAN",
            MiddleName = "PAUL",
            DateOfBirth = new DateTime(1990, 5, 20),
            Sex = "M",
            Nationality = "CD",
            DocumentType = "P",
            DocumentNumber = "OP123456",
            CountryOfIssue = "CD",
            IssueDate = new DateTime(2024, 1, 10),
            ExpiryDate = new DateTime(2030, 1, 9),
            BookingReference = "REF001",
        });
        message.Travellers.Add(new PaxlstTraveller
        {
            Type = PaxlstTravellerType.Crew,
            Surname = "PILOTE",
            FirstName = "MARC",
            DateOfBirth = new DateTime(1980, 1, 1),
            Sex = "M",
        });

        message.ControlTotals.Add(new PaxlstControlTotal(PaxlstControlTotal.Passengers, 1));
        message.ControlTotals.Add(new PaxlstControlTotal(PaxlstControlTotal.Crew, 1));

        return message;
    }
}

/// <summary>Analyse de l'enveloppe EDIFACT (UNB/UNH/UNT/UNZ, UNA, libérations, compteurs).</summary>
public class EdifactParserTests
{
    [Fact]
    public void Parse_ReadsInterchangeAndMessageEnvelopes()
    {
        var interchange = EdifactParser.Parse(ExchangeSeed.SamplePaxlst);

        Assert.True(interchange.HasUna);
        Assert.Equal("BORDERNEXT", interchange.Sender);
        Assert.Equal("SITE2", interchange.Recipient);
        Assert.Equal("000000001", interchange.InterchangeControlReference);
        Assert.Equal(new DateTime(2026, 10, 15, 14, 30, 0), interchange.Date);

        var message = Assert.Single(interchange.Messages);
        Assert.Equal("PAXLST", message.Type);
        Assert.Equal("D", message.Version);
        Assert.Equal("05B", message.Release);
        Assert.Equal("UN", message.ControllingAgency);
        Assert.Equal("IATA", message.AssociationCode);
    }

    [Fact]
    public void Parse_WithoutUna_UsesDefaultSeparators()
    {
        var content = ExchangeSeed.SamplePaxlst[9..]; // retire le segment UNA
        var interchange = EdifactParser.Parse(content);

        Assert.False(interchange.HasUna);
        Assert.Equal("BORDERNEXT", interchange.Sender);
    }

    [Fact]
    public void Parse_CustomSeparatorsFromUna_AreApplied()
    {
        // UNA avec « | » élément, « ; » composant, « ! » libération et « ~ » terminateur.
        const string content = "UNA;|.!*~" +
            "UNB|UNOA;1|SENDER|RECIPIENT|261015;1430|CTRL1~" +
            "UNH|1|PAXLST;D;05B;UN;IATA~" +
            "BGM|745|1|9~" +
            "NAD|FL|||DUPONT;JEAN~" +
            "UNT|4|1~" +
            "UNZ|1|CTRL1~";

        var interchange = EdifactParser.Parse(content);
        var message = Assert.Single(interchange.Messages);

        Assert.Equal('|', interchange.Syntax.DataElementSeparator);
        Assert.Equal(';', interchange.Syntax.ComponentDataSeparator);
        Assert.Equal('~', interchange.Syntax.SegmentTerminator);
        Assert.Equal("SENDER", interchange.Sender);
        Assert.Equal("PAXLST", message.Type);

        var nad = message.Segments.Single(s => s.Tag == "NAD");
        Assert.Equal("DUPONT", nad.Component(4, 1));
        Assert.Equal("JEAN", nad.Component(4, 2));
    }

    [Fact]
    public void Parse_ReleaseCharacterProtectsSeparators()
    {
        var message = Assert.Single(PaxlstParser.Parse(ExchangeSeed.SamplePaxlst));
        var passenger = message.Passengers.Single();

        Assert.Equal("D'ARC", passenger.Surname);
    }

    [Fact]
    public void Parse_UntCountMismatch_IsRejected()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNT+21+1'", "UNT+19+1'");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse(content));

        Assert.Equal("Edi_Error_UntCountMismatch", exception.ErrorKey);
    }

    [Fact]
    public void Parse_UntReferenceMismatch_IsRejected()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNT+21+1'", "UNT+21+2'");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse(content));

        Assert.Equal("Edi_Error_UntReferenceMismatch", exception.ErrorKey);
    }

    [Fact]
    public void Parse_UnzCountMismatch_IsRejected()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNZ+1+000000001'", "UNZ+2+000000001'");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse(content));

        Assert.Equal("Edi_Error_UnzCountMismatch", exception.ErrorKey);
    }

    [Fact]
    public void Parse_UnzReferenceMismatch_IsRejected()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNZ+1+000000001'", "UNZ+1+000000002'");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse(content));

        Assert.Equal("Edi_Error_UnzReferenceMismatch", exception.ErrorKey);
    }

    [Fact]
    public void Parse_MissingUnb_IsRejected()
    {
        // Sans UNA ni enveloppe UNB (le flux commence au premier message).
        var content = ExchangeSeed.SamplePaxlst
            .Replace("UNA:+.? '\n", "")
            .Replace("UNB+UNOA:1+BORDERNEXT+SITE2+261015:1430+000000001'\n", "");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse(content));

        Assert.Equal("Edi_Error_UnbMissing", exception.ErrorKey);
    }

    [Fact]
    public void Parse_MissingUnz_IsRejected()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNZ+1+000000001'", "");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse(content));

        Assert.Equal("Edi_Error_UnzMissing", exception.ErrorKey);
    }

    [Fact]
    public void Parse_MissingUnt_IsRejected()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNT+21+1'", "");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse(content));

        Assert.Equal("Edi_Error_UntMissing", exception.ErrorKey);
    }

    [Fact]
    public void Parse_SegmentOutsideMessage_IsRejected()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNZ+1+000000001'", "FTX+AAI+++hors message'UNZ+1+000000001'");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse(content));

        Assert.Equal("Edi_Error_SegmentOutsideMessage", exception.ErrorKey);
    }

    [Fact]
    public void Parse_DataAfterUnz_IsRejected()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNZ+1+000000001'", "UNZ+1+000000001'FTX+AAI+++après'");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse(content));

        Assert.Equal("Edi_Error_DataAfterUnz", exception.ErrorKey);
    }

    [Fact]
    public void Parse_EmptyContent_IsRejected()
    {
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.Parse("   "));
        Assert.Equal("Edi_Error_Empty", exception.ErrorKey);
    }

    [Fact]
    public void Parse_MultipleMessages_SharesEnvelope()
    {
        var generated = PaxlstGenerator.Generate(
            new[] { ExchangeSeed.Model("AF100"), ExchangeSeed.Model("AF200") },
            new PaxlstGeneratorOptions { ControlReference = "CTRL9", PreparedAt = new DateTime(2026, 10, 15, 14, 30, 0) });

        var interchange = EdifactParser.Parse(generated);

        Assert.Equal(2, interchange.Messages.Count);
        Assert.Equal("AF100", PaxlstParser.ParseMessage(interchange.Messages[0]).Flight.Id);
        Assert.Equal("AF200", PaxlstParser.ParseMessage(interchange.Messages[1]).Flight.Id);
    }

    [Fact]
    public void Parse_FunctionalGroups_AreTraversed()
    {
        var content = ExchangeSeed.SamplePaxlst
            .Replace("UNH+1+PAXLST", "UNG+PAXLST+SENDER+RECIPIENT+261015:1430+1+UN+D:05B'UNH+1+PAXLST")
            .Replace("UNT+21+1'", "UNT+21+1'UNE+1+1'");

        var interchange = EdifactParser.Parse(content);
        Assert.Single(interchange.Messages);
    }

    [Fact]
    public void ParsePaxlst_OtherMessageType_IsRejected()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNH+1+PAXLST:D:05B:UN:IATA'", "UNH+1+CUSCAR:D:96A:UN'");
        var exception = Assert.Throws<EdifactParseException>(() => EdifactParser.ParsePaxlstMessages(content));

        Assert.Equal("Edi_Error_NotPaxlst", exception.ErrorKey);
    }
}

/// <summary>Lecture typée PAXLST : vol, partie déclarante, voyageurs, documents, totaux.</summary>
public class PaxlstParserTests
{
    [Fact]
    public void Parse_ReadsFlightDetails()
    {
        var message = Assert.Single(PaxlstParser.Parse(ExchangeSeed.SamplePaxlst));

        Assert.Equal("AF100", message.Flight.Id);
        Assert.Equal("AF", message.Flight.CarrierCode);
        Assert.Equal("Air France", message.Flight.CarrierName);
        Assert.Equal("ORY", message.Flight.DeparturePort);
        Assert.Equal("FIH", message.Flight.ArrivalPort);
        Assert.Equal(new DateTime(2026, 10, 15, 14, 30, 0), message.Flight.DepartureDate);
        Assert.Equal(new DateTime(2026, 10, 15, 22, 30, 0), message.Flight.ArrivalDate);
        Assert.Equal("FIH", message.Flight.Port);
    }

    [Fact]
    public void Parse_ReadsReportingPartyAndContact()
    {
        var message = Assert.Single(PaxlstParser.Parse(ExchangeSeed.SamplePaxlst));

        Assert.NotNull(message.ReportingParty);
        Assert.Equal("KINSHASA AIRPORT", message.ReportingParty!.Name);
        Assert.Equal("243810000000", message.ReportingParty.ContactNumber);
    }

    [Fact]
    public void Parse_SplitsPassengersAndCrew()
    {
        var message = Assert.Single(PaxlstParser.Parse(ExchangeSeed.SamplePaxlst));

        Assert.Equal(2, message.Travellers.Count);
        var passenger = Assert.Single(message.Passengers);
        var crew = Assert.Single(message.Crew);

        Assert.Equal("D'ARC", passenger.Surname);
        Assert.Equal("JEAN", passenger.FirstName);
        Assert.Equal("PAUL", passenger.MiddleName);
        Assert.Equal(PaxlstTravellerType.Crew, crew.Type);
        Assert.Equal("PILOTE", crew.Surname);
    }

    [Fact]
    public void Parse_ReadsTravelDocument()
    {
        var passenger = Assert.Single(Assert.Single(PaxlstParser.Parse(ExchangeSeed.SamplePaxlst)).Passengers);

        Assert.Equal("P", passenger.DocumentType);
        Assert.Equal("OP123456", passenger.DocumentNumber);
        Assert.Equal("CD", passenger.CountryOfIssue);
        Assert.Equal(new DateTime(2024, 1, 10), passenger.IssueDate);
        Assert.Equal(new DateTime(2030, 1, 9), passenger.ExpiryDate);
        Assert.Equal("CD", passenger.Nationality);
        Assert.Equal("M", passenger.Sex);
        Assert.Equal("REF001", passenger.BookingReference);
    }

    [Fact]
    public void Parse_LegacyBirthQualifierAndSexCodes_AreAccepted()
    {
        var crew = Assert.Single(Assert.Single(PaxlstParser.Parse(ExchangeSeed.SamplePaxlst)).Crew);

        // DTM+174 (qualificatif date de naissance hérité) et ATT+2 « 1 » → M.
        Assert.Equal(new DateTime(1980, 1, 1), crew.DateOfBirth);
        Assert.Equal("M", crew.Sex);
    }

    [Fact]
    public void Parse_ReadsControlTotals()
    {
        var message = Assert.Single(PaxlstParser.Parse(ExchangeSeed.SamplePaxlst));

        Assert.Contains(message.ControlTotals, t => t.Qualifier == PaxlstControlTotal.Passengers && t.Value == 1);
        Assert.Contains(message.ControlTotals, t => t.Qualifier == PaxlstControlTotal.Crew && t.Value == 1);
    }

    [Theory]
    [InlineData("F", "F")]
    [InlineData("f", "F")]
    [InlineData("2", "F")]
    [InlineData("Female", "F")]
    [InlineData("1", "M")]
    [InlineData("Male", "M")]
    [InlineData("X", "U")]
    [InlineData(null, "U")]
    public void NormalizeSex_MapsCodes(string? value, string expected)
        => Assert.Equal(expected, PaxlstParser.NormalizeSex(value));

    [Theory]
    [InlineData("20261015", 2026, 10, 15)]
    [InlineData("261015", 2026, 10, 15)]
    [InlineData("611015", 1961, 10, 15)] // fenêtre : ≥ 60 → siècle précédent
    [InlineData("19900520", 1990, 5, 20)]
    public void ParseCompact_ReadsDateFormats(string value, int year, int month, int day)
    {
        var parsed = EdifactDate.ParseCompact(value);
        Assert.Equal(new DateTime(year, month, day), parsed);
    }

    [Fact]
    public void ParseCompact_RejectsInvalid()
    {
        Assert.Null(EdifactDate.ParseCompact("20261301")); // mois 13
        Assert.Null(EdifactDate.ParseCompact("abc"));
    }
}

/// <summary>Génération des messages PAXLST (enveloppes, compteurs, libérations, aller-retour).</summary>
public class PaxlstGeneratorTests
{
    [Fact]
    public void Generate_ProducesUnaAndEnvelopes()
    {
        var options = new PaxlstGeneratorOptions
        {
            ControlReference = "CTRL1",
            PreparedAt = new DateTime(2026, 10, 15, 14, 30, 0),
        };

        var content = PaxlstGenerator.Generate(ExchangeSeed.Model(), options);

        Assert.StartsWith("UNA:+.? '", content);
        Assert.Contains("UNB+UNOA:1+BORDERNEXT+BORDER+261015:1430+CTRL1'", content);
        Assert.Contains("UNH+CTRL11+PAXLST:D:05B:UN:IATA'", content);
        Assert.EndsWith("UNZ+1+CTRL1'", content);
    }

    [Fact]
    public void Generate_WithoutUna_StartsAtUnb()
    {
        var content = PaxlstGenerator.Generate(ExchangeSeed.Model(), new PaxlstGeneratorOptions
        {
            WriteUna = false,
            ControlReference = "C1",
        });

        Assert.StartsWith("UNB+", content);
        Assert.DoesNotContain("UNA", content);
    }

    [Fact]
    public void Generate_UntCountMatchesSegments()
    {
        var content = PaxlstGenerator.Generate(ExchangeSeed.Model(), new PaxlstGeneratorOptions
        {
            ControlReference = "C1",
            PreparedAt = new DateTime(2026, 10, 15, 14, 30, 0),
        });

        // La relecture vérifie le compteur UNT (incohérence → exception).
        var message = Assert.Single(PaxlstParser.Parse(content));
        Assert.Equal("AF100", message.Flight.Id);
    }

    [Fact]
    public void Generate_EscapesSeparatorsInValues()
    {
        var content = PaxlstGenerator.Generate(ExchangeSeed.Model(passenger: "D'ARC"),
            new PaxlstGeneratorOptions { ControlReference = "C1" });

        Assert.Contains("D?'ARC", content);
    }

    [Fact]
    public void Generate_ComputesControlTotals()
    {
        var content = PaxlstGenerator.Generate(ExchangeSeed.Model(), new PaxlstGeneratorOptions { ControlReference = "C1" });

        Assert.Contains("CNT+41:1'", content);
        Assert.Contains("CNT+42:1'", content);
    }

    [Fact]
    public void Generate_EmitsStandardDateAndNationalityComposites()
    {
        var content = PaxlstGenerator.Generate(ExchangeSeed.Model(), new PaxlstGeneratorOptions { ControlReference = "C1" });

        // DTM et NAT sous forme composite standard (les émetteurs aériens utilisent aussi
        // la forme à éléments séparés, acceptée à la lecture).
        Assert.Contains("DTM+329:19900520:102'", content);
        Assert.Contains("NAT+2:CD'", content);
        Assert.Contains("DTM+189:202610151430:203'", content);
    }

    [Fact]
    public void Generate_RoundTripsThroughParser()
    {
        var source = ExchangeSeed.Model();
        var content = PaxlstGenerator.Generate(source, new PaxlstGeneratorOptions
        {
            ControlReference = "RT1",
            PreparedAt = new DateTime(2026, 10, 15, 14, 30, 0),
        });

        var parsed = Assert.Single(PaxlstParser.Parse(content));

        Assert.Equal(source.Flight.Id, parsed.Flight.Id);
        Assert.Equal(source.Flight.CarrierName, parsed.Flight.CarrierName);
        Assert.Equal(source.Flight.DeparturePort, parsed.Flight.DeparturePort);
        Assert.Equal(source.Flight.DepartureDate, parsed.Flight.DepartureDate);
        Assert.Equal(source.ReportingParty!.Name, parsed.ReportingParty!.Name);
        Assert.Equal(source.Travellers.Count, parsed.Travellers.Count);

        var expected = source.Travellers[0];
        var actual = parsed.Passengers.Single();
        Assert.Equal(expected.Surname, actual.Surname);
        Assert.Equal(expected.FirstName, actual.FirstName);
        Assert.Equal(expected.MiddleName, actual.MiddleName);
        Assert.Equal(expected.DateOfBirth, actual.DateOfBirth);
        Assert.Equal(expected.Sex, actual.Sex);
        Assert.Equal(expected.Nationality, actual.Nationality);
        Assert.Equal(expected.DocumentNumber, actual.DocumentNumber);
        Assert.Equal(expected.CountryOfIssue, actual.CountryOfIssue);
        Assert.Equal(expected.IssueDate, actual.IssueDate);
        Assert.Equal(expected.ExpiryDate, actual.ExpiryDate);
        Assert.Equal(expected.BookingReference, actual.BookingReference);
    }

    [Fact]
    public void Generate_EmptyMessageList_IsRejected()
        => Assert.Throws<ArgumentException>(() => PaxlstGenerator.Generate(Array.Empty<PaxlstMessage>()));
}

/// <summary>Sortie XML d'un message PAXLST (forme héritée du XmlWriter EDIFACT).</summary>
public class PaxlstXmlWriterTests
{
    [Fact]
    public void ToXml_ContainsLegacyElementShape()
    {
        var xml = PaxlstXmlWriter.ToXml(ExchangeSeed.Model());

        Assert.Contains("<ReportingParty Name=", xml);
        Assert.Contains("CarrierName=\"Air France\"", xml);
        Assert.Contains("Port=\"FIH\"", xml);
        Assert.Contains("Date=\"15.10.2026\"", xml);
        Assert.Contains("<Traveller Type=\"PAX\">", xml);
        Assert.Contains("<Traveller Type=\"CREW\">", xml);
        Assert.Contains("<LastName>D'ARC</LastName>", xml);
        Assert.Contains("<DateOfBirth>20.05.1990</DateOfBirth>", xml);
        Assert.Contains("<PassportNumber>OP123456</PassportNumber>", xml);
        Assert.Contains("<DateOfExpiry>09.01.2030</DateOfExpiry>", xml);
        Assert.Contains("<ControlTotal Type=\"41\" Value=\"1\"", xml);
    }
}

/// <summary>Import des messages APIS, conservation des fichiers et rapprochement.</summary>
public class ApisMessageServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly ApisMessageService _service;

    public ApisMessageServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"ex-{Guid.NewGuid()}").Options);
        ExchangeSeed.SeedReferentials(_db);

        var audit = new AuditWriter(_db, _currentUser, _clock);
        var journal = new ExchangeJournal(_db, _currentUser, _clock, audit);
        var matcher = new StopListMatcher(_db, _clock);
        var doubtful = new DoubtablePersonService(_db, _currentUser, _clock, audit);
        _service = new ApisMessageService(_db, _currentUser, _clock, audit, journal,
            new ExchangeFileStore(_db, _clock), matcher, doubtful);

        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3, BorderPointId = 2 }, "IMMIG_OFF");
        _currentUser.SetPcNumber("A01");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Import_CreatesMessagePassengersAndFile()
    {
        var result = await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        Assert.Equal("000000001", result.ControlReference);
        Assert.Equal(1, result.MessageCount);
        Assert.Equal(1, result.PassengerCount);
        Assert.Equal(1, result.CrewCount);
        Assert.Empty(result.Warnings);

        var message = await _db.ApisMessages.Include(m => m.Passengers).Include(m => m.Files).SingleAsync();
        Assert.Equal("AF100", message.FlightNumber);
        Assert.Equal("ORY", message.DepartureLocation);
        Assert.Equal("FIH", message.ArrivalLocation);
        Assert.Equal("Air France", message.CarrierName);
        Assert.Equal("EDIFACT", message.SourceFormat);
        Assert.Equal((short)2, message.BorderPointId);
        Assert.Equal(ApisMessageStatus.Received, message.Status);
        Assert.Equal(1, message.PassengerCount); // passagers seuls
        Assert.Equal(2, message.Passengers.Count);

        var passenger = message.Passengers.Single(p => p.IsPassenger);
        Assert.Equal("D'ARC", passenger.Surname);
        Assert.Equal("OP123456", passenger.DocumentNumber);
        Assert.Equal("CD", passenger.Citizenship);
        Assert.Equal("M", passenger.Gender);
        Assert.Equal(ApisPassengerStatus.Received, passenger.Status);

        var crew = message.Passengers.Single(p => !p.IsPassenger);
        Assert.Equal("PILOTE", crew.Surname);

        var file = Assert.Single(message.Files);
        Assert.Equal("af100.paxlst", file.FileName);
        Assert.True(File.Exists(file.FilePath));
        Assert.Equal(64, file.ContentHash!.Length); // SHA-256 hexadécimal
    }

    [Fact]
    public async Task Import_JournalRecordsRun()
    {
        await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        var run = await _db.ExchangeRuns.SingleAsync(r => r.Channel == ExchangeChannels.Edifact
            && r.Direction == ExchangeDirections.Import);
        Assert.Equal(ExchangeRunStatus.Success, run.Status);
        Assert.Equal("000000001", run.Reference);
        Assert.Equal(2, run.ItemCount);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task Import_DuplicateContent_RaisesWarning()
    {
        await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");
        var second = await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100-copie.paxlst");

        Assert.Single(second.Warnings);
        var run = await _db.ExchangeRuns.Where(r => r.Direction == ExchangeDirections.Import)
            .OrderByDescending(r => r.Id).FirstAsync();
        Assert.Equal(ExchangeRunStatus.Partial, run.Status);
        Assert.Equal(1, run.ErrorCount);
    }

    [Fact]
    public async Task Import_InvalidEnvelope_FailsAndJournals()
    {
        await Assert.ThrowsAsync<EdifactParseException>(
            () => _service.ImportPaxlstAsync("UNB+UNOA:1+S+R+261015:1430+C1'", "bad.edi"));

        var run = await _db.ExchangeRuns.SingleAsync();
        Assert.Equal(ExchangeRunStatus.Failed, run.Status);
        Assert.True(run.ErrorCount > 0);
    }

    [Fact]
    public async Task Import_NonPaxlstMessage_IsJournaledAsError()
    {
        var content = ExchangeSeed.SamplePaxlst.Replace("UNH+1+PAXLST:D:05B:UN:IATA'", "UNH+1+CUSCAR:D:96A:UN'");
        var result = await _service.ImportPaxlstAsync(content, "car.edi");

        Assert.Empty(result.MessageIds);
        var run = await _db.ExchangeRuns.SingleAsync(r => r.Direction == ExchangeDirections.Import);
        Assert.Equal(ExchangeRunStatus.Partial, run.Status);
        Assert.Contains(_db.ExchangeErrors, e => e.Message!.Contains("CUSCAR"));
    }

    [Fact]
    public async Task Reconcile_MatchesLocalDocumentAndMovement()
    {
        var documentService = new DocumentService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        var documentId = await documentService.CreateAsync(DossierSeed.CompleteDocument("OP123456"));
        _db.Movements.Add(new Movement
        {
            PersonId = _db.TravelDocuments.Single(d => d.Id == documentId).PersonId,
            DocumentId = documentId,
            TravelDate = new DateTime(2026, 10, 15, 22, 45, 0),
            DirectionLookupId = 1,
            BorderPointId = 2,
        });
        await _db.SaveChangesAsync();

        var import = await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");
        var passenger = await _db.ApisPassengers.SingleAsync(p => p.IsPassenger);

        Assert.Equal(1, import.Matched);
        Assert.Equal(documentId, passenger.MatchedDocumentId);
        Assert.Equal(documentId.ToString(), passenger.PassportRegistrationId);
        Assert.NotNull(passenger.MatchedMovementId);
        Assert.Equal(ApisPassengerStatus.Reconciled, passenger.Status);
    }

    [Fact]
    public async Task Reconcile_WithoutLocalDocument_StaysReceived()
    {
        var result = await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        Assert.Equal(0, result.Matched);
        Assert.Equal(1, result.Unmatched);
        var passenger = await _db.ApisPassengers.SingleAsync(p => p.IsPassenger);
        Assert.Null(passenger.MatchedDocumentId);
        Assert.Equal(ApisPassengerStatus.Received, passenger.Status);
    }

    [Fact]
    public async Task Reconcile_StopListHit_RegistersDoubtfulAndAlerts()
    {
        _db.PersonsStopList.Add(WatchlistSeed.Person(surname: "D'ARC", givenName: "JEAN",
            dob: new DateTime(1990, 5, 20)));
        await _db.SaveChangesAsync();

        var result = await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        Assert.Equal(1, result.Doubtful);
        var passenger = await _db.ApisPassengers.SingleAsync(p => p.IsPassenger);
        Assert.Equal(ApisPassengerStatus.Doubtful, passenger.Status);
        Assert.Equal("FDP", passenger.CheckingReason);
        Assert.Equal(ApisMessageStatus.Alerted, (await _db.ApisMessages.SingleAsync()).Status);

        var doubtful = Assert.Single(_db.ApisDoubtfulPersons);
        Assert.Equal(passenger.Id, doubtful.ApisPassengerId);
        Assert.Equal(0, doubtful.ActionStatus);
        Assert.Contains("critère", doubtful.SupervisorsNote);
    }

    [Fact]
    public async Task Reconcile_StopListHit_RegistersEvenWhenAutoCheckDisabled()
    {
        // Réglage « CheckInDoubtablePersons » à 0 (valeur du déploiement) : le
        // rapprochement APIS inscrit tout de même les correspondances de liste.
        _db.PersonsStopList.Add(WatchlistSeed.Person(surname: "D'ARC", givenName: "JEAN",
            dob: new DateTime(1990, 5, 20)));
        await _db.SaveChangesAsync();

        await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        Assert.Single(_db.ApisDoubtfulPersons);
    }

    [Fact]
    public async Task Reconcile_PriorAdverseAction_UsesSscReasonOnlyWhenAutoCheckEnabled()
    {
        // Décision antérieure (Refuse) sur la même personne : motif SSC.
        var movementId = await SeedMovementForDoubtAsync(adverse: true);

        var result = await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");
        Assert.Equal(0, result.Doubtful);
        Assert.Empty(_db.ApisDoubtfulPersons);

        var passenger = await _db.ApisPassengers.SingleAsync(p => p.IsPassenger);
        Assert.Equal("SSC", passenger.CheckingReason);

        // Réglage activé : inscription automatique en seconde inspection.
        _db.Settings.Single(s => s.Type == "CheckInDoubtablePersons").Value = 1;
        await _db.SaveChangesAsync();
        _db.ApisDoubtfulPersons.RemoveRange(_db.ApisDoubtfulPersons);
        await _db.SaveChangesAsync();

        var messageId = await _db.ApisMessages.Select(m => m.Id).FirstAsync();
        var second = await _service.ReconcileMessageAsync(messageId);

        Assert.Equal(1, second.Doubtful);
        Assert.Contains(_db.ApisDoubtfulPersons, d => d.CheckingReason == "SSC");
        Assert.True(movementId > 0);
    }

    private async Task<int> SeedMovementForDoubtAsync(bool adverse)
    {
        var actionCode = adverse ? "Refuse" : "Permit";
        var actionId = _db.Lookups.Single(l => l.Code == actionCode).Id;

        // Document identique au passager APIS (nom, naissance, numéro) : la décision
        // antérieure est retrouvée par fn_CheckPersonInDoubtablePersons reconstruite.
        var documentService = new DocumentService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        var documentId = await documentService.CreateAsync(DossierSeed.CompleteDocument("OP123456")
            with { Surname = "D'Arc", GivenName = "Jean", PersonalNumber = null });
        var document = _db.TravelDocuments.Single(d => d.Id == documentId);

        var doubtful = new DoubtablePerson
        {
            MovementId = await SeedMovementAsync(document),
            Status = actionId,
            WorkflowStatus = 2,
            CreateDate = _clock.Now,
        };
        _db.DoubtablePersons.Add(doubtful);
        await _db.SaveChangesAsync();
        return doubtful.Id;
    }

    private async Task<int> SeedMovementAsync(TravelDocument document)
    {
        var movement = new Movement
        {
            PersonId = document.PersonId,
            DocumentId = document.Id,
            TravelDate = new DateTime(2026, 10, 14, 10, 0, 0),
            DirectionLookupId = 1,
            BorderPointId = 2,
        };
        _db.Movements.Add(movement);
        await _db.SaveChangesAsync();
        return movement.Id;
    }

    [Fact]
    public async Task ProcessDoubtful_SetsSupervisorAndRejectsSecondDecision()
    {
        _db.PersonsStopList.Add(WatchlistSeed.Person(surname: "D'ARC", givenName: "JEAN",
            dob: new DateTime(1990, 5, 20)));
        await _db.SaveChangesAsync();
        await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        var doubtful = await _db.ApisDoubtfulPersons.SingleAsync();
        var refuse = _db.Lookups.Single(l => l.Code == "Refuse").Id;

        await _service.ProcessDoubtfulAsync(doubtful.Id, refuse, "Refusé : liste d'alerte.");

        var processed = await _db.ApisDoubtfulPersons.SingleAsync();
        Assert.Equal(1, processed.ActionStatus);
        Assert.Equal(refuse, processed.PerformActionId);
        Assert.Equal("jdoe", processed.Supervisor);
        Assert.Equal("A01", processed.SupervisorPcNumber);
        Assert.Equal("Refusé : liste d'alerte.", processed.SupervisorsNote);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.ProcessDoubtfulAsync(doubtful.Id, refuse, "Encore"));
        Assert.Equal("EX_Error_DoubtAlreadyProcessed", exception.ErrorKey);
    }

    [Fact]
    public async Task ProcessDoubtful_UnknownAction_IsRejected()
    {
        _db.PersonsStopList.Add(WatchlistSeed.Person(surname: "D'ARC", givenName: "JEAN",
            dob: new DateTime(1990, 5, 20)));
        await _db.SaveChangesAsync();
        await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        var doubtful = await _db.ApisDoubtfulPersons.SingleAsync();
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.ProcessDoubtfulAsync(doubtful.Id, 999999, null));
        Assert.Equal("EX_Error_InvalidAction", exception.ErrorKey);
    }

    [Fact]
    public async Task Search_And_Statistics_ReflectImportedData()
    {
        _db.PersonsStopList.Add(WatchlistSeed.Person(surname: "D'ARC", givenName: "JEAN",
            dob: new DateTime(1990, 5, 20)));
        await _db.SaveChangesAsync();
        await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        var messages = await _service.SearchMessagesAsync(flightNumber: "AF1",
            status: ApisMessageStatus.Alerted);
        Assert.Single(messages);

        var byDocument = await _service.SearchPassengersAsync("op123456");
        Assert.Single(byDocument);

        var doubtful = await _service.GetDoubtfulAsync();
        Assert.Single(doubtful);
        Assert.NotNull(doubtful[0].ApisPassenger);

        var statistics = await _service.GetStatisticsAsync();
        Assert.Equal(1, statistics.MessageCount);
        Assert.Equal(1, statistics.PassengerTotal);
        Assert.Equal(1, statistics.CrewTotal);
        Assert.Equal(1, statistics.DoubtfulTotal);
        Assert.Equal(1, statistics.PendingDoubtfulTotal);
        Assert.Equal(0, statistics.ExportedTotal);
    }

    [Fact]
    public async Task ExportMessagesXml_WritesFileAndMarksExported()
    {
        await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        var result = await _service.ExportMessagesXmlAsync(new DateTime(2026, 1, 1), new DateTime(2027, 1, 1));

        Assert.Equal(1, result.MessageCount);
        Assert.Equal(2, result.PassengerCount);
        Assert.True(File.Exists(result.FilePath));

        var xml = await File.ReadAllTextAsync(result.FilePath);
        Assert.Contains("<ApisMessage", xml);
        Assert.Contains("FlightNumber=\"AF100\"", xml);
        Assert.Contains("<ApisPassenger", xml);
        Assert.Contains("DocumentNumber=\"OP123456\"", xml);

        Assert.Equal(ApisMessageStatus.Exported, (await _db.ApisMessages.SingleAsync()).Status);

        var run = await _db.ExchangeRuns.SingleAsync(r => r.Direction == ExchangeDirections.Export);
        Assert.Equal(ExchangeChannels.Apis, run.Channel);
        Assert.Equal(ExchangeRunStatus.Success, run.Status);
    }

    [Fact]
    public async Task ExportMessagesXml_BorderPointLocationFilters()
    {
        await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");

        var other = await _service.ExportMessagesXmlAsync(new DateTime(2026, 1, 1), new DateTime(2027, 1, 1),
            borderPointLocation: "SSG");
        Assert.Equal(0, other.MessageCount);

        var matching = await _service.ExportMessagesXmlAsync(new DateTime(2026, 1, 1), new DateTime(2027, 1, 1),
            borderPointLocation: "FIH");
        Assert.Equal(1, matching.MessageCount);
    }

    [Fact]
    public async Task ExportMessagesPaxlst_RoundTripsStoredMessage()
    {
        await _service.ImportPaxlstAsync(ExchangeSeed.SamplePaxlst, "af100.paxlst");
        var messageId = await _db.ApisMessages.Select(m => m.Id).SingleAsync();

        var result = await _service.ExportMessagesPaxlstAsync(new[] { messageId });
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(1, result.MessageCount);
        Assert.Equal(2, result.TravellerCount);
        Assert.False(string.IsNullOrEmpty(result.ControlReference));

        var reparsed = Assert.Single(PaxlstParser.Parse(await File.ReadAllTextAsync(result.FilePath)));
        Assert.Equal("AF100", reparsed.Flight.Id);
        Assert.Equal("D'ARC", reparsed.Passengers.Single().Surname);
        Assert.Equal("PILOTE", reparsed.Crew.Single().Surname);
    }

    [Fact]
    public async Task ExportMessagesPaxlst_WithoutSelection_IsRejected()
    {
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.ExportMessagesPaxlstAsync(Array.Empty<int>()));
        Assert.Equal("EX_Error_NoMessageSelected", exception.ErrorKey);
    }

    [Fact]
    public async Task GeneratePaxlstFromMovements_GroupsByFlightAndBuildsTravellers()
    {
        var documentService = new DocumentService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        var documentId = await documentService.CreateAsync(DossierSeed.CompleteDocument("OP555555"));
        var document = _db.TravelDocuments.Single(d => d.Id == documentId);
        var movementId = await SeedMovementAsync(document);

        // Voyage + passage : le numéro externe du voyage devient le vol du message.
        var tripService = new TripService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        var crossingId = await tripService.RegisterAsync(BorderOpsSeed.Arrival("AF777"));
        var movement = _db.Movements.Single(m => m.Id == movementId);
        movement.TripCrossingId = crossingId;
        await _db.SaveChangesAsync();

        var result = await _service.GeneratePaxlstFromMovementsAsync(
            new DateTime(2026, 10, 1), new DateTime(2026, 11, 1), borderPointCode: null);

        Assert.Equal(1, result.MessageCount);
        Assert.Equal(1, result.TravellerCount);

        var message = Assert.Single(PaxlstParser.Parse(await File.ReadAllTextAsync(result.FilePath)));
        Assert.Equal("AF777", message.Flight.Id);
        Assert.Equal("ORY", message.Flight.DeparturePort);
        Assert.Equal("FIH", message.Flight.ArrivalPort);

        var traveller = Assert.Single(message.Travellers);
        Assert.Equal("MBALA", traveller.Surname);
        Assert.Equal("OP555555", traveller.DocumentNumber);
        Assert.Equal("CD", traveller.Nationality);
        Assert.Equal("M", traveller.Sex); // lookup SEX « M » du document
    }
}
