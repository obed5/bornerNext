using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Vue d'un rôle pour les listes déroulantes.</summary>
public sealed record RoleDto(short Id, string Code, string Name);

/// <summary>Vue d'un utilisateur dans l'administration.</summary>
public sealed record UserListItem(string Id, string UserName, string? FirstName, string? LastName,
    short RoleId, string RoleCode, short? BorderPointId, string? BorderPointCode,
    bool Revoke, bool MustChangePassword, DateTime? LastLoginDate);

/// <summary>
/// Administration des comptes (héritage : Users) : création, modification,
/// réinitialisation de mot de passe et révocation — avec garde-fous
/// (pas d'auto-révocation, dernier administrateur protégé) et audit.
/// </summary>
public class UserAdminService
{
    /// <summary>Politique de mot de passe héritée conservée : 8 caractères minimum.</summary>
    public const int MinimumPasswordLength = 8;

    private readonly DbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public UserAdminService(DbContext db, IPasswordHasher hasher, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _hasher = hasher;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default)
    {
        var roles = await _db.Set<UserRole>().Include(r => r.Cultures).Where(r => !r.Revoke).ToListAsync(ct);
        return roles.OrderBy(r => r.SortOrder).ThenBy(r => r.Code)
            .Select(r => new RoleDto(r.Id, r.Code,
                r.Cultures.FirstOrDefault(c => c.Culture == "fr")?.Description
                ?? r.Cultures.FirstOrDefault()?.Description ?? r.Code))
            .ToList();
    }

    public async Task<IReadOnlyList<UserListItem>> GetUsersAsync(bool includeRevoked = false, string? search = null, CancellationToken ct = default)
    {
        var users = await _db.Set<User>()
            .Include(u => u.UserRole).Include(u => u.BorderPoint)
            .Where(u => includeRevoked || !u.Revoke)
            .ToListAsync(ct);

        return users
            .Where(u => string.IsNullOrWhiteSpace(search)
                        || u.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || u.UserName.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (u.LastName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (u.FirstName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(u => u.Id)
            .Select(u => new UserListItem(u.Id, u.UserName, u.FirstName, u.LastName,
                u.UserRoleId, u.UserRole?.Code ?? "?", u.BorderPointId, u.BorderPoint?.ExternalId,
                u.Revoke, u.MustChangePassword, u.LastLoginDate))
            .ToList();
    }

    public async Task CreateUserAsync(string id, string userName, string? firstName, string? lastName,
        short roleId, short? borderPointId, string initialPassword, CancellationToken ct = default)
    {
        id = (id ?? string.Empty).Trim();
        userName = (userName ?? string.Empty).Trim();
        if (id.Length == 0 || userName.Length == 0) throw new DomainValidationException("Admin_Error_UserIdRequired");
        if (initialPassword is null || initialPassword.Length < MinimumPasswordLength)
            throw new DomainValidationException("Admin_Error_PasswordTooShort");
        if (await _db.Set<User>().AnyAsync(u => u.Id == id || u.UserName == userName, ct))
            throw new DomainValidationException("Admin_Error_UserExists", id);
        if (!await _db.Set<UserRole>().AnyAsync(r => r.Id == roleId, ct))
            throw new DomainValidationException("Admin_Error_ItemNotFound", roleId.ToString());

        var user = new User
        {
            Id = id,
            UserName = userName,
            FirstName = Clean(firstName),
            LastName = Clean(lastName),
            UserRoleId = roleId,
            BorderPointId = borderPointId,
            PasswordHash = _hasher.Hash(initialPassword),
            MustChangePassword = true, // héritage : mot de passe à changer à la 1re connexion
            UpdateDate = _clock.Now,
            UpdateUser = _currentUser.UserId
        };
        _db.Set<User>().Add(user);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(User), user.Id, $"Création « {userName} » (rôle {roleId})", ct);
    }

    public async Task UpdateUserAsync(string id, string? firstName, string? lastName, short roleId, short? borderPointId, CancellationToken ct = default)
    {
        var user = await _db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id);
        if (!await _db.Set<UserRole>().AnyAsync(r => r.Id == roleId, ct))
            throw new DomainValidationException("Admin_Error_ItemNotFound", roleId.ToString());

        // Protège le dernier administrateur actif contre une rétrogradation de rôle.
        if (roleId != user.UserRoleId && await IsLastActiveAdminAsync(user, ct))
            throw new DomainValidationException("Admin_Error_LastAdmin");

        user.FirstName = Clean(firstName);
        user.LastName = Clean(lastName);
        user.UserRoleId = roleId;
        user.BorderPointId = borderPointId;
        user.UpdateDate = _clock.Now;
        user.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(User), user.Id, $"Modification (rôle {roleId})", ct);
    }

    public async Task ResetPasswordAsync(string id, string newPassword, CancellationToken ct = default)
    {
        if (newPassword is null || newPassword.Length < MinimumPasswordLength)
            throw new DomainValidationException("Admin_Error_PasswordTooShort");

        var user = await _db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id);

        user.PasswordHash = _hasher.Hash(newPassword);
        user.MustChangePassword = true;
        user.UpdateDate = _clock.Now;
        user.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(User), user.Id, "Mot de passe réinitialisé par l'administrateur", ct);
    }

    public async Task SetUserRevokedAsync(string id, bool revoke, CancellationToken ct = default)
    {
        var user = await _db.Set<User>().SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id);

        if (revoke && user.Id == _currentUser.UserId)
            throw new DomainValidationException("Admin_Error_CannotRevokeSelf");
        if (revoke && await IsLastActiveAdminAsync(user, ct))
            throw new DomainValidationException("Admin_Error_LastAdmin");

        user.Revoke = revoke;
        user.RevokeDate = revoke ? _clock.Now : null;
        user.UpdateDate = _clock.Now;
        user.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(User), user.Id,
            revoke ? "Compte révoqué" : "Compte réactivé", ct);
    }

    private async Task<bool> IsLastActiveAdminAsync(User candidate, CancellationToken ct)
    {
        var adminRoleIds = await _db.Set<UserRole>().Where(r => r.Code == "ADMIN").Select(r => r.Id).ToListAsync(ct);
        if (!adminRoleIds.Contains(candidate.UserRoleId)) return false;
        return !await _db.Set<User>().AnyAsync(u => u.Id != candidate.Id && !u.Revoke && adminRoleIds.Contains(u.UserRoleId), ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
