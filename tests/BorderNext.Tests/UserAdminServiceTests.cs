using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Security;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Tests d'administration des comptes et des droits (Lot 1).</summary>
public class UserAdminServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly CurrentUser _currentUser = new();
    private readonly UserAdminService _service;

    public UserAdminServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"users-{Guid.NewGuid()}")
            .Options);
        _service = new UserAdminService(_db, _hasher, _currentUser, new SystemClock(), new AuditWriter(_db, _currentUser, new SystemClock()));
        Seed();
    }

    private void Seed()
    {
        _db.UserRoles.Add(new UserRole { Id = 1, Code = "ADMIN", Cultures = { new UserRoleCulture { Culture = "fr", Description = "Administrateur" } } });
        _db.UserRoles.Add(new UserRole { Id = 3, Code = "IMMIG_OFF", Cultures = { new UserRoleCulture { Culture = "fr", Description = "Officier" } } });
        _db.Users.Add(new User { Id = "ADM001", UserName = "admin", PasswordHash = _hasher.Hash("Secret!2026"), UserRoleId = 1 });
        _db.Users.Add(new User { Id = "U001", UserName = "jdoe", PasswordHash = _hasher.Hash("Secret!2026"), UserRoleId = 3 });
        _db.SaveChanges();

        // Session de l'administrateur qui opère.
        _currentUser.Set(new User { Id = "ADM001", UserName = "admin", UserRoleId = 1 }, "ADMIN");
    }

    [Fact]
    public async Task CreateUser_HashesPassword_ForcesPasswordChange_AndAudits()
    {
        await _service.CreateUserAsync("U002", "mmbala", "Marc", "Mbala", 3, null, "Init!2026");

        var user = await _db.Users.SingleAsync(u => u.Id == "U002");
        Assert.True(_hasher.Verify("Init!2026", user.PasswordHash));
        Assert.True(user.MustChangePassword);
        Assert.False(user.Revoke);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(User) && a.EntityId == "U002");
    }

    [Fact]
    public async Task CreateUser_DuplicateLogin_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateUserAsync("U003", "jdoe", null, null, 3, null, "Init!2026"));
        Assert.Equal("Admin_Error_UserExists", ex.ErrorKey);
    }

    [Fact]
    public async Task CreateUser_ShortPassword_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateUserAsync("U004", "user4", null, null, 3, null, "court"));
        Assert.Equal("Admin_Error_PasswordTooShort", ex.ErrorKey);
    }

    [Fact]
    public async Task ResetPassword_ChangesHash_AndForcesChange()
    {
        _db.Users.Single(u => u.Id == "U001").MustChangePassword = false;
        await _db.SaveChangesAsync();

        await _service.ResetPasswordAsync("U001", "Nouveau!2026");

        var user = await _db.Users.SingleAsync(u => u.Id == "U001");
        Assert.True(_hasher.Verify("Nouveau!2026", user.PasswordHash));
        Assert.True(user.MustChangePassword);
        Assert.Contains(_db.AuditLogs, a => a.EntityId == "U001" && a.Details!.Contains("réinitialisé"));
    }

    [Fact]
    public async Task UpdateUser_ChangesRoleAndBorderPoint()
    {
        _db.BorderPoints.Add(new BorderPoint { Id = 5, ExternalId = "KAS" });
        await _db.SaveChangesAsync();

        await _service.UpdateUserAsync("U001", "Jean", "Doe", 3, 5);

        var user = await _db.Users.SingleAsync(u => u.Id == "U001");
        Assert.Equal("Jean", user.FirstName);
        Assert.Equal((short)5, user.BorderPointId);
    }

    [Fact]
    public async Task RevokeUser_OwnAccount_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.SetUserRevokedAsync("ADM001", true));
        Assert.Equal("Admin_Error_CannotRevokeSelf", ex.ErrorKey);
    }

    [Fact]
    public async Task DemoteLastActiveAdmin_IsRejected()
    {
        // ADM002 administrateur, révoqué ; ADM001 (session) reste le seul admin actif.
        await _service.CreateUserAsync("ADM002", "admin2", null, null, 1, null, "Init!2026");
        await _service.SetUserRevokedAsync("ADM002", true);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.UpdateUserAsync("ADM001", null, null, 3, null));
        Assert.Equal("Admin_Error_LastAdmin", ex.ErrorKey);

        // Avec un second admin actif, la rétrogradation est permise.
        await _service.SetUserRevokedAsync("ADM002", false);
        await _service.UpdateUserAsync("ADM001", "Initial", "Administrator", 3, null);
        Assert.Equal((short)3, (await _db.Users.SingleAsync(u => u.Id == "ADM001")).UserRoleId);
    }

    [Fact]
    public async Task RevokeUser_RegularUser_Succeeds_ThenRestores()
    {
        await _service.SetUserRevokedAsync("U001", true);
        var user = await _db.Users.SingleAsync(u => u.Id == "U001");
        Assert.True(user.Revoke);
        Assert.NotNull(user.RevokeDate);

        await _service.SetUserRevokedAsync("U001", false);
        user = await _db.Users.SingleAsync(u => u.Id == "U001");
        Assert.False(user.Revoke);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowRevoked && a.EntityId == "U001");
    }

    [Fact]
    public async Task GetUsers_SearchAndRevokedFilter()
    {
        Assert.Equal(2, (await _service.GetUsersAsync()).Count);
        Assert.Single(await _service.GetUsersAsync(search: "jdoe"));

        await _service.SetUserRevokedAsync("U001", true);
        Assert.Single(await _service.GetUsersAsync());
        Assert.Equal(2, (await _service.GetUsersAsync(includeRevoked: true)).Count);
    }

    [Fact]
    public async Task GetRoles_ReturnsLocalizedName()
    {
        var roles = await _service.GetRolesAsync();

        Assert.Equal(2, roles.Count);
        Assert.Equal("Administrateur", roles.Single(r => r.Code == "ADMIN").Name);
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>Tests de la matrice des droits et du journal d'audit (Lot 1).</summary>
public class PermissionAndAuditTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly PermissionService _permissions;
    private readonly AuditQueryService _audit;

    public PermissionAndAuditTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"perm-{Guid.NewGuid()}")
            .Options);
        var clock = new SystemClock();
        _permissions = new PermissionService(_db, _currentUser, clock);
        _audit = new AuditQueryService(_db, new AuditWriter(_db, _currentUser, clock), clock);
        Seed();
    }

    private void Seed()
    {
        _db.UserRoles.Add(new UserRole { Id = 3, Code = "IMMIG_OFF" });
        _db.Controls.Add(new Control { Id = 1, Code = "DM_USERS", Cultures = { new ControlCulture { Culture = "fr", Description = "Utilisateurs" } } });
        _db.Controls.Add(new Control { Id = 2, Code = "DM_REP", Cultures = { new ControlCulture { Culture = "fr", Description = "Rapports" } } });
        _db.BorderPoints.Add(new BorderPoint { Id = 5, ExternalId = "KAS" });
        _db.ControlsToRoles.Add(new ControlToRole { UserRoleId = 3, ControlId = 1, Permission = true, Visibility = true });
        _db.SaveChanges();

        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3 }, "IMMIG_OFF");
    }

    [Fact]
    public async Task Can_TrueWhenGranted_FalseOtherwise()
    {
        Assert.True(await _permissions.CanAsync("DM_USERS"));
        Assert.False(await _permissions.CanAsync("DM_REP"));
        Assert.False(await _permissions.CanAsync("UNKNOWN_CONTROL"));
    }

    [Fact]
    public async Task BorderPointScopedRight_OverridesGlobal()
    {
        // Droit global refusé, droit pour le poste de l'utilisateur accordé.
        _db.ControlsToRoles.Add(new ControlToRole { UserRoleId = 3, ControlId = 2, Permission = false, BorderPointId = null });
        _db.SaveChanges();
        Assert.False(await _permissions.CanAsync("DM_REP"));

        var user = new User { Id = "U002", UserName = "kasa", UserRoleId = 3, BorderPointId = 5 };
        _currentUser.Set(user, "IMMIG_OFF");
        _db.ControlsToRoles.Add(new ControlToRole { UserRoleId = 3, ControlId = 2, Permission = true, BorderPointId = 5 });
        await _db.SaveChangesAsync();

        Assert.True(await _permissions.CanAsync("DM_REP"));
    }

    [Fact]
    public async Task SetRoleRight_CreatesThenUpdates_GlobalMapping()
    {
        await _permissions.SetRoleRightAsync(3, 2, permission: true, visibility: true);
        Assert.True(await _permissions.CanAsync("DM_REP"));

        await _permissions.SetRoleRightAsync(3, 2, permission: false, visibility: true);
        Assert.False(await _permissions.CanAsync("DM_REP"));
        Assert.Single(_db.ControlsToRoles.Where(m => m.UserRoleId == 3 && m.ControlId == 2 && m.BorderPointId == null));
    }

    [Fact]
    public async Task GetRoleRights_ReturnsAllControlsWithFlags()
    {
        var rights = await _permissions.GetRoleRightsAsync(3);

        Assert.Equal(2, rights.Count);
        var users = rights.Single(r => r.Code == "DM_USERS");
        Assert.True(users.Permission);
        Assert.False(rights.Single(r => r.Code == "DM_REP").Permission);
    }

    [Fact]
    public async Task AuditSearch_FiltersByUserEventAndPeriod()
    {
        var writer = new AuditWriter(_db, _currentUser, new SystemClock());
        await writer.WriteAsync(AuditEventTypes.UserLogin, nameof(User), "U001", "connexion");
        await writer.WriteAsync(AuditEventTypes.RowAdded, nameof(Lookup), "10", "ajout");
        _db.AuditLogs.Add(new AuditLog { EventType = AuditEventTypes.RowAdded, UserId = "U999", Entity = nameof(Lookup), EntityId = "11", EventTime = DateTime.Now });
        await _db.SaveChangesAsync();

        Assert.Equal(3, (await _audit.SearchAsync()).Count);
        Assert.Equal(2, (await _audit.SearchAsync(eventType: AuditEventTypes.RowAdded)).Count);
        Assert.Single(await _audit.SearchAsync(userId: "U999"));
        Assert.Empty(await _audit.SearchAsync(from: DateTime.Now.AddDays(1)));
        Assert.Contains(AuditEventTypes.UserLogin, await _audit.GetEventTypesAsync());
    }

    [Fact]
    public async Task AuditExport_WritesCsv_AndAuditsExport()
    {
        var writer = new AuditWriter(_db, _currentUser, new SystemClock());
        await writer.WriteAsync(AuditEventTypes.UserLogin, nameof(User), "U001", "détail;avec séparateur");

        var rows = await _audit.SearchAsync();
        var csv = await _audit.ExportCsvAsync(rows);

        Assert.StartsWith("Id;DateHeure;Evenement;Utilisateur;Machine;Poste;Entite;EntiteId;Detail", csv);
        Assert.Contains("détail,avec séparateur", csv); // séparateur neutralisé
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.DataExported);
    }

    public void Dispose() => _db.Dispose();
}
