using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SettingsService _settings;

    public SettingsServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"settings-{Guid.NewGuid()}")
            .Options);
        _settings = new SettingsService(_db, _currentUser, new SystemClock(), new AuditWriter(_db, _currentUser, new SystemClock()));
    }

    [Fact]
    public async Task SetThenGet_Int_RoundTrips()
    {
        await _settings.SetIntAsync("UserTimeOut", 600, borderPointId: 2);
        Assert.Equal(600, await _settings.GetIntAsync("UserTimeOut", borderPointId: 2));
    }

    [Fact]
    public async Task SetThenGet_String_RoundTrips()
    {
        await _settings.SetStringAsync("ServerIP", "10.0.0.5", borderPointId: 1);
        Assert.Equal("10.0.0.5", await _settings.GetStringAsync("ServerIP", borderPointId: 1));
    }

    [Fact]
    public async Task Setting_PerBorderPoint_IsIsolated()
    {
        await _settings.SetIntAsync("FoundRecordLimit", 100, borderPointId: 1);
        await _settings.SetIntAsync("FoundRecordLimit", 250, borderPointId: 2);

        Assert.Equal(100, await _settings.GetIntAsync("FoundRecordLimit", borderPointId: 1));
        Assert.Equal(250, await _settings.GetIntAsync("FoundRecordLimit", borderPointId: 2));
        Assert.Null(await _settings.GetIntAsync("FoundRecordLimit", borderPointId: 3));
    }

    [Fact]
    public async Task Set_ExistingKey_Updates_WithoutDuplicating()
    {
        await _settings.SetIntAsync("DeletingPeriod", 30);
        await _settings.SetIntAsync("DeletingPeriod", 90);

        Assert.Equal(90, await _settings.GetIntAsync("DeletingPeriod"));
        Assert.Single(_db.Settings.Where(s => s.Type == "DeletingPeriod"));
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsNull()
    {
        Assert.Null(await _settings.GetStringAsync("DoesNotExist"));
    }

    // ---- Administration du paramétrage (Lot 1) -------------------------------

    [Fact]
    public async Task AdminList_ReturnsAllSettings_WithBorderPointCode()
    {
        _db.BorderPoints.Add(new Domain.Entities.BorderPoint { Id = 2, ExternalId = "FIH" });
        await _db.SaveChangesAsync();
        await _settings.UpsertAsync(null, "FoundRecordLimit", 1000, null, "Limite de recherche", 2);

        var list = await _settings.ListAsync();

        var setting = Assert.Single(list);
        Assert.Equal("FoundRecordLimit", setting.Type);
        Assert.Equal(1000, setting.Value);
        Assert.Equal("FIH", setting.BorderPointCode);
        Assert.Equal("Limite de recherche", setting.Description);
    }

    [Fact]
    public async Task AdminSearch_MatchesTypeOrDescription()
    {
        await _settings.UpsertAsync(null, "DeletingPeriod", 0, null, "Période de suppression", null);
        await _settings.UpsertAsync(null, "Currency", 4, null, "USD", null);

        Assert.Single(await _settings.ListAsync("Deleting"));
        Assert.Single(await _settings.ListAsync("suppression"));
        Assert.Equal(2, (await _settings.ListAsync()).Count);
    }

    [Fact]
    public async Task AdminUpsert_CreatesThenUpdates_WithoutDuplicating()
    {
        var id = await _settings.UpsertAsync(null, "UserTimeOut", 600, null, null, null);
        await _settings.UpsertAsync(id, "UserTimeOut", 900, null, "Délai", null);

        var setting = Assert.Single(_db.Settings.Where(s => s.Type == "UserTimeOut"));
        Assert.Equal(900, setting.Value);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == "Setting");
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowUpdated && a.Entity == "Setting");
    }

    [Fact]
    public async Task AdminUpsert_EmptyType_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<BorderNext.Application.Abstractions.DomainValidationException>(
            () => _settings.UpsertAsync(null, "  ", 1, null, null, null));
        Assert.Equal("Admin_Error_SettingTypeRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task AdminDelete_RemovesSetting_AndAudits()
    {
        var id = await _settings.UpsertAsync(null, "ServerIP", null, "10.0.0.5", null, null);
        await _settings.DeleteAsync(id);

        Assert.Empty(_db.Settings.Where(s => s.Type == "ServerIP"));
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowRevoked && a.Details!.Contains("ServerIP"));
    }

    public void Dispose() => _db.Dispose();
}
