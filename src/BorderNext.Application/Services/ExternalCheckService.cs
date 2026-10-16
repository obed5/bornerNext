using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>
/// Contrôles externes des systèmes partenaires (SIMIEC, Belize, système aérien) :
/// les connecteurs sont définis par les tâches d'échange (canal + point d'accès +
/// mode simulé/direct), chaque contrôle est journalisé dans le journal d'échanges.
/// </summary>
public class ExternalCheckService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ExchangeJournal _journal;
    private readonly IReadOnlyList<IExternalCheckConnector> _connectors;

    public ExternalCheckService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit,
        ExchangeJournal journal, IEnumerable<IExternalCheckConnector> connectors)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _journal = journal;
        _connectors = connectors.ToList();
    }

    /// <summary>Définitions des connecteurs de contrôle (tâches d'échange direction CHECK).</summary>
    public async Task<IReadOnlyList<ExchangeTask>> GetConnectorsAsync(CancellationToken ct = default)
        => await _db.Set<ExchangeTask>().AsNoTracking()
            .Where(t => t.Direction == ExchangeDirections.Check)
            .OrderBy(t => t.Priority).ThenBy(t => t.Name)
            .ToListAsync(ct);

    /// <summary>Paramètres effectifs d'un connecteur (mode simulé par défaut).</summary>
    public async Task<ConnectorSettings> GetSettingsAsync(string channel, CancellationToken ct = default)
    {
        var task = await _db.Set<ExchangeTask>().AsNoTracking()
            .Where(t => t.Channel == channel && t.Direction == ExchangeDirections.Check)
            .OrderBy(t => t.Priority).ThenBy(t => t.Id)
            .FirstOrDefaultAsync(ct);

        return SettingsFrom(task);
    }

    /// <summary>Contrôle un partenaire : connecteur actif requis, résultat journalisé.</summary>
    public async Task<ExternalCheckOutcome> CheckAsync(string channel, ExternalCheckRequest request,
        CancellationToken ct = default)
    {
        var connector = _connectors.FirstOrDefault(c => c.Channel == channel)
            ?? throw new DomainValidationException("EX_Error_ConnectorUnknown");
        var settings = await GetSettingsAsync(channel, ct);
        if (!settings.IsActive)
            throw new DomainValidationException("EX_Error_ConnectorInactive");

        var reference = request.DocumentNumber;
        var run = await _journal.BeginAsync(channel, ExchangeDirections.Check, reference, ct: ct);

        try
        {
            var outcome = await connector.CheckAsync(request, settings, ct);
            var success = outcome.Verdict is ExternalCheckVerdicts.Clear or ExternalCheckVerdicts.Alert ? 1 : 0;

            if (outcome.Verdict == ExternalCheckVerdicts.Error)
                _journal.AddError(run, channel, reference, outcome.Summary);
            else if (settings.IsMock)
                _journal.AddError(run, "Simulateur", reference,
                    "Contrôle exécuté en mode simulé — point d'accès réel non configuré.");

            await _journal.CompleteAsync(run,
                outcome.Verdict == ExternalCheckVerdicts.Error ? ExchangeRunStatus.Failed : ExchangeRunStatus.Success,
                1, success, outcome.Summary, ct);

            await _audit.WriteAsync(AuditEventTypes.SearchPerformed, $"ExternalCheck_{channel}", reference,
                outcome.Verdict, ct);
            return outcome;
        }
        catch (Exception exception)
        {
            await _journal.FailAsync(run, exception, ct);
            throw;
        }
    }

    /// <summary>Contrôle tous les partenaires actifs (un résultat par connecteur).</summary>
    public async Task<IReadOnlyList<ExternalCheckOutcome>> CheckAllAsync(ExternalCheckRequest request,
        CancellationToken ct = default)
    {
        var outcomes = new List<ExternalCheckOutcome>();
        foreach (var connector in _connectors)
        {
            var settings = await GetSettingsAsync(connector.Channel, ct);
            if (!settings.IsActive) continue;
            outcomes.Add(await CheckAsync(connector.Channel, request, ct));
        }

        return outcomes;
    }

    /// <summary>Met à jour la configuration d'un connecteur (point d'accès, mode, activation).</summary>
    public async Task SaveConnectorAsync(int taskId, string? endpoint, string mode, bool isActive,
        CancellationToken ct = default)
    {
        var task = await _db.Set<ExchangeTask>().FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new DomainValidationException("EX_Error_ConnectorUnknown");

        var normalizedMode = string.Equals(mode, ConnectorSettings.ModeLive, StringComparison.OrdinalIgnoreCase)
            ? ConnectorSettings.ModeLive
            : ConnectorSettings.ModeMock;

        task.Endpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
        task.Parameters = $"MODE={normalizedMode}";
        task.IsActive = isActive;
        task.UpdateDate = _clock.Now;
        task.UpdateUser = _currentUser.UserName;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(ExchangeTask), task.Id.ToString(),
            $"{task.Name} : mode {normalizedMode}, actif {isActive}.", ct);
    }

    /// <summary>Paramètres d'exécution d'un connecteur d'après sa définition (MOCK par défaut).</summary>
    public static ConnectorSettings SettingsFrom(ExchangeTask? task)
    {
        if (task is null)
            return new ConnectorSettings(string.Empty, string.Empty, false, ConnectorSettings.ModeMock, null, null);

        var mode = ConnectorSettings.ModeMock;
        foreach (var token in (task.Parameters ?? string.Empty)
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith("MODE=", StringComparison.OrdinalIgnoreCase))
                mode = token[5..].Trim();
        }

        return new ConnectorSettings(task.Channel, task.Name, task.IsActive, mode, task.Endpoint, task.Parameters);
    }
}
