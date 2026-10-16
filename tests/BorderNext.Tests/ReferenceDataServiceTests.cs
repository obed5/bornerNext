using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Tests des nomenclatures génériques et des contrôles (Lot 1).</summary>
public class ReferenceDataServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly ReferenceDataService _service;

    public ReferenceDataServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"refdata-{Guid.NewGuid()}")
            .Options);
        _service = new ReferenceDataService(_db, _currentUser, new SystemClock(), new AuditWriter(_db, _currentUser, new SystemClock()));
        _currentUser.Set(new User { Id = "ADM001", UserName = "admin", UserRoleId = 1 }, "ADMIN");
        Seed();
    }

    private void Seed()
    {
        var regions = new LookupType { Id = 1, Code = "REGION", NameFr = "Régions", NameEn = "Regions" };
        regions.Items.Add(new Lookup
        {
            Id = 1, Code = "WA", SortOrder = 0,
            Cultures = { new LookupCulture { Culture = "fr", Description = "Afrique de l'Ouest" }, new LookupCulture { Culture = "en", Description = "West Africa" } }
        });
        _db.LookupTypes.Add(regions);

        var docTypes = new LookupType { Id = 2, Code = "DOCUMENT_TYPE", NameFr = "Types de documents", NameEn = "Document types" };
        docTypes.Items.Add(new Lookup
        {
            Id = 2, Code = "OP", SortOrder = 1,
            Cultures = { new LookupCulture { Culture = "fr", Description = "Passeport ordinaire" }, new LookupCulture { Culture = "en", Description = "Ordinary Passport" } }
        });
        docTypes.Items.Add(new Lookup
        {
            Id = 3, Code = "PD", SortOrder = 20, Revoke = true,
            Cultures = { new LookupCulture { Culture = "fr", Description = "Passeport diplomatique" }, new LookupCulture { Culture = "en", Description = "Diplomatic Passport" } }
        });
        _db.LookupTypes.Add(docTypes);

        _db.Controls.Add(new Control
        {
            Id = 1, Code = "DM_USERS", SortOrder = 100,
            Cultures = { new ControlCulture { Culture = "fr", Description = "Menu Utilisateurs" }, new ControlCulture { Culture = "en", Description = "Users menu" } }
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetTypes_ReturnsActiveTypes_WithActiveItemCount()
    {
        var types = await _service.GetTypesAsync();

        var docTypes = Assert.Single(types, t => t.Code == "DOCUMENT_TYPE");
        Assert.Equal(1, docTypes.ItemCount); // PD est révoqué
        Assert.Equal(2, types.Count);
    }

    [Fact]
    public async Task GetItems_ExcludesRevoked_UnlessRequested()
    {
        Assert.Single(await _service.GetItemsAsync("DOCUMENT_TYPE"));
        Assert.Equal(2, (await _service.GetItemsAsync("DOCUMENT_TYPE", includeRevoked: true)).Count);
    }

    [Fact]
    public async Task GetItems_Search_MatchesFrenchOrEnglishOrCode()
    {
        Assert.Single(await _service.GetItemsAsync("DOCUMENT_TYPE", search: "diplomatique", includeRevoked: true));
        Assert.Single(await _service.GetItemsAsync("DOCUMENT_TYPE", search: "Ordinary"));
        Assert.Single(await _service.GetItemsAsync("DOCUMENT_TYPE", search: "OP"));
    }

    [Fact]
    public async Task CreateItem_AddsCultures_AndAudits()
    {
        var id = await _service.CreateItemAsync("DOCUMENT_TYPE", "ID", 15, "Carte d'Identité", "Identification Card");

        var created = await _db.Lookups.Include(l => l.Cultures).SingleAsync(l => l.Id == id);
        Assert.Equal("ID", created.Code);
        Assert.Equal("Carte d'Identité", created.Cultures.Single(c => c.Culture == "fr").Description);
        Assert.Equal("Identification Card", created.Cultures.Single(c => c.Culture == "en").Description);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(Lookup));
    }

    [Fact]
    public async Task CreateItem_DuplicateCode_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateItemAsync("DOCUMENT_TYPE", "OP", 0, "Doublon", "Duplicate"));
        Assert.Equal("Admin_Error_DuplicateCode", ex.ErrorKey);
    }

    [Fact]
    public async Task CreateItem_WithoutAnyName_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateItemAsync("DOCUMENT_TYPE", "XX", 0, "  ", ""));
        Assert.Equal("Admin_Error_NameRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task CreateItem_UnknownType_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateItemAsync("UNKNOWN_TYPE", null, 0, "Test", ""));
        Assert.Equal("Admin_Error_TypeNotFound", ex.ErrorKey);
    }

    [Fact]
    public async Task CreateItem_WithoutFrenchName_FallsBackToEnglish()
    {
        var id = await _service.CreateItemAsync("DOCUMENT_TYPE", "EN", 0, "", "English only");
        var created = await _db.Lookups.Include(l => l.Cultures).SingleAsync(l => l.Id == id);
        Assert.Equal("English only", created.Cultures.Single(c => c.Culture == "fr").Description);
    }

    [Fact]
    public async Task UpdateItem_ChangesCodeAndNames()
    {
        await _service.UpdateItemAsync(2, "OP2", 5, "Passeport ordinaire (MAJ)", "Ordinary Passport (upd)");

        var updated = await _db.Lookups.Include(l => l.Cultures).SingleAsync(l => l.Id == 2);
        Assert.Equal("OP2", updated.Code);
        Assert.Equal(5, updated.SortOrder);
        Assert.Equal("Passeport ordinaire (MAJ)", updated.Cultures.Single(c => c.Culture == "fr").Description);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowUpdated && a.EntityId == "2");
    }

    [Fact]
    public async Task RevokeItem_SetsFlagAndDate_ThenRestoreClearsIt()
    {
        await _service.SetItemRevokedAsync(2, true);
        var revoked = await _db.Lookups.SingleAsync(l => l.Id == 2);
        Assert.True(revoked.Revoke);
        Assert.NotNull(revoked.RevokeDate);

        await _service.SetItemRevokedAsync(2, false);
        revoked = await _db.Lookups.SingleAsync(l => l.Id == 2);
        Assert.False(revoked.Revoke);
        Assert.Null(revoked.RevokeDate);
    }

    [Fact]
    public async Task GetControls_ReturnsLocalizedControls()
    {
        var controls = await _service.GetControlsAsync();

        var control = Assert.Single(controls);
        Assert.Equal("DM_USERS", control.Code);
        Assert.Equal("Menu Utilisateurs", control.Fr);
        Assert.Equal("Users menu", control.En);
    }

    [Fact]
    public async Task UpdateControl_ChangesNamesAndAudits()
    {
        await _service.UpdateControlAsync(1, 120, "Menu Utilisateurs (MAJ)", "Users menu (upd)");

        var control = await _db.Controls.Include(c => c.Cultures).SingleAsync(c => c.Id == 1);
        Assert.Equal(120, control.SortOrder);
        Assert.Equal("Menu Utilisateurs (MAJ)", control.Cultures.Single(c => c.Culture == "fr").Description);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowUpdated && a.Entity == nameof(Control));
    }

    public void Dispose() => _db.Dispose();
}
