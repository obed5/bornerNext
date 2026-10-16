using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Contrôles externes (SIMIEC, Belize, système aérien en mode simulé) et journal d'échanges.</summary>
public class ExternalCheckServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly ExternalCheckService _service;

    public ExternalCheckServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"exc-{Guid.NewGuid()}").Options);
        ExchangeSeed.SeedReferentials(_db);

        var audit = new AuditWriter(_db, _currentUser, _clock);
        var journal = new ExchangeJournal(_db, _currentUser, _clock, audit);
        Func<HttpClient> clientFactory = () => new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        _service = new ExternalCheckService(_db, _currentUser, _clock, audit, journal, new IExternalCheckConnector[]
        {
            new SimiecConnector(clientFactory),
            new BelizeConnector(clientFactory),
            new AirConnector(_db, clientFactory),
        }, new BorderNext.Infrastructure.Security.DpapiSecretProtector());

        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 4, BorderPointId = 2 }, "ALERT_OFF");
        _currentUser.SetPcNumber("A01");
    }

    public void Dispose() => _db.Dispose();

    private static ExternalCheckRequest Request(string number = "OP123456", DateTime? expiry = null)
        => new(number, "P", "CD", "MBALA", "JEAN", new DateTime(1990, 5, 20), "CD", expiry);

    [Fact]
    public async Task Simiec_ClearForOrdinaryNumber()
    {
        var outcome = await _service.CheckAsync(ExchangeChannels.Simiec, Request("OP123456"));

        Assert.Equal(ExternalCheckVerdicts.Clear, outcome.Verdict);
        Assert.Equal("SIM-OP123456", outcome.Reference);
        Assert.False(outcome.IsLive);
    }

    [Fact]
    public async Task Simiec_AlertsOnWithdrawnTitle()
    {
        var outcome = await _service.CheckAsync(ExchangeChannels.Simiec, Request("OPEXP999"));

        Assert.Equal(ExternalCheckVerdicts.Alert, outcome.Verdict);
        Assert.Contains("retiré", outcome.Summary);
    }

    [Fact]
    public async Task Simiec_AlertsOnIrregularStay()
    {
        var outcome = await _service.CheckAsync(ExchangeChannels.Simiec, Request("OP123450"));

        Assert.Equal(ExternalCheckVerdicts.Alert, outcome.Verdict);
        Assert.Contains("séjour irrégulier", outcome.Summary);
    }

    [Fact]
    public async Task Simiec_WithoutDocumentNumber_IsInconclusive()
    {
        var outcome = await _service.CheckAsync(ExchangeChannels.Simiec, Request(number: ""));

        Assert.Equal(ExternalCheckVerdicts.Inconclusive, outcome.Verdict);
        Assert.Null(outcome.Reference);
    }

    [Fact]
    public async Task Belize_ValidCardIsClear_AndExpiredAlerts()
    {
        var valid = await _service.CheckAsync(ExchangeChannels.Belize,
            Request(expiry: DateTime.Today.AddMonths(6)));
        Assert.Equal(ExternalCheckVerdicts.Clear, valid.Verdict);
        Assert.Contains("valide", valid.Summary);

        var expired = await _service.CheckAsync(ExchangeChannels.Belize,
            Request(expiry: DateTime.Today.AddDays(-1)));
        Assert.Equal(ExternalCheckVerdicts.Alert, expired.Verdict);
        Assert.Contains("expiré", expired.Summary);
    }

    [Fact]
    public async Task Belize_WithoutExpiry_IsInconclusive()
    {
        var outcome = await _service.CheckAsync(ExchangeChannels.Belize, Request());
        Assert.Equal(ExternalCheckVerdicts.Inconclusive, outcome.Verdict);
    }

    [Fact]
    public async Task Air_BlacklistedNameAlerts()
    {
        _db.Settings.Add(new Setting
        {
            Type = AirConnector.MockBlacklistSetting,
            StringValue = "MBALA, KABILA",
        });
        await _db.SaveChangesAsync();

        var outcome = await _service.CheckAsync(ExchangeChannels.Air, Request());

        Assert.Equal(ExternalCheckVerdicts.Alert, outcome.Verdict);
        Assert.Contains("liste noire", outcome.Summary);
    }

    [Fact]
    public async Task Air_LastMovementIsDeterministic()
    {
        var first = await _service.CheckAsync(ExchangeChannels.Air, Request("OP123456"));
        var second = await _service.CheckAsync(ExchangeChannels.Air, Request("OP123456"));

        Assert.Equal(ExternalCheckVerdicts.Clear, first.Verdict);
        Assert.Equal(first.Summary, second.Summary); // même document → même dernier mouvement simulé
        Assert.Contains("dernier mouvement", first.Summary);
    }

    [Fact]
    public async Task Check_UnknownChannel_IsRejected()
    {
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CheckAsync("INCONNU", Request()));
        Assert.Equal("EX_Error_ConnectorUnknown", exception.ErrorKey);
    }

    [Fact]
    public async Task Check_InactiveConnector_IsRejected()
    {
        await _service.SaveConnectorAsync(
            _db.ExchangeTasks.Single(t => t.Channel == ExchangeChannels.Belize).Id, null, "MOCK", false);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CheckAsync(ExchangeChannels.Belize, Request()));
        Assert.Equal("EX_Error_ConnectorInactive", exception.ErrorKey);
    }

    [Fact]
    public async Task Check_JournalsRunAndSimulatorNotice()
    {
        await _service.CheckAsync(ExchangeChannels.Simiec, Request());

        var run = await _db.ExchangeRuns.SingleAsync();
        Assert.Equal(ExchangeChannels.Simiec, run.Channel);
        Assert.Equal(ExchangeDirections.Check, run.Direction);
        Assert.Equal(ExchangeRunStatus.Partial, run.Status); // succès avec avis de simulateur
        Assert.Equal(1, run.ItemCount);
        Assert.Contains(_db.ExchangeErrors, e => e.Step == "Simulateur");
    }

    [Fact]
    public async Task Check_LiveEndpointUnreachable_ReturnsErrorVerdict()
    {
        var task = _db.ExchangeTasks.Single(t => t.Channel == ExchangeChannels.Simiec);
        await _service.SaveConnectorAsync(task.Id, "http://127.0.0.1:1/unreachable", "LIVE", true);

        var outcome = await _service.CheckAsync(ExchangeChannels.Simiec, Request("OP123456"));

        Assert.Equal(ExternalCheckVerdicts.Error, outcome.Verdict);
        Assert.True(outcome.IsLive);

        var run = await _db.ExchangeRuns.OrderByDescending(r => r.Id).FirstAsync();
        Assert.Equal(ExchangeRunStatus.Failed, run.Status);
    }

    [Fact]
    public async Task CheckAll_SkipsInactiveConnectors()
    {
        await _service.SaveConnectorAsync(
            _db.ExchangeTasks.Single(t => t.Channel == ExchangeChannels.Belize).Id, null, "MOCK", false);

        var outcomes = await _service.CheckAllAsync(Request());

        Assert.Equal(2, outcomes.Count);
        Assert.DoesNotContain(outcomes, o => o.Channel == ExchangeChannels.Belize);
    }

    [Fact]
    public async Task SaveConnector_UpdatesEndpointModeAndAudits()
    {
        var task = _db.ExchangeTasks.Single(t => t.Channel == ExchangeChannels.Air);

        await _service.SaveConnectorAsync(task.Id, "https://air.example.org/check", "live", true);

        var updated = await _db.ExchangeTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal("https://air.example.org/check", updated.Endpoint);
        Assert.Equal("MODE=LIVE", updated.Parameters);
        Assert.True(updated.IsActive);
        Assert.Equal("jdoe", updated.UpdateUser);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowUpdated
            && a.Entity == nameof(ExchangeTask));

        var settings = ExternalCheckService.SettingsFrom(updated);
        Assert.False(settings.IsMock);
    }

    [Fact]
    public async Task GetConnectors_ReturnsCheckTasksOrderedByPriority()
    {
        var connectors = await _service.GetConnectorsAsync();

        Assert.Equal(3, connectors.Count);
        Assert.Equal(ExchangeChannels.Simiec, connectors[0].Channel);
        Assert.Equal(ExchangeChannels.Air, connectors[^1].Channel);
    }
}

