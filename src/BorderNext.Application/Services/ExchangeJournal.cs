using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>
/// Journal des échanges frontaliers (héritage : sync.ExportHistory +
/// sync.ImportExportErrorLog) : chaque import, export ou contrôle externe ouvre
/// une exécution, enregistre ses comptages, ses statuts et ses erreurs unitaires.
/// </summary>
public class ExchangeJournal
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public ExchangeJournal(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>Ouvre une exécution d'échange (statut « en cours »).</summary>
    public async Task<ExchangeRun> BeginAsync(string channel, string direction, string? reference = null,
        string? filePath = null, int? taskId = null, CancellationToken ct = default)
    {
        var run = new ExchangeRun
        {
            Channel = channel,
            Direction = direction,
            Reference = reference,
            FilePath = filePath,
            ExchangeTaskId = taskId,
            StartedAt = _clock.Now,
            Status = ExchangeRunStatus.Running,
            UserId = _currentUser.UserName,
        };
        _db.Set<ExchangeRun>().Add(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    /// <summary>Ajoute une erreur unitaire (enregistrée avec la clôture de l'exécution).</summary>
    public void AddError(ExchangeRun run, string? step, string? itemReference, string? message)
    {
        run.Errors.Add(new ExchangeError
        {
            ExchangeRunId = run.Id,
            Step = step,
            ItemReference = Truncate(itemReference, 100),
            Message = Truncate(message, 1000),
            OccurredAt = _clock.Now,
        });
        run.ErrorCount = run.Errors.Count;
    }

    /// <summary>Clôture l'exécution : comptages, statut (partiel si des erreurs subsistent),
    /// résumé et trace d'audit.</summary>
    public async Task CompleteAsync(ExchangeRun run, int status, int itemCount, int successCount,
        string? summary = null, CancellationToken ct = default)
    {
        run.ItemCount = itemCount;
        run.SuccessCount = successCount;
        run.ErrorCount = run.Errors.Count;
        run.Status = status == ExchangeRunStatus.Success && run.ErrorCount > 0
            ? ExchangeRunStatus.Partial
            : status;
        run.Summary = Truncate(summary, 1000);
        run.CompletedAt = _clock.Now;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.ExchangeRun, "ExchangeRun", run.Id.ToString(),
            $"{run.Channel}/{run.Direction} : {run.SuccessCount}/{run.ItemCount} élément(s), {run.ErrorCount} erreur(s).", ct);
    }

    /// <summary>Clôture sur exception : statut « échec » et message d'erreur global.</summary>
    public async Task FailAsync(ExchangeRun run, Exception exception, CancellationToken ct = default)
    {
        AddError(run, "exception", null, exception.Message);
        await CompleteAsync(run, ExchangeRunStatus.Failed, run.ItemCount, run.SuccessCount,
            exception.Message, ct);
    }

    /// <summary>Exécutions du journal, filtrées par canal et période, les plus récentes en premier.</summary>
    public async Task<IReadOnlyList<ExchangeRun>> SearchRunsAsync(string? channel = null, string? direction = null,
        DateTime? from = null, DateTime? to = null, int take = 200, CancellationToken ct = default)
    {
        var query = _db.Set<ExchangeRun>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(channel)) query = query.Where(r => r.Channel == channel);
        if (!string.IsNullOrWhiteSpace(direction)) query = query.Where(r => r.Direction == direction);
        if (from is { } start) query = query.Where(r => r.StartedAt >= start);
        if (to is { } end) query = query.Where(r => r.StartedAt < end);

        return await query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .Take(take).ToListAsync(ct);
    }

    /// <summary>Erreurs unitaires d'une exécution.</summary>
    public async Task<IReadOnlyList<ExchangeError>> GetErrorsAsync(int runId, CancellationToken ct = default)
        => await _db.Set<ExchangeError>().AsNoTracking()
            .Where(e => e.ExchangeRunId == runId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync(ct);

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
