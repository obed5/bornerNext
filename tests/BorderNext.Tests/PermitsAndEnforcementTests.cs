using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Référentiels et fixtures supplémentaires du Lot 4 (permis, enforcement).</summary>
internal static class EnforcementSeed
{
    public static void SeedLookups(BorderNextDbContext db)
    {
        db.LookupTypes.Add(new LookupType
        {
            Id = 10, Code = "PERMIT_TYPE", NameFr = "Types de permis", NameEn = "Permit types",
            Items =
            {
                new Lookup { Id = 20, Code = "Residence", Cultures = { Fr("Permis de résidence"), En("Residence permit") } },
                new Lookup { Id = 21, Code = "Work", Cultures = { Fr("Permis de travail"), En("Work permit") } }
            }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 11, Code = "PERMIT_STATUS", NameFr = "Statuts de permis", NameEn = "Permit statuses",
            Items = { new Lookup { Id = 22, Code = "Pending", Cultures = { Fr("En attente"), En("Pending") } } }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 12, Code = "REMOVAL_ORDER_TYPE", NameFr = "Ordres de reconduite", NameEn = "Removal order types",
            Items =
            {
                new Lookup { Id = 23, Code = "Order", Cultures = { Fr("Ordre de reconduite"), En("Removal Order") } },
                new Lookup { Id = 24, Code = "Notice", Cultures = { Fr("Avis d'inadmissibilité"), En("Notice of Inadmissible Traveller") } }
            }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 13, Code = "PENALTY_TICKET_REASON", NameFr = "Motifs d'amende", NameEn = "Penalty reasons",
            Items = { new Lookup { Id = 25, Code = "Overstayer", Cultures = { Fr("Dépassement de séjour"), En("Overstayer") } } }
        });
        db.LookupTypes.Add(new LookupType
        {
            Id = 14, Code = "REFUSAL_JUSTIFICATION", NameFr = "Justifications de refus", NameEn = "Refusal justifications",
            Items =
            {
                new Lookup { Id = 26, Code = "NoVisa", Cultures = { Fr("Absence de visa valide"), En("Has no valid visa") } },
                new Lookup { Id = 27, Code = "Other", Cultures = { Fr("Autre"), En("Other") } }
            }
        });
        db.SaveChanges();
    }

    /// <summary>Crée document + personne + mouvement (paramètre : accordé ou refusé).</summary>
    public static (int DocumentId, int PersonId, int MovementId) CreateMovement(BorderNextDbContext db,
        bool granted, string documentNumber = "OP555001", short fingerOffset = 0)
    {
        var person = new Person { BorderPointId = 2 };
        var document = new TravelDocument
        {
            Person = person, DocumentTypeId = 3, DocumentNumber = documentNumber,
            Surname = "KABEYA", GivenName = "Paul", DateOfBirth = new DateTime(1988, 7, 14),
            SexId = 5, CitizenshipId = 1, ExpireDate = DateTime.Today.AddYears(2)
        };
        var movement = new Movement
        {
            Person = person, Document = document, TravelDate = DateTime.Today, DirectionLookupId = 1,
            Granted = granted, DeportationReasonId = granted ? null : 8, RegistrationTime = DateTime.Now,
            Inspector = "jdoe", BorderPointId = 2
        };
        db.Persons.Add(person);
        db.TravelDocuments.Add(document);
        db.Movements.Add(movement);
        db.SaveChanges();
        return (document.Id, person.Id, movement.Id);
    }

    private static LookupCulture Fr(string value) => new() { Culture = "fr", Description = value };
    private static LookupCulture En(string value) => new() { Culture = "en", Description = value };
}