/// <summary>Journal des échanges : exécutions, statuts, comptages et erreurs unitaires.</summary>
public class ExchangeJournalTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly ExchangeJournal _journal;

    public ExchangeJournalTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"exj-{Guid.NewGuid()}").Options);
        _journal = new ExchangeJournal(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 1 }, "ADMIN");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Complete_RecordsCountsAndAudits()
    {
        var run = await _journal.BeginAsync(ExchangeChannels.Edifact, ExchangeDirections.Import, "CTRL1");

        await _journal.CompleteAsync(run, ExchangeRunStatus.Success, 10, 10, "Import terminé.");

        var stored = await _db.ExchangeRuns.SingleAsync();
        Assert.Equal(ExchangeRunStatus.Success, stored.Status);
        Assert.Equal(10, stored.ItemCount);
        Assert.Equal(10, stored.SuccessCount);
        Assert.NotNull(stored.CompletedAt);
        Assert.Equal("Import terminé.", stored.Summary);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.ExchangeRun);
    }

    [Fact]
    public async Task Complete_WithErrors_DowngradesToPartial()
    {
        var run = await _journal.BeginAsync(ExchangeChannels.Apis, ExchangeDirections.Check, "CTRL1");
        _journal.AddError(run, "Rapprochement", "OP123456", "Document introuvable.");

        await _journal.CompleteAsync(run, ExchangeRunStatus.Success, 5, 4);

        var stored = await _db.ExchangeRuns.SingleAsync();
        Assert.Equal(ExchangeRunStatus.Partial, stored.Status);
        Assert.Equal(1, stored.ErrorCount);
    }

    [Fact]
    public async Task Fail_RecordsException()
    {
        var run = await _journal.BeginAsync(ExchangeChannels.Edifact, ExchangeDirections.Import);

        await _journal.FailAsync(run, new InvalidOperationException("Enveloppe illisible."));

        var stored = await _db.ExchangeRuns.Include(r => r.Errors).SingleAsync();
        Assert.Equal(ExchangeRunStatus.Failed, stored.Status);
        Assert.Equal("exception", stored.Errors.Single().Step);
        Assert.Contains("Enveloppe illisible", stored.Errors.Single().Message);
    }

    [Fact]
    public async Task Search_FiltersByChannelAndPeriod()
    {
        var first = await _journal.BeginAsync(ExchangeChannels.Edifact, ExchangeDirections.Import, "A");
        await _journal.CompleteAsync(first, ExchangeRunStatus.Success, 1, 1);

        var second = await _journal.BeginAsync(ExchangeChannels.Simiec, ExchangeDirections.Check, "B");
        await _journal.CompleteAsync(second, ExchangeRunStatus.Success, 1, 1);

        var simiec = await _journal.SearchRunsAsync(channel: ExchangeChannels.Simiec);
        Assert.Single(simiec);
        Assert.Equal("B", simiec[0].Reference);

        var recent = await _journal.SearchRunsAsync(from: DateTime.Now.AddMinutes(-5));
        Assert.Equal(2, recent.Count);

        var past = await _journal.SearchRunsAsync(to: DateTime.Now.AddMinutes(-5));
        Assert.Empty(past);

        var errors = await _journal.GetErrorsAsync(first.Id);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GetErrors_ReturnsItemizedErrors()
    {
        var run = await _journal.BeginAsync(ExchangeChannels.Edifact, ExchangeDirections.Import, "CTRL1");
        _journal.AddError(run, "Import", "AF100", "Passager sans document.");
        _journal.AddError(run, "Import", "AF100", "Date de naissance invalide.");
        await _journal.CompleteAsync(run, ExchangeRunStatus.Success, 2, 0);

        var errors = await _journal.GetErrorsAsync(run.Id);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal("Import", e.Step));
    }
}
