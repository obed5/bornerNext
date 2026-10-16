using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Référentiels et données communes aux tests du dossier frontalier (Lot 2).</summary>
internal static class DossierSeed
{
    public static void SeedReferentials(BorderNextDbContext db)
    {
        db.LookupTypes.Add(new LookupType
        {
            Id = 1, Code = "MOVEMENT_DIRECTION", NameFr = "Sens du mouvement", NameEn = "Movement direction",
            Items =
            {
                new Lookup { Id = 1, Code = "Entry", Cultures = { Fr("Arrivée"), En("Entry") } },
                new Lookup { Id = 2, Code = "Exit", Cultures = { Fr("Départ"), En("Exit") } }
            }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 2, Code = "DOCUMENT_TYPE", NameFr = "Types de documents", NameEn = "Document types",
            Items = { new Lookup { Id = 3, Code = "OP", Cultures = { Fr("Passeport ordinaire"), En("Ordinary Passport") } } }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 3, Code = "SEX", NameFr = "Sexe", NameEn = "Sex",
            Items =
            {
                new Lookup { Id = 4, Code = "F", Cultures = { Fr("Féminin"), En("Female") } },
                new Lookup { Id = 5, Code = "M", Cultures = { Fr("Masculin"), En("Male") } }
            }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 4, Code = "TRAVEL_REASON", NameFr = "Motifs de voyage", NameEn = "Travel reasons",
            Items = { new Lookup { Id = 6, Code = "Tourist", Cultures = { Fr("Tourisme"), En("Tourist") } } }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 5, Code = "TRANSPORT_TYPE", NameFr = "Modes de transport", NameEn = "Transport types",
            Items = { new Lookup { Id = 7, Code = "Flight", Cultures = { Fr("Vol"), En("Flight") } } }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 6, Code = "DEPORTATION_REASON", NameFr = "Motifs de déportation", NameEn = "Deportation reasons",
            Items = { new Lookup { Id = 8, Code = "NoVisa", Cultures = { Fr("Absence de visa"), En("Visa absence") } } }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 7, Code = "ACCOMODATION_TYPE", NameFr = "Types d'hébergement", NameEn = "Accommodation types",
            Items = { new Lookup { Id = 9, Code = "Hotel", Cultures = { Fr("Hôtel"), En("Hotel") } } }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 8, Code = "VISA_TYPE", NameFr = "Types de visa", NameEn = "Visa types",
            Items = { new Lookup { Id = 10, Code = "TouristVisa", Cultures = { Fr("Visa touristique"), En("Tourist visa") } } }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 9, Code = "VISA_ENTRY", NameFr = "Entrées de visa", NameEn = "Visa entries",
            Items = { new Lookup { Id = 11, Code = "Single", Cultures = { Fr("Entrée unique"), En("Single entry") } } }
        });

        db.Countries.Add(new Country { Id = 1, Alpha2 = "CD", Cultures = { new CountryCulture { Culture = "fr", Name = "CONGO, RDC" } } });
        db.Countries.Add(new Country { Id = 2, Alpha2 = "FR", Cultures = { new CountryCulture { Culture = "fr", Name = "FRANCE" } } });
        db.BorderPoints.Add(new BorderPoint { Id = 2, ExternalId = "FIH", Cultures = { new BorderPointCulture { Culture = "fr", Name = "FIH" } } });
        db.SaveChanges();
    }

    private static LookupCulture Fr(string value) => new() { Culture = "fr", Description = value };
    private static LookupCulture En(string value) => new() { Culture = "en", Description = value };

    public static DocumentSaveRequest CompleteDocument(string number = "OP123456") => new(
        number, DocumentTypeId: 3, CountryOfIssueId: 1, IssueDate: new DateTime(2024, 1, 10),
        ExpireDate: new DateTime(2030, 1, 9), Surname: "Mbala", GivenName: "Jean", FatherName: "Pierre",
        DateOfBirth: new DateTime(1990, 5, 20), SexId: 5, CitizenshipId: 1, PersonalNumber: "PN-001");
}

