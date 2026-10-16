using System.Text;
using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Entrée du journal d'audit (lecture administration).</summary>
public sealed record AuditEntryDto(long Id, DateTime EventTime, string EventType, string? UserId,
    string? MachineName, string? PcNumber, string? Entity, string? EntityId, string? Details);

/// <summary>
/// Consultation du journal d'audit (héritage : table AuditLog + réglages
/// AUDIT_LOG_*) : filtres utilisateur / type d'événement / période, export CSV.
/// </summary>
public class AuditQueryService
{
    /// <summary>Nombre maximal de lignes retournées par recherche (bornage mémoire).</summary>
    public const int MaxRows = 500;

    private readonly DbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public AuditQueryService(DbContext db, IAuditWriter audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AuditEntryDto>> SearchAsync(string? userId = null, string? eventType = null,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var query = _db.Set<AuditLog>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(a => a.UserId != null && a.UserId.Contains(userId));
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(a => a.EventType == eventType);
        if (from is not null)
            query = query.Where(a => a.EventTime >= from);
        if (to is not null)
            query = query.Where(a => a.EventTime <= to);

        var rows = await query.OrderByDescending(a => a.EventTime).ThenByDescending(a => a.Id)
            .Take(MaxRows)
            .ToListAsync(ct);

        return rows.Select(a => new AuditEntryDto(a.Id, a.EventTime, a.EventType, a.UserId,
            a.MachineName, a.PcNumber, a.Entity, a.EntityId, a.Details)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetEventTypesAsync(CancellationToken ct = default)
        => await _db.Set<AuditLog>().Select(a => a.EventType).Distinct().OrderBy(t => t).ToListAsync(ct);

    /// <summary>Export CSV (séparateur « ; », encodage UTF-8 avec BOM pour Excel) et trace d'export dans l'audit.</summary>
    public async Task<string> ExportCsvAsync(IReadOnlyList<AuditEntryDto> rows, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id;DateHeure;Evenement;Utilisateur;Machine;Poste;Entite;EntiteId;Detail");
        foreach (var r in rows)
            sb.AppendLine(string.Join(';',
                r.Id, r.EventTime.ToString("yyyy-MM-dd HH:mm:ss"), Esc(r.EventType), Esc(r.UserId),
                Esc(r.MachineName), Esc(r.PcNumber), Esc(r.Entity), Esc(r.EntityId), Esc(r.Details)));

        await _audit.WriteAsync(AuditEventTypes.DataExported, nameof(AuditLog), null,
            $"Export CSV du journal d'audit ({rows.Count} lignes) le {_clock.Now:yyyy-MM-dd HH:mm}", ct);
        return sb.ToString();
    }

    private static string Esc(string? value)
        => value is null ? string.Empty : value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
}