/// <summary>Tests du cycle de vie des permis (Lot 4).</summary>
public class PermitServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly PermitService _service;
    private readonly int _documentId;

    public PermitServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"permits-{Guid.NewGuid()}").Options);
        var clock = new SystemClock();
        _service = new PermitService(_db, _currentUser, clock, new AuditWriter(_db, _currentUser, clock));
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3, BorderPointId = 2 }, "IMMIG_OFF");
        _currentUser.SetPcNumber("A01");
        DossierSeed.SeedReferentials(_db);
        EnforcementSeed.SeedLookups(_db);
        _documentId = EnforcementSeed.CreateMovement(_db, granted: true).DocumentId;
    }

    private PermitSaveRequest Request(string? number = null) => new(number, _documentId, null, 20,
        DateTime.Today, DateTime.Today.AddYears(2), 22, null, null, null, null, null, null, "DGM", "Demande de résidence");

    [Fact]
    public async Task Create_GeneratesSequentialNumber_AndAudits()
    {
        var first = await _service.CreateAsync(Request());
        var second = await _service.CreateAsync(Request());

        var year = DateTime.Today.Year;
        Assert.Equal($"PERMIT-{year}-000001", (await _db.Permits.SingleAsync(p => p.Id == first)).Number);
        Assert.Equal($"PERMIT-{year}-000002", (await _db.Permits.SingleAsync(p => p.Id == second)).Number);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(Permit));
    }

    [Fact]
    public async Task Create_ExplicitNumber_IsKept()
    {
        var id = await _service.CreateAsync(Request("PERMIT-2026-777"));
        Assert.Equal("PERMIT-2026-777", (await _db.Permits.SingleAsync(p => p.Id == id)).Number);
    }

    [Fact]
    public async Task Create_UnknownDocument_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateAsync(Request() with { DocumentId = 999 }));
        Assert.Equal("PAL_Error_DocumentNotFound", ex.ErrorKey);
    }

    [Fact]
    public async Task Create_ExpireBeforeIssue_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateAsync(Request() with { IssueDate = new DateTime(2026, 6, 1), ExpireDate = new DateTime(2026, 1, 1) }));
        Assert.Equal("VISA_Error_DatesInvalid", ex.ErrorKey);
    }

    [Fact]
    public async Task Create_WrongPermitType_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateAsync(Request() with { PermitTypeId = 25 })); // 25 = motif d'amende
        Assert.Equal("PV_Error_TypeRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task Create_WrongStatus_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateAsync(Request() with { StatusId = 23 })); // 23 = ordre de reconduite
        Assert.Equal("PV_Error_StatusUnknown", ex.ErrorKey);
    }

    [Fact]
    public async Task Search_ByNumberDocumentAndSurname()
    {
        await _service.CreateAsync(Request("PERMIT-2026-500"));

        Assert.Single(await _service.SearchAsync(number: "2026-500"));
        Assert.Single(await _service.SearchAsync(documentNumber: "OP555001"));
        Assert.Single(await _service.SearchAsync(surname: "Kabeya"));
        Assert.Empty(await _service.SearchAsync(number: "ZZZ"));
    }

    [Fact]
    public async Task Update_ChangesFields_AndRejectsDuplicateNumber()
    {
        var first = await _service.CreateAsync(Request("PERMIT-A"));
        await _service.CreateAsync(Request("PERMIT-B"));

        await _service.UpdateAsync(first, Request("PERMIT-A") with { Note = "Renouvellement" });
        Assert.Equal("Renouvellement", (await _db.Permits.SingleAsync(p => p.Id == first)).Note);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.UpdateAsync(first, Request("PERMIT-B")));
        Assert.Equal("PV_Error_DuplicateNumber", ex.ErrorKey);
    }

    [Fact]
    public async Task Revoke_ThenExcludedUnlessRequested()
    {
        var id = await _service.CreateAsync(Request());
        await _service.SetRevokedAsync(id, true);

        Assert.Empty(await _service.SearchAsync());
        Assert.Single(await _service.SearchAsync(includeRevoked: true));

        await _service.SetRevokedAsync(id, false);
        Assert.Single(await _service.SearchAsync());
    }

    [Fact]
    public async Task SaveDetails_Twice_UpsertsSingleRow()
    {
        var permitId = await _service.CreateAsync(Request());
        var details = new PermitDetailsRequest(1, null, null, "Kinshasa", "Av. du Port 12", "+243 81 000", null,
            2, null, null, "Paris", "10 rue de Rivoli", "75001", 2,
            1, null, "Kinshasa", null, 20, "PERMIT-OLD-1", "Joseph Kabeya", "+243 99", 9, null,
            2, 2, 4, true, true, false, true, true, true, false);

        await _service.SaveDetailsAsync(permitId, details);
        await _service.SaveDetailsAsync(permitId, details with { CurrentTown = "Goma", NumberOfChildrenBornHere = 3 });

        var saved = await _db.PermitDetails.SingleAsync(d => d.PermitId == permitId);
        Assert.Equal("Goma", saved.CurrentTown);
        Assert.Equal(3, saved.NumberOfChildrenBornHere);
        Assert.True(saved.ChildrenEnrolledEducation == false);
    }

    [Fact]
    public async Task SaveTravelAndSelfEmployed_AppearInDetail()
    {
        var permitId = await _service.CreateAsync(Request());
        await _service.SaveTravelDetailsAsync(permitId, new PermitTravelRequest(
            FreeWillTravel: true, TravelReasonId: 6, DocumentTypeId: 3, TransportTypeId: 7,
            DepartureCountryId: 2, DepartureProvinceId: null, DepartureTown: "Paris",
            DepartureDate: DateTime.Today.AddDays(-10), ImmigrationCheck: true, PortOfEntryId: 2,
            ArrivalDate: DateTime.Today, IrregularExit: false));
        await _service.SaveSelfEmployedAsync(permitId, new PermitSelfEmployedRequest(
            "Épicerie Kivu", null, "Av. de la Paix 5", null, null));

        var detail = await _service.GetDetailAsync(permitId);
        Assert.NotNull(detail);
        Assert.Equal("Paris", detail!.Travel!.DepartureTown);
        Assert.Equal((short)2, detail.Travel.PortOfEntryId);
        Assert.Equal("Épicerie Kivu", detail.SelfEmployed!.Name);
    }

    [Fact]
    public async Task ContactsLicensesSecurityRecords_Crud()
    {
        var permitId = await _service.CreateAsync(Request());

        var contactId = await _service.AddContactAsync(permitId, 6, "Frère à Kinshasa");
        var licenseId = await _service.AddDriverLicenseAsync(permitId, 1, "DL-99887");
        var recordId = await _service.AddSecurityRecordAsync(permitId, "Vérification", "Aucun antécédent");

        var detail = await _service.GetDetailAsync(permitId);
        Assert.Single(detail!.Contacts);
        Assert.Single(detail.DriverLicenses);
        Assert.Single(detail.SecurityRecords);

        await _service.RemoveContactAsync(contactId);
        await _service.RemoveDriverLicenseAsync(licenseId);
        await _service.RemoveSecurityRecordAsync(recordId);
        detail = await _service.GetDetailAsync(permitId);
        Assert.Empty(detail!.Contacts);
        Assert.Empty(detail.DriverLicenses);
        Assert.Empty(detail.SecurityRecords);
    }

    [Fact]
    public async Task AddDriverLicense_WithoutNumber_IsRejected()
    {
        var permitId = await _service.CreateAsync(Request());
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.AddDriverLicenseAsync(permitId, 1, "  "));
        Assert.Equal("PV_Error_LicenseNumberRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task Dependants_Crud_AndNameRequired()
    {
        var permitId = await _service.CreateAsync(Request());
        var dependantId = await _service.AddDependantAsync(permitId, new DependantRequest(
            "Kabeya", "Esther", null, new DateTime(2015, 4, 2), 1, 1, 3, "KID-001", 1, 4, "+243 82"));

        var dependant = await _db.PermitDependants.SingleAsync(d => d.Id == dependantId);
        Assert.Equal("KABEYA", dependant.Surname);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.AddDependantAsync(permitId, new DependantRequest(" ", "", null, null, null, null, null, null, null, null, null)));
        Assert.Equal("Admin_Error_NameRequired", ex.ErrorKey);

        await _service.UpdateDependantAsync(dependantId, new DependantRequest(
            "Kabeya", "Esther", "Bénie", new DateTime(2015, 4, 2), 1, 1, 3, "KID-001", 1, 4, "+243 82"));
        Assert.Equal("Bénie", (await _db.PermitDependants.SingleAsync(d => d.Id == dependantId)).OtherNames);

        await _service.RemoveDependantAsync(dependantId);
        Assert.Empty(_db.PermitDependants.Where(d => d.Id == dependantId));
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>Tests de l'enforcement : refus, ordres de reconduite, amendes (Lot 4).</summary>
public class EnforcementServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly EnforcementService _service;
    private readonly int _refusedMovementId;
    private readonly int _refusedPersonId;
    private readonly int _grantedMovementId;

    public EnforcementServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"enforcement-{Guid.NewGuid()}").Options);
        var clock = new SystemClock();
        _service = new EnforcementService(_db, _currentUser, clock, new AuditWriter(_db, _currentUser, clock));
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3, BorderPointId = 2 }, "IMMIG_OFF");
        DossierSeed.SeedReferentials(_db);
        EnforcementSeed.SeedLookups(_db);

        var refused = EnforcementSeed.CreateMovement(_db, granted: false, "OP666001");
        _refusedMovementId = refused.MovementId;
        _refusedPersonId = refused.PersonId;
        _grantedMovementId = EnforcementSeed.CreateMovement(_db, granted: true, "OP666002").MovementId;
    }

    [Fact]
    public async Task GetRefusals_ReturnsOnlyRefusedMovements()
    {
        var refusals = await _service.GetRefusalsAsync();

        var refusal = Assert.Single(refusals);
        Assert.Equal(_refusedMovementId, refusal.MovementId);
        Assert.Equal("OP666001", refusal.DocumentNumber);
        Assert.Equal("Absence de visa", refusal.DeportationReasonFr);
        Assert.False(refusal.HasRemovalOrder);
        Assert.False(refusal.HasPenalty);
    }

    [Fact]
    public async Task SetRefusalJustification_OnRefusedMovement_Succeeds_AndAudits()
    {
        await _service.SetRefusalJustificationAsync(_refusedMovementId, 26);

        Assert.Equal(26, (await _db.Movements.SingleAsync(m => m.Id == _refusedMovementId)).RefusalJustificationId);
        Assert.Equal("Absence de visa valide", (await _service.GetRefusalsAsync()).Single().RefusalJustificationFr);
        Assert.Contains(_db.AuditLogs, a => a.Details!.Contains("Base légale du refus"));
    }

    [Fact]
    public async Task SetRefusalJustification_OnGrantedMovement_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.SetRefusalJustificationAsync(_grantedMovementId, 26));
        Assert.Equal("ENF_Error_MovementNotRefused", ex.ErrorKey);
    }

    [Fact]
    public async Task SetRefusalJustification_WrongLookupType_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.SetRefusalJustificationAsync(_refusedMovementId, 25)); // motif d'amende
        Assert.Equal("ENF_Error_JustificationUnknown", ex.ErrorKey);
    }

    [Fact]
    public async Task RemovalOrder_FromRefusal_CopiesDocumentNumber_AndFlagsRefusal()
    {
        var orderId = await _service.CreateRemovalOrderAsync(new RemovalOrderRequest(
            _refusedPersonId, "Order", "Refus d'entrée, absence de visa", _refusedMovementId));

        var order = await _db.RemovalOrders.SingleAsync(o => o.Id == orderId);
        Assert.Equal("OP666001", order.DocNumber);
        Assert.Equal("jdoe", order.OfficerName);
        Assert.Equal(_refusedMovementId, order.MovementId);
        Assert.True((await _service.GetRefusalsAsync()).Single().HasRemovalOrder);

        var list = await _service.GetRemovalOrdersAsync();
        Assert.Equal("Ordre de reconduite", Assert.Single(list).TypeFr);
    }

    [Fact]
    public async Task RemovalOrder_UnknownType_IsRejected_AndUnknownPersonToo()
    {
        var typeEx = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateRemovalOrderAsync(new RemovalOrderRequest(_refusedPersonId, "Nope", null, null)));
        Assert.Equal("ENF_Error_RemovalTypeRequired", typeEx.ErrorKey);

        var personEx = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateRemovalOrderAsync(new RemovalOrderRequest(999, "Notice", null, null)));
        Assert.Equal("Admin_Error_ItemNotFound", personEx.ErrorKey);
    }

    [Fact]
    public async Task RemovalOrder_Revoke_ExcludesFromList()
    {
        var orderId = await _service.CreateRemovalOrderAsync(new RemovalOrderRequest(_refusedPersonId, "Notice", null, null));

        await _service.SetRemovalOrderRevokedAsync(orderId, true);

        Assert.Empty(await _service.GetRemovalOrdersAsync());
        Assert.Single(await _service.GetRemovalOrdersAsync(includeRevoked: true));
        Assert.False((await _service.GetRefusalsAsync()).Single().HasRemovalOrder);
    }

    [Fact]
    public async Task Penalty_FromMovement_CopiesDocumentVisaAndData()
    {
        // Visa valide sur le document refusé : le numéro et les dates doivent être copiés.
        var documentId = (await _db.Movements.SingleAsync(m => m.Id == _refusedMovementId)).DocumentId;
        var visa = new Visa { DocumentId = documentId, VisaTypeId = 10, Number = "VISA-TEST-1", IssueDate = DateTime.Today.AddDays(-5), ExpireDate = DateTime.Today.AddMonths(2) };
        _db.Visas.Add(visa);
        await _db.SaveChangesAsync();
        var movement = await _db.Movements.SingleAsync(m => m.Id == _refusedMovementId);
        movement.VisaId = visa.Id;
        await _db.SaveChangesAsync();
        _db.Settings.Add(new Setting { Type = "Currency", Description = "USD", ShortDescription = "USD" });
        await _db.SaveChangesAsync();

        var ticketId = await _service.CreatePenaltyTicketAsync(new PenaltyTicketRequest(
            _refusedMovementId, 25, 250.00m, 12, null, null));

        var ticket = await _db.PenaltyTickets.SingleAsync(t => t.Id == ticketId);
        Assert.StartsWith($"TICKET-{DateTime.Today.Year}-", ticket.Number);
        Assert.Equal("OP666001", ticket.DocumentNumber);
        Assert.Equal("KABEYA", ticket.Surname);
        Assert.Equal("Paul", ticket.GivenName);
        Assert.Equal("VISA-TEST-1", ticket.VisaNumber);
        Assert.Equal("USD", ticket.CurrencyCode);
        Assert.Equal(250.00m, ticket.Amount);
        Assert.Equal(12, ticket.Duration);
        Assert.Equal("U001", ticket.UserId); // héritage : UserID = identifiant utilisateur
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(PenaltyTicket));

        var item = Assert.Single(await _service.GetPenaltyTicketsAsync());
        Assert.Equal("Dépassement de séjour", item.ReasonFr);
        Assert.False(item.Paid);
    }

    [Fact]
    public async Task Penalty_InvalidInputs_AreRejected()
    {
        var amountEx = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreatePenaltyTicketAsync(new PenaltyTicketRequest(_refusedMovementId, 25, 0m, null, null, null)));
        Assert.Equal("ENF_Error_AmountInvalid", amountEx.ErrorKey);

        var durationEx = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreatePenaltyTicketAsync(new PenaltyTicketRequest(_refusedMovementId, 25, 10m, -1, null, null)));
        Assert.Equal("ENF_Error_DurationInvalid", durationEx.ErrorKey);

        var reasonEx = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreatePenaltyTicketAsync(new PenaltyTicketRequest(_refusedMovementId, 23, 10m, null, null, null)));
        Assert.Equal("ENF_Error_ReasonRequired", reasonEx.ErrorKey);
    }

    [Fact]
    public async Task Penalty_Duplicate_ForSameMovementAndReason_IsRejected()
    {
        await _service.CreatePenaltyTicketAsync(new PenaltyTicketRequest(_refusedMovementId, 25, 100m, null, null, null));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreatePenaltyTicketAsync(new PenaltyTicketRequest(_refusedMovementId, 25, 100m, null, null, null)));
        Assert.Equal("ENF_Error_DuplicatePenalty", ex.ErrorKey);
    }

    [Fact]
    public async Task Penalty_Paid_RequiresReceipt_ThenAudits()
    {
        var ticketId = await _service.CreatePenaltyTicketAsync(new PenaltyTicketRequest(_refusedMovementId, 25, 75m, 5, null, null));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.SetPaidAsync(ticketId, true));
        Assert.Equal("ENF_Error_ReceiptRequired", ex.ErrorKey);

        await _service.SetPaidAsync(ticketId, true, "REC-2026-001");
        var ticket = await _db.PenaltyTickets.SingleAsync(t => t.Id == ticketId);
        Assert.True(ticket.Paid);
        Assert.NotNull(ticket.PaidDate);
        Assert.Equal("REC-2026-001", ticket.ReceiptNumber);
        Assert.Contains(_db.AuditLogs, a => a.Details!.Contains("encaissée"));
    }

    [Fact]
    public async Task Penalty_Search_And_Revoke()
    {
        var ticketId = await _service.CreatePenaltyTicketAsync(new PenaltyTicketRequest(_refusedMovementId, 25, 60m, null, null, null));

        Assert.Single(await _service.GetPenaltyTicketsAsync(search: "OP666001"));
        Assert.Single(await _service.GetPenaltyTicketsAsync(search: "Kabeya"));
        Assert.Empty(await _service.GetPenaltyTicketsAsync(search: "ZZZ"));

        await _service.SetPenaltyRevokedAsync(ticketId, true);
        Assert.Empty(await _service.GetPenaltyTicketsAsync());
        Assert.Single(await _service.GetPenaltyTicketsAsync(includeRevoked: true));
        Assert.False((await _service.GetRefusalsAsync()).Single().HasPenalty);
    }

    public void Dispose() => _db.Dispose();
}
