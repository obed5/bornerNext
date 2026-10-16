using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Droit d'un rôle sur un contrôle (matrice des droits).</summary>
public sealed record ControlRightsDto(int ControlId, string Code, string Fr, string En, bool Permission, bool Visibility);

/// <summary>
/// Contrôle d'accès par rôle (héritage : ControlsToRoles).
/// Un droit peut être défini globalement (BorderPointId null) ou pour un poste
/// frontière précis, qui prime alors sur le droit global.
/// </summary>
public class PermissionService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public PermissionService(DbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>Le rôle courant peut-il exécuter le contrôle indiqué ?</summary>
    public Task<bool> CanAsync(string controlCode, CancellationToken ct = default)
        => _currentUser.RoleId is { } roleId ? CanAsync(roleId, controlCode, ct) : Task.FromResult(false);

    public async Task<bool> CanAsync(short roleId, string controlCode, CancellationToken ct = default)
        => await FindRightAsync(roleId, controlCode, ct) is { Permission: true };

    /// <summary>Le contrôle doit-il être visible pour le rôle courant ?</summary>
    public async Task<bool> IsVisibleAsync(string controlCode, CancellationToken ct = default)
    {
        if (_currentUser.RoleId is not { } roleId) return false;
        return await FindRightAsync(roleId, controlCode, ct) is { Visibility: true };
    }

    /// <summary>Matrice des droits d'un rôle (contrôles actifs, droit global).</summary>
    public async Task<IReadOnlyList<ControlRightsDto>> GetRoleRightsAsync(short roleId, CancellationToken ct = default)
    {
        var controls = await _db.Set<Control>().Include(c => c.Cultures).Where(c => !c.Revoke).ToListAsync(ct);
        var mappings = await _db.Set<ControlToRole>()
            .Where(m => m.UserRoleId == roleId && m.BorderPointId == null)
            .ToDictionaryAsync(m => m.ControlId, ct);

        return controls
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
            .Select(c => new ControlRightsDto(c.Id, c.Code,
                c.Cultures.FirstOrDefault(x => x.Culture == "fr")?.Description ?? c.Code,
                c.Cultures.FirstOrDefault(x => x.Culture == "en")?.Description ?? c.Code,
                mappings.TryGetValue(c.Id, out var m) && m.Permission,
                mappings.TryGetValue(c.Id, out var v) && v.Visibility))
            .ToList();
    }

    /// <summary>Définit (crée ou met à jour) le droit global d'un rôle sur un contrôle.</summary>
    public async Task SetRoleRightAsync(short roleId, int controlId, bool permission, bool visibility, CancellationToken ct = default)
    {
        var mapping = await _db.Set<ControlToRole>()
            .SingleOrDefaultAsync(m => m.UserRoleId == roleId && m.ControlId == controlId && m.BorderPointId == null, ct);

        if (mapping is null)
        {
            mapping = new ControlToRole { UserRoleId = roleId, ControlId = controlId };
            _db.Set<ControlToRole>().Add(mapping);
        }
        mapping.Permission = permission;
        mapping.Visibility = visibility;
        mapping.UpdateDate = _clock.Now;
        mapping.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ControlToRole?> FindRightAsync(short roleId, string controlCode, CancellationToken ct)
    {
        var controlIds = await _db.Set<Control>()
            .Where(c => c.Code == controlCode && !c.Revoke)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (controlIds.Count == 0) return null;

        var mappings = await _db.Set<ControlToRole>()
            .Where(m => m.UserRoleId == roleId && controlIds.Contains(m.ControlId))
            .ToListAsync(ct);

        // Le droit défini pour le poste frontière de l'utilisateur prime sur le droit global.
        var scoped = mappings.FirstOrDefault(m => m.BorderPointId != null && m.BorderPointId == _currentUser.BorderPointId);
        return scoped ?? mappings.FirstOrDefault(m => m.BorderPointId == null);
    }
}