/// <summary>Tests de la recherche et de la gestion des documents (Lot 2).</summary>
public class DocumentServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly DocumentService _service;

    public DocumentServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"doc-{Guid.NewGuid()}").Options);
        var clock = new SystemClock();
        _service = new DocumentService(_db, _currentUser, clock, new AuditWriter(_db, _currentUser, clock));
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3, BorderPointId = 2 }, "IMMIG_OFF");
        _currentUser.SetPcNumber("A01");
        DossierSeed.SeedReferentials(_db);
    }

    [Fact]
    public async Task Search_WithoutCriteria_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.SearchAsync());
        Assert.Equal("PAL_Error_SearchCriteriaRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task Create_CreatesPersonAndDocument_AndAudits()
    {
        var id = await _service.CreateAsync(DossierSeed.CompleteDocument());

        var document = await _db.TravelDocuments.SingleAsync(d => d.Id == id);
        Assert.Equal("OP123456", document.DocumentNumber);
        Assert.Equal("MBALA", document.Surname); // normalisation upper héritée
        Assert.NotEqual(0, document.PersonId);
        Assert.Equal((short)2, document.BorderPointId);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(TravelDocument));
    }

    [Fact]
    public async Task Create_WithoutNumber_IsRejected()
    {
        var request = DossierSeed.CompleteDocument() with { DocumentNumber = "  " };
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.CreateAsync(request));
        Assert.Equal("PAL_Error_DocumentNumberRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task FindOrCreate_ExistingDocument_ReusesPerson()
    {
        var first = await _service.CreateAsync(DossierSeed.CompleteDocument());
        var personId = (await _db.TravelDocuments.SingleAsync(d => d.Id == first)).PersonId;

        var again = await _service.FindOrCreateAsync(DossierSeed.CompleteDocument());
        Assert.Equal(first, again);

        var second = await _service.FindOrCreateAsync(DossierSeed.CompleteDocument("OP999999"));
        Assert.NotEqual(first, second);
        Assert.NotEqual(personId, (await _db.TravelDocuments.SingleAsync(d => d.Id == second)).PersonId);
    }

    [Fact]
    public async Task Search_ByNumberNameDateAndCitizenship()
    {
        await _service.CreateAsync(DossierSeed.CompleteDocument());
        await _service.CreateAsync(DossierSeed.CompleteDocument("OP777777") with { Surname = "Kabila", GivenName = "Alice", DateOfBirth = new DateTime(1985, 3, 2) });

        Assert.Single(await _service.SearchAsync(number: "OP123"));
        Assert.Single(await _service.SearchAsync(surname: "Kabila"));
        Assert.Single(await _service.SearchAsync(givenName: "Alice"));
        Assert.Single(await _service.SearchAsync(dateOfBirth: new DateTime(1985, 3, 2)));
        Assert.Equal(2, (await _service.SearchAsync(citizenshipId: 1)).Count);
        Assert.Empty(await _service.SearchAsync(number: "ZZZ"));
    }

    [Fact]
    public async Task Search_ReturnsLastMovementInfo()
    {
        var documentId = await _service.CreateAsync(DossierSeed.CompleteDocument());
        var movement = new MovementService(_db, _currentUser, new SystemClock(), new AuditWriter(_db, _currentUser, new SystemClock()));
        await movement.RegisterAsync(NewMovement(documentId));

        var item = Assert.Single(await _service.SearchAsync(number: "OP123456"));
        Assert.NotNull(item.LastMovementDate);
        Assert.True(item.LastWasEntry);
    }

    [Fact]
    public async Task Get_ReturnsFullDetail()
    {
        var documentId = await _service.CreateAsync(DossierSeed.CompleteDocument());

        var detail = await _service.GetAsync(documentId);

        Assert.NotNull(detail);
        Assert.Equal("Passeport ordinaire", detail!.DocumentTypeFr);
        Assert.Equal("CONGO, RDC", detail.CitizenshipFr);
        Assert.False(detail.HasPhoto);
    }

    [Fact]
    public async Task Update_ChangesFields_AndRejectsDuplicate()
    {
        var documentId = await _service.CreateAsync(DossierSeed.CompleteDocument());
        var other = await _service.CreateAsync(DossierSeed.CompleteDocument("OP555555"));

        await _service.UpdateAsync(documentId, DossierSeed.CompleteDocument() with { Surname = "Nouveau" });
        Assert.Equal("NOUVEAU", (await _db.TravelDocuments.SingleAsync(d => d.Id == documentId)).Surname);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.UpdateAsync(other, DossierSeed.CompleteDocument()));
        Assert.Equal("PAL_Error_DocumentDuplicate", ex.ErrorKey);
    }

    [Fact]
    public async Task Images_PhotoAndScanRoundTrip()
    {
        var documentId = await _service.CreateAsync(DossierSeed.CompleteDocument());

        await _service.SetPhotoAsync(documentId, new byte[] { 1, 2, 3 });
        await _service.SetScannedDocumentAsync(documentId, new byte[] { 9, 8 });

        var (photo, scan) = await _service.GetImagesAsync(documentId);
        Assert.Equal(new byte[] { 1, 2, 3 }, photo);
        Assert.Equal(new byte[] { 9, 8 }, scan);

        var detail = await _service.GetAsync(documentId);
        Assert.True(detail!.HasPhoto);
        Assert.True(detail.HasScannedDocument);
    }

    [Fact]
    public async Task Update_UnknownDocument_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.UpdateAsync(999, DossierSeed.CompleteDocument()));
        Assert.Equal("PAL_Error_DocumentNotFound", ex.ErrorKey);
    }

    private static MovementRequest NewMovement(int documentId) => new(documentId, MovementService.EntryCode,
        DateTime.Today, TransportTypeId: 7, IsDriver: false, TravelReasonId: 6, Granted: true,
        DeportationReasonId: null, IsTransit: false, HasVisa: false, VisaId: null, ChildrenCount: 0,
        Note: null, ResidentCountryId: 1, DestinationCountryId: 1, Occupation: "Commerçant",
        AccommodationAddress: "Av. du Port 12", AccommodationTypeId: 9, DurationOfStay: 15);

    public void Dispose() => _db.Dispose();
}

