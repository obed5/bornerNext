using BorderNext.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>
/// Configuration du poste de travail (héritage : réglages « BorderPoint », « Terminal »
/// et « MovementDirection »). Les mouvements et voyages prennent le poste de l'opérateur
/// connecté, à défaut celui configuré pour le poste de travail — comportement hérité :
/// la configuration du poste primait sur le compte.
/// </summary>
public static class StationSettings
{
    /// <summary>Poste frontière : opérateur, puis réglage « BorderPoint ».</summary>
    public static async Task<short?> GetBorderPointIdAsync(DbContext db, ICurrentUser currentUser, CancellationToken ct = default)
    {
        if (currentUser.BorderPointId is { } userStation) return userStation;
        var value = await db.Set<Domain.Entities.Setting>().AsNoTracking()
            .Where(s => s.Type == "BorderPoint" && s.Value != null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return value is { } v and >= short.MinValue and <= short.MaxValue ? (short)v : null;
    }

    /// <summary>Terminal du poste (réglage hérité « Terminal »).</summary>
    public static async Task<int?> GetTerminalIdAsync(DbContext db, CancellationToken ct = default)
        => await db.Set<Domain.Entities.Setting>().AsNoTracking()
            .Where(s => s.Type == "Terminal" && s.Value != null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
}
