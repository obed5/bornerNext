using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Résultat d'une purge (rapport d'exploitation, aucune donnée personnelle).</summary>
public sealed record PurgeReport(int DirectoryCount, int FileCount, long BytesFreed, IReadOnlyList<string> DeletedFiles);

/// <summary>
/// Rétention des fichiers produits par le poste : fichiers d'échange (messages reçus,
/// exports PAXLST/XML) et journaux applicatifs. La durée est un réglage
/// (<see cref="ExchangeFileStore.RetentionSetting"/>, en jours ; 0 = conservation
/// illimitée). La purge est journalisée par l'appelant ; elle ne touche à la base que
/// pour les fichiers d'échange, dont le registre est conservé même après suppression
/// du contenu (la ligne garde chemin, empreinte et taille pour l'audit).
/// </summary>
public class FileRetentionService
{
    private readonly DbContext _db;
    private readonly ExchangeFileStore _store;
    private readonly Abstractions.IClock _clock;

    public FileRetentionService(DbContext db, ExchangeFileStore store, Abstractions.IClock clock)
    {
        _db = db;
        _store = store;
        _clock = clock;
    }

    /// <summary>Durée de conservation configurée (jours ; 0 = illimitée).</summary>
    public async Task<int> GetRetentionDaysAsync(CancellationToken ct = default)
    {
        var value = await _db.Set<Setting>()
            .Where(s => s.Type == ExchangeFileStore.RetentionSetting)
            .Select(s => (int?)s.Value)
            .FirstOrDefaultAsync(ct);
        return value is > 0 ? value.Value : 0;
    }

    /// <summary>
    /// Purge les fichiers de plus de <paramref name="keepDays"/> jours dans un répertoire
    /// (récursif). <paramref name="dryRun"/> liste sans supprimer.
    /// </summary>
    public PurgeReport PurgeDirectory(string directory, int keepDays, bool dryRun = false)
    {
        if (keepDays <= 0 || !Directory.Exists(directory))
            return new PurgeReport(0, 0, 0, Array.Empty<string>());

        var limit = _clock.Now.AddDays(-keepDays);
        var deleted = new List<string>();
        var bytes = 0L;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTime >= limit) continue;
                bytes += info.Length;
                deleted.Add(info.Name);
                if (!dryRun) info.Delete();
            }
            catch (IOException)
            {
                // Fichier verrouillé par une autre application : conservé pour la prochaine purge.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return new PurgeReport(
            deleted.Count == 0 ? 0 : deleted.Distinct().Count(),
            deleted.Count,
            bytes,
            deleted.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Purge complète selon le réglage : fichiers d'échange (messages et exports) puis
    /// journaux. Retourne le détail par répertoire.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, PurgeReport>> PurgeAllAsync(bool dryRun = false,
        CancellationToken ct = default)
        => await PurgeAllAsync(await GetRetentionDaysAsync(ct), dryRun, ct);

    /// <summary>Purge avec une durée de conservation explicite (tâche planifiée, forçage).</summary>
    public async Task<IReadOnlyDictionary<string, PurgeReport>> PurgeAllAsync(int keepDays, bool dryRun = false,
        CancellationToken ct = default)
    {
        var reports = new Dictionary<string, PurgeReport>(StringComparer.OrdinalIgnoreCase);
        if (keepDays <= 0) return reports;

        var root = await _store.GetRootAsync(ct);
        foreach (var sub in new[] { "Messages", "Exports", "Imports" })
        {
            var directory = Path.Combine(root, sub);
            reports[directory] = PurgeDirectory(directory, keepDays, dryRun);
        }

        var logs = Path.Combine(AppContext.BaseDirectory, "logs");
        reports[logs] = PurgeDirectory(logs, keepDays, dryRun);
        return reports;
    }
}