/// <summary>Tests de l'enregistrement des mouvements (Lot 2).</summary>
public class MovementServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly MovementService _service;
    private readonly DocumentService _documents;
    private readonly VisaService _visas;

    public MovementServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"mov-{Guid.NewGuid()}").Options);
        var clock = new SystemClock();
        var audit = new AuditWriter(_db, _currentUser, clock);
        _service = new MovementService(_db, _currentUser, clock, audit);
        _documents = new DocumentService(_db, _currentUser, clock, audit);
        _visas = new VisaService(_db, _currentUser, clock, audit);
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3, BorderPointId = 2 }, "IMMIG_OFF");
        _currentUser.SetPcNumber("A01");
        DossierSeed.SeedReferentials(_db);
    }

    private async Task<int> NewDocumentAsync(string number = "OP123456")
        => await _documents.CreateAsync(DossierSeed.CompleteDocument(number));

    private static MovementRequest Request(int documentId, string direction = MovementService.EntryCode,
        DateTime? travelDate = null) => new(documentId, direction, travelDate ?? DateTime.Today,
        TransportTypeId: 7, IsDriver: false, TravelReasonId: 6, Granted: true, DeportationReasonId: null,
        IsTransit: false, HasVisa: false, VisaId: null, ChildrenCount: 0, Note: null,
        ResidentCountryId: 1, DestinationCountryId: 1, Occupation: "Commerçant",
        AccommodationAddress: "Av. du Port 12", AccommodationTypeId: 9, DurationOfStay: 15);

    [Fact]
    public async Task Register_Entry_StoresMovementWithInspectorAndPc()
    {
        var documentId = await NewDocumentAsync();

        var movementId = await _service.RegisterAsync(Request(documentId));

        var movement = await _db.Movements.SingleAsync(m => m.Id == movementId);
        Assert.Equal(1, movement.DirectionLookupId);
        Assert.Equal(documentId, movement.DocumentId);
        Assert.Equal(_db.TravelDocuments.Single(d => d.Id == documentId).PersonId, movement.PersonId);
        Assert.Equal("jdoe", movement.Inspector);
        Assert.Equal("A01", movement.PcNumber);
        Assert.Equal((short)2, movement.BorderPointId);
        Assert.True(movement.Granted);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(Movement));
    }

    [Fact]
    public async Task Register_IncompleteDocument_IsRejected_WithMissingFields()
    {
        var documentId = await _documents.CreateAsync(DossierSeed.CompleteDocument() with { SexId = null, CitizenshipId = null });

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.RegisterAsync(Request(documentId)));
        Assert.Equal("PAL_Error_DocumentIncomplete", ex.ErrorKey);
        Assert.Contains("SexId", ex.Message);
        Assert.Contains("CitizenshipId", ex.Message);
    }

    [Fact]
    public async Task Register_UnknownDirection_IsRejected()
    {
        var documentId = await NewDocumentAsync();
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(Request(documentId, "Sideways")));
        Assert.Equal("PAL_Error_DirectionRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task Register_FutureTravelDate_IsRejected()
    {
        var documentId = await NewDocumentAsync();
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(Request(documentId, travelDate: DateTime.Today.AddDays(1))));
        Assert.Equal("PAL_Error_TravelDateFuture", ex.ErrorKey);
    }

    [Fact]
    public async Task Register_ExpiredDocument_IsRejected()
    {
        var documentId = await _documents.CreateAsync(DossierSeed.CompleteDocument() with { ExpireDate = DateTime.Today.AddDays(-1) });

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.RegisterAsync(Request(documentId)));
        Assert.Equal("PAL_Error_DocumentExpired", ex.ErrorKey);
    }

    [Fact]
    public async Task Register_HonorsMinimumDocumentValiditySetting()
    {
        _db.Settings.Add(new Setting { Type = "MinimumDocumentValidityMonth", Value = 6 });
        await _db.SaveChangesAsync();
        // Document expirant dans 3 mois : sous la marge de 6 mois → refus.
        var documentId = await _documents.CreateAsync(DossierSeed.CompleteDocument() with { ExpireDate = DateTime.Today.AddMonths(3) });

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.RegisterAsync(Request(documentId)));
        Assert.Equal("PAL_Error_DocumentExpired", ex.ErrorKey);

        // Dans 8 mois : au-delà de la marge → accepté.
        var okDocumentId = await _documents.CreateAsync(DossierSeed.CompleteDocument("OP888888") with { ExpireDate = DateTime.Today.AddMonths(8) });
        var movementId = await _service.RegisterAsync(Request(okDocumentId));
        Assert.True(movementId > 0);
    }

    [Fact]
    public async Task Register_RefusedWithoutReason_IsRejected_WithReasonAccepted()
    {
        var documentId = await NewDocumentAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(Request(documentId) with { Granted = false }));
        Assert.Equal("PAL_Error_DeportationReasonRequired", ex.ErrorKey);

        var movementId = await _service.RegisterAsync(
            Request(documentId) with { Granted = false, DeportationReasonId = 8 });
        var movement = await _db.Movements.SingleAsync(m => m.Id == movementId);
        Assert.False(movement.Granted);
        Assert.Equal(8, movement.DeportationReasonId);
    }

    [Fact]
    public async Task Register_GrantedWithReason_ClearsTheReason()
    {
        var documentId = await NewDocumentAsync();

        var movementId = await _service.RegisterAsync(Request(documentId) with { Granted = true, DeportationReasonId = 8 });

        Assert.Null((await _db.Movements.SingleAsync(m => m.Id == movementId)).DeportationReasonId);
    }

    [Fact]
    public async Task Register_HasVisaWithoutValidVisa_IsRejected()
    {
        var documentId = await NewDocumentAsync();
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(Request(documentId) with { HasVisa = true }));
        Assert.Equal("PAL_Error_VisaNotFound", ex.ErrorKey);
    }

    [Fact]
    public async Task Register_HasVisa_AutoLinksValidVisa()
    {
        var documentId = await NewDocumentAsync();
        var visaId = await _visas.CreateAsync(NewVisaRequest(documentId));

        var movementId = await _service.RegisterAsync(Request(documentId) with { HasVisa = true });

        Assert.Equal(visaId, (await _db.Movements.SingleAsync(m => m.Id == movementId)).VisaId);
    }

    [Fact]
    public async Task Register_VisaFromOtherDocument_IsRejected()
    {
        var documentId = await NewDocumentAsync();
        var otherId = await NewDocumentAsync("OP222222");
        var visaId = await _visas.CreateAsync(NewVisaRequest(otherId));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(Request(documentId) with { HasVisa = true, VisaId = visaId }));
        Assert.Equal("PAL_Error_VisaMismatch", ex.ErrorKey);
    }

    [Fact]
    public async Task Register_RevokedVisa_IsNotAutoLinked()
    {
        var documentId = await NewDocumentAsync();
        var visaId = await _visas.CreateAsync(NewVisaRequest(documentId));
        await _visas.SetRevokedAsync(visaId, true);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(Request(documentId) with { HasVisa = true }));
        Assert.Equal("PAL_Error_VisaNotFound", ex.ErrorKey);
    }

    [Fact]
    public async Task Register_NegativeValues_AreRejected()
    {
        var documentId = await NewDocumentAsync();
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.RegisterAsync(Request(documentId) with { DurationOfStay = -1 }));
        Assert.Equal("PAL_Error_NegativeValue", ex.ErrorKey);
    }

    [Fact]
    public async Task History_ReturnsMostRecentFirst_AndLastDirection()
    {
        var documentId = await NewDocumentAsync();
        await _service.RegisterAsync(Request(documentId, travelDate: DateTime.Today.AddDays(-2)));
        await _service.RegisterAsync(Request(documentId, MovementService.ExitCode, DateTime.Today.AddDays(-1)));
        await _service.RegisterAsync(Request(documentId, travelDate: DateTime.Today));

        var history = await _service.GetHistoryAsync(documentId);
        Assert.Equal(3, history.Count);
        Assert.True(history[0].RegistrationTime >= history[1].RegistrationTime);
        Assert.True(history[0].IsEntry);
        Assert.False(history[1].IsEntry);
        Assert.True(await _service.GetLastDirectionIsEntryAsync(documentId));
    }

    private static VisaRequest NewVisaRequest(int documentId) => new(VisaTypeId: 10, VisaEntryId: 11,
        IssueDate: DateTime.Today.AddDays(-10), ExpireDate: DateTime.Today.AddMonths(3), Duration: 90,
        DocumentId: documentId, Number: null, ReferenceNumber: "REF-1", ReceiptNumber: null, Address: null,
        ContactNumber: null, Employer: null, InvitedBy: null, RelationId: null, UniversityId: null,
        PermitStatusId: null, Occupation: null, PeriodTypeId: null, Note: null, VoucherNumber: null,
        PermitConditionId: null, ProvinceId: null, Barcode: null);

    public void Dispose() => _db.Dispose();
}

