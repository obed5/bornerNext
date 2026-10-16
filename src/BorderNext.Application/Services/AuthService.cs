using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Service d'authentification — requêtes paramétrées, hachage PBKDF2, audit.</summary>
public class AuthService
{
    private readonly DbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public AuthService(DbContext db, IPasswordHasher hasher, IAuditWriter audit, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        userName = (userName ?? string.Empty).Trim();
        if (userName.Length == 0) return LoginResult.Fail("Login_UserNameRequired");
        if (string.IsNullOrEmpty(password)) return LoginResult.Fail("Login_PasswordRequired");

        // Requête paramétrée (EF Core) — aucune concaténation SQL.
        var user = await _db.Set<User>()
            .Include(u => u.UserRole)
            .SingleOrDefaultAsync(u => u.UserName == userName, ct);

        if (user is null)
        {
            await _audit.WriteAsync(AuditEventTypes.UserLogin, nameof(User), null, $"Échec: utilisateur inconnu '{userName}'", ct);
            return LoginResult.Fail("Login_InvalidCredentials");
        }

        if (user.Revoke)
        {
            await _audit.WriteAsync(AuditEventTypes.UserLogin, nameof(User), user.Id, "Échec: compte révoqué", ct);
            return LoginResult.Fail("Login_AccountRevoked");
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            await _audit.WriteAsync(AuditEventTypes.UserLogin, nameof(User), user.Id, "Échec: mot de passe invalide", ct);
            return LoginResult.Fail("Login_InvalidCredentials");
        }

        return await CompleteLoginAsync(user, "mot de passe", ct);
    }

    /// <summary>
    /// Connexion après identification biométrique (héritage :
    /// UserLoginForm.CheckFingerAgainstUsers). Seuls les comptes actifs sont
    /// acceptés ; l'événement est audité avec la méthode d'authentification.
    /// </summary>
    public async Task<LoginResult> LoginWithBiometricsAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Set<User>().Include(u => u.UserRole)
            .SingleOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            await _audit.WriteAsync(AuditEventTypes.UserLogin, nameof(User), userId, "Échec biométrie: utilisateur inconnu", ct);
            return LoginResult.Fail("Login_InvalidCredentials");
        }

        if (user.Revoke)
        {
            await _audit.WriteAsync(AuditEventTypes.UserLogin, nameof(User), user.Id, "Échec biométrie: compte révoqué", ct);
            return LoginResult.Fail("Login_AccountRevoked");
        }

        return await CompleteLoginAsync(user, "empreinte digitale", ct);
    }

    private async Task<LoginResult> CompleteLoginAsync(User user, string method, CancellationToken ct)
    {
        var roleCode = user.UserRole?.Code ?? "UNKNOWN";
        user.LastLoginDate = _clock.Now;
        await _db.SaveChangesAsync(ct);

        _currentUser.Set(user, roleCode);
        await _audit.WriteAsync(AuditEventTypes.UserLogin, nameof(User), user.Id, $"Connexion réussie ({roleCode}, {method})", ct);
        return LoginResult.Ok(user, roleCode);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (_currentUser.IsAuthenticated)
            await _audit.WriteAsync(AuditEventTypes.UserLogout, nameof(User), _currentUser.UserId, "Déconnexion", ct);
        _currentUser.Clear();
    }

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !_hasher.Verify(currentPassword, user.PasswordHash)) return false;

        user.PasswordHash = _hasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.UpdateDate = _clock.Now;
        user.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(User), user.Id, "Mot de passe modifié", ct);
        return true;
    }
}
