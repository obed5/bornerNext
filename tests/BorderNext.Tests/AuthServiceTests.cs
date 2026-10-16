using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Security;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Tests d'intégration du service d'authentification (EF Core InMemory).</summary>
public class AuthServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly CurrentUser _currentUser = new();
    private readonly AuditWriter _audit;
    private readonly AuthService _auth;

    public AuthServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid()}")
            .Options);
        _audit = new AuditWriter(_db, _currentUser, new SystemClock());
        _auth = new AuthService(_db, _hasher, _audit, _currentUser, new SystemClock());
        Seed();
    }

    private void Seed()
    {
        _db.UserRoles.Add(new UserRole { Id = 1, Code = "ADMIN" });
        _db.UserRoles.Add(new UserRole { Id = 3, Code = "IMMIG_OFF" });
        _db.Users.Add(new User
        {
            Id = "U001",
            UserName = "jdoe",
            PasswordHash = _hasher.Hash("Secret!2026"),
            UserRoleId = 3
        });
        _db.Users.Add(new User
        {
            Id = "U002",
            UserName = "revoked",
            PasswordHash = _hasher.Hash("Secret!2026"),
            UserRoleId = 3,
            Revoke = true
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Login_ValidCredentials_Succeeds_AndAudits()
    {
        var result = await _auth.LoginAsync("jdoe", "Secret!2026");

        Assert.True(result.Success);
        Assert.Equal("IMMIG_OFF", result.RoleCode);
        Assert.True(_currentUser.IsAuthenticated);
        Assert.Equal("U001", _currentUser.UserId);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.UserLogin && a.EntityId == "U001" && a.Details!.Contains("réussie"));
    }

    [Fact]
    public async Task Login_WrongPassword_Fails_AndAuditsFailure()
    {
        var result = await _auth.LoginAsync("jdoe", "WrongPass");

        Assert.False(result.Success);
        Assert.Equal("Login_InvalidCredentials", result.ErrorCode);
        Assert.False(_currentUser.IsAuthenticated);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.UserLogin && a.Details!.Contains("invalide"));
    }

    [Fact]
    public async Task Login_UnknownUser_Fails()
    {
        var result = await _auth.LoginAsync("ghost", "Secret!2026");

        Assert.False(result.Success);
        Assert.Equal("Login_InvalidCredentials", result.ErrorCode);
    }

    [Fact]
    public async Task Login_RevokedAccount_Fails()
    {
        var result = await _auth.LoginAsync("revoked", "Secret!2026");

        Assert.False(result.Success);
        Assert.Equal("Login_AccountRevoked", result.ErrorCode);
    }

    [Fact]
    public async Task Login_EmptyInputs_FailWithSpecificErrors()
    {
        Assert.Equal("Login_UserNameRequired", (await _auth.LoginAsync("", "x")).ErrorCode);
        Assert.Equal("Login_PasswordRequired", (await _auth.LoginAsync("jdoe", "")).ErrorCode);
    }

    [Fact]
    public async Task Login_SqlInjectionAttempt_TreatedAsLiteral()
    {
        // Le couple "' OR '1'='1 / x ne doit PAS ouvrir de session (requête paramétrée).
        var result = await _auth.LoginAsync("' OR '1'='1", "x' OR '1'='1");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ChangePassword_ValidCurrent_UpdatesHash_AndClearsFlag()
    {
        var user = _db.Users.Single(u => u.Id == "U001");
        user.MustChangePassword = true;
        await _db.SaveChangesAsync();

        var ok = await _auth.ChangePasswordAsync("U001", "Secret!2026", "Nouveau!2026");
        Assert.True(ok);

        user = _db.Users.Single(u => u.Id == "U001");
        Assert.False(user.MustChangePassword);
        Assert.True(_hasher.Verify("Nouveau!2026", user.PasswordHash));
        Assert.False(_hasher.Verify("Secret!2026", user.PasswordHash));
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_ReturnsFalse()
    {
        Assert.False(await _auth.ChangePasswordAsync("U001", "Wrong", "Nouveau!2026"));
    }

    [Fact]
    public async Task Logout_ClearsSession_AndAudits()
    {
        await _auth.LoginAsync("jdoe", "Secret!2026");
        await _auth.LogoutAsync();

        Assert.False(_currentUser.IsAuthenticated);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.UserLogout);
    }

    public void Dispose() => _db.Dispose();
}