/// <summary>Tests des visas et de leurs enfants (Lot 2).</summary>
public class VisaServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly VisaService _service;
    private readonly DocumentService _documents;

    public VisaServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"visa-{Guid.NewGuid()}").Options);
        var clock = new SystemClock();
        var audit = new AuditWriter(_db, _currentUser, clock);
        _service = new VisaService(_db, _currentUser, clock, audit);
        _documents = new DocumentService(_db, _currentUser, clock, audit);
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 5, BorderPointId = 2 }, "VISA_OFF");
        DossierSeed.SeedReferentials(_db);
    }

    private static VisaRequest Request(int? documentId, DateTime? issue = null, DateTime? expire = null) => new(
        VisaTypeId: 10, VisaEntryId: 11, IssueDate: issue ?? DateTime.Today, ExpireDate: expire ?? DateTime.Today.AddMonths(3),
        Duration: 90, DocumentId: documentId, Number: null, ReferenceNumber: null, ReceiptNumber: null,
        Address: null, ContactNumber: null, Employer: null, InvitedBy: null, RelationId: null, UniversityId: null,
        PermitStatusId: null, Occupation: null, PeriodTypeId: null, Note: null, VoucherNumber: null,
        PermitConditionId: null, ProvinceId: null, Barcode: null);

    [Fact]
    public async Task Create_GeneratesSequentialNumber()
    {
        var first = await _service.CreateAsync(Request(null));
        var second = await _service.CreateAsync(Request(null));

        var year = DateTime.Today.Year;
        Assert.Equal($"VISA-{year}-000001", (await _db.Visas.SingleAsync(v => v.Id == first)).Number);
        Assert.Equal($"VISA-{year}-000002", (await _db.Visas.SingleAsync(v => v.Id == second)).Number);
    }

    [Fact]
    public async Task Create_ExplicitNumber_IsKept_AndAudited()
    {
        var id = await _service.CreateAsync(Request(null) with { Number = "V-2026-777" });
        Assert.Equal("V-2026-777", (await _db.Visas.SingleAsync(v => v.Id == id)).Number);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(Visa));
    }

    [Fact]
    public async Task Create_InvalidVisaType_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateAsync(Request(null) with { VisaTypeId = 7 })); // 7 = TRANSPORT_TYPE
        Assert.Equal("VISA_Error_TypeRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task Create_ExpireBeforeIssue_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateAsync(Request(null, issue: new DateTime(2026, 6, 1), expire: new DateTime(2026, 1, 1))));
        Assert.Equal("VISA_Error_DatesInvalid", ex.ErrorKey);
    }

    [Fact]
    public async Task Create_WithUnknownDocument_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateAsync(Request(documentId: 999)));
        Assert.Equal("PAL_Error_DocumentNotFound", ex.ErrorKey);
    }

    [Fact]
    public async Task Create_ThenListByDocument_ShowsHolderAndValidity()
    {
        var documentId = await _documents.CreateAsync(DossierSeed.CompleteDocument());

        await _service.CreateAsync(Request(documentId));

        var visa = Assert.Single(await _service.ListByDocumentAsync(documentId));
        Assert.Equal("Visa touristique", visa.TypeFr);
        Assert.Equal("OP123456", visa.DocumentNumber);
        Assert.Equal("MBALA JEAN", visa.HolderName!.ToUpperInvariant());
        Assert.True(visa.IsValid);
    }

    [Fact]
    public async Task FindValidVisa_RespectsDatesAndRevocation()
    {
        var documentId = await _documents.CreateAsync(DossierSeed.CompleteDocument());
        var expired = await _service.CreateAsync(Request(documentId, issue: DateTime.Today.AddMonths(-6), expire: DateTime.Today.AddDays(-1)));
        var valid = await _service.CreateAsync(Request(documentId, issue: DateTime.Today.AddDays(-5), expire: DateTime.Today.AddMonths(2)));

        Assert.Equal(valid, await _service.FindValidVisaAsync(documentId, DateTime.Today));
        Assert.Equal(expired, await _service.FindValidVisaAsync(documentId, DateTime.Today.AddMonths(-3)));

        await _service.SetRevokedAsync(valid, true);
        await _service.SetRevokedAsync(expired, true);
        Assert.Null(await _service.FindValidVisaAsync(documentId, DateTime.Today));
    }

    [Fact]
    public async Task Revoke_ThenListExcludes_UnlessRequested()
    {
        var documentId = await _documents.CreateAsync(DossierSeed.CompleteDocument());
        var id = await _service.CreateAsync(Request(documentId));

        await _service.SetRevokedAsync(id, true);
        Assert.Empty(await _service.ListByDocumentAsync(documentId));
        Assert.Single(await _service.ListByDocumentAsync(documentId, includeRevoked: true));

        await _service.SetRevokedAsync(id, false);
        Assert.Single(await _service.ListByDocumentAsync(documentId));
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        var id = await _service.CreateAsync(Request(null));

        await _service.UpdateAsync(id, Request(null) with { Duration = 30, ReferenceNumber = "REF-42", Employer = "DGM" });

        var visa = await _db.Visas.SingleAsync(v => v.Id == id);
        Assert.Equal((short)30, visa.Duration);
        Assert.Equal("REF-42", visa.ReferenceNumber);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowUpdated && a.Entity == nameof(Visa));
    }

    [Fact]
    public async Task Children_AddListRemoveLogically()
    {
        var visaId = await _service.CreateAsync(Request(null));
        var childId = await _service.AddChildAsync(visaId, new VisaChildDto(0, "Mbala", "Petit", new DateTime(2018, 2, 3), 5, 1, 3, "OP-CHILD"));

        var children = await _service.GetChildrenAsync(visaId);
        var child = Assert.Single(children);
        Assert.Equal("MBALA", child.Surname);

        await _service.UpdateChildAsync(childId, new VisaChildDto(childId, "Mbala", "Petite", new DateTime(2018, 2, 3), 4, 1, 3, "OP-CHILD"));
        Assert.Equal("PETITE", (await _service.GetChildrenAsync(visaId)).Single().GivenName);

        await _service.RemoveChildAsync(childId);
        Assert.Empty(await _service.GetChildrenAsync(visaId));
        Assert.True((await _db.VisaChildren.SingleAsync(c => c.Id == childId)).IsDeleted);
    }

    public void Dispose() => _db.Dispose();
}
