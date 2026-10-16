using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Security;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>
/// Tests du durcissement (Lot 9) : verrouillage des comptes après échecs répétés,
/// protection des secrets (DPAPI), mot de passe d'amorçage, rétention des fichiers.
/// </summary>
public class HardeningTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly CurrentUser _currentUser = new();
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 16, 9, 0, 0));
    private readonly AuditWriter _audit;
    private readonly AuthService _auth;
    private readonly SettingsService _settings;

    public HardeningTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"hard-{Guid.NewGuid()}").Options);
        _audit = new AuditWriter(_db, _currentUser, _clock);
        _auth = new AuthService(_db, _hasher, _audit, _currentUser, _clock);
        _settings = new SettingsService(_db, _currentUser, _clock, _audit, new DpapiSecretProtector());
        Seed();
    }

    private void Seed()
    {
        _db.UserRoles.Add(new UserRole { Id = 1, Code = "ADMIN" });
        _db.Users.Add(new User
        {
            Id = "U001",
            UserName = "jdoe",
            PasswordHash = _hasher.Hash("Secret!2026"),
            UserRoleId = 1,
            FirstFingerprint = new byte[] { 1, 2, 3 },
        });
        _db.SaveChanges();
    }

    private User User => _db.Users.Single(u => u.Id == "U001");

    // ---- Verrouillage après échecs --------------------------------------------

    [Fact]
    public async Task Login_FiveFailures_LocksAccount_AndAudits()
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var failure = await _auth.LoginAsync("jdoe", "mauvais");
            Assert.False(failure.Success);
            Assert.Equal("Login_Error_InvalidCredentials", failure.ErrorCode);
        }

        var fifth = await _auth.LoginAsync("jdoe", "mauvais");
        Assert.Equal("Login_Error_AccountLocked", fifth.ErrorCode);
        Assert.NotNull(User.LockoutUntil);
        Assert.Equal(0, User.FailedAttemptCount);
        Assert.Contains(_db.AuditLogs, a => a.Details!.Contains("compte verrouillé"));

        // Même le bon mot de passe est refusé pendant le verrouillage.
        var locked = await _auth.LoginAsync("jdoe", "Secret!2026");
        Assert.Equal("Login_Error_AccountLocked", locked.ErrorCode);
    }

    [Fact]
    public async Task Login_Success_ResetsFailureCounter()
    {
        await _auth.LoginAsync("jdoe", "mauvais");
        await _auth.LoginAsync("jdoe", "mauvais");
        Assert.Equal(2, User.FailedAttemptCount);

        var ok = await _auth.LoginAsync("jdoe", "Secret!2026");
        Assert.True(ok.Success);
        Assert.Equal(0, User.FailedAttemptCount);
        Assert.Null(User.LockoutUntil);
    }

    [Fact]
    public async Task Login_LockedAccount_RefusesBiometrics()
    {
        for (var attempt = 0; attempt < 5; attempt++) await _auth.LoginAsync("jdoe", "mauvais");

        var biometrics = await _auth.LoginWithBiometricsAsync("U001");
        Assert.False(biometrics.Success);
        Assert.Equal("Login_Error_AccountLocked", biometrics.ErrorCode);
    }

    [Fact]
    public async Task Login_Threshold_IsConfigurable()
    {
        _db.Settings.Add(new Setting { Type = "Security_MaxFailedAttempts", Value = 3 });
        await _db.SaveChangesAsync();

        await _auth.LoginAsync("jdoe", "mauvais");
        await _auth.LoginAsync("jdoe", "mauvais");
        var third = await _auth.LoginAsync("jdoe", "mauvais");

        Assert.Equal("Login_Error_AccountLocked", third.ErrorCode);
    }

    [Fact]
    public async Task Login_AfterLockoutExpiry_IsAllowedAgain()
    {
        for (var attempt = 0; attempt < 5; attempt++) await _auth.LoginAsync("jdoe", "mauvais");
        Assert.NotNull(User.LockoutUntil);

        // Le délai est dépassé (compte dont le verrouillage est échu).
        User.LockoutUntil = _clock.Now.AddMinutes(-1);
        await _db.SaveChangesAsync();

        var ok = await _auth.LoginAsync("jdoe", "Secret!2026");
        Assert.True(ok.Success);
        Assert.Null(User.LockoutUntil);
    }

    // ---- Protection des secrets ------------------------------------------------

    [Fact]
    public void SecretProtector_RoundTrips_WithoutPlainText()
    {
        var protector = new DpapiSecretProtector();
        const string secret = "jeton-partenaire-42";

        var protectedBytes = protector.Protect(secret);
        var text = System.Text.Encoding.ASCII.GetString(protectedBytes);

        Assert.DoesNotContain(secret, text);
        Assert.Equal(secret, protector.Unprotect(protectedBytes));
    }

    [Theory]
    [InlineData("Exchange_Connector_AIR_Secret", true)]
    [InlineData("MINDPassword", true)]
    [InlineData("Simiec_Token", true)]
    [InlineData("Belize_ApiKey", true)]
    [InlineData("BorderPoint", false)]
    [InlineData("Security_MaxFailedAttempts", false)]
    public void IsSecretKey_DetectsSecretConventions(string key, bool expected)
        => Assert.Equal(expected, SettingsService.IsSecretKey(key));

    [Fact]
    public async Task Settings_Secret_IsEncrypted_AndNotListed()
    {
        await _settings.SetSecretAsync("Exchange_Connector_AIR_Secret", "jeton-secret");

        var row = _db.Settings.Single(s => s.Type == "Exchange_Connector_AIR_Secret");
        Assert.NotNull(row.EncryptedValue);
        Assert.Null(row.StringValue);
        Assert.DoesNotContain("jeton-secret", System.Text.Encoding.ASCII.GetString(row.EncryptedValue!));

        Assert.True(await _settings.HasSecretAsync("Exchange_Connector_AIR_Secret"));
        Assert.Equal("jeton-secret", await _settings.GetSecretAsync("Exchange_Connector_AIR_Secret"));

        // La vue d'administration n'expose jamais la valeur.
        var listed = await _settings.ListAsync("Exchange_Connector_AIR_Secret");
        Assert.All(listed, s => Assert.True(s.StringValue is null));

        // Effacement.
        await _settings.SetSecretAsync("Exchange_Connector_AIR_Secret", string.Empty);
        Assert.False(await _settings.HasSecretAsync("Exchange_Connector_AIR_Secret"));
        Assert.Null(await _settings.GetSecretAsync("Exchange_Connector_AIR_Secret"));
    }

    [Fact]
    public async Task Connector_Secret_IsSentAsBearerToken()
    {
        // Un connecteur en mode direct reçoit le jeton porteur du réglage protégé.
        await _settings.SetSecretAsync(ConnectorSettings.SecretSettingKey(ExchangeChannels.Air), "jeton-partenaire");

        var task = new ExchangeTask
        {
            Id = 1,
            Name = "Système aérien",
            Channel = ExchangeChannels.Air,
            Direction = ExchangeDirections.Check,
            IsActive = true,
            Endpoint = "https://partenaire.test/check",
            Parameters = "MODE=LIVE",
        };
        _db.ExchangeTasks.Add(task);
        await _db.SaveChangesAsync();

        string? seenAuthorization = null;
        var handler = new StubHandler(request =>
        {
            seenAuthorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"verdict\":\"clear\",\"reference\":\"R1\",\"summary\":\"ok\",\"details\":[]}"),
            };
        });

        var audit = new AuditWriter(_db, _currentUser, _clock);
        var journal = new ExchangeJournal(_db, _currentUser, _clock, audit);
        var service = new ExternalCheckService(_db, _currentUser, _clock, audit, journal,
            new IExternalCheckConnector[] { new AirConnector(_db, () => new HttpClient(handler)) },
            new DpapiSecretProtector());

        var outcome = await service.CheckAsync(ExchangeChannels.Air,
            new ExternalCheckRequest("OP123456", "MARTIN", "CLAIRE"));

        Assert.Equal(ExternalCheckVerdicts.Clear, outcome.Verdict);
        Assert.Equal("Bearer jeton-partenaire", seenAuthorization);
    }

    // ---- Mot de passe d'amorçage ------------------------------------------------

    [Fact]
    public void Bootstrap_PrefersConfiguredValue()
    {
        var store = new BootstrapSecretStore(new DpapiSecretProtector(),
            Path.Combine(Path.GetTempPath(), $"bn-boot-{Guid.NewGuid():N}"));

        var password = store.Resolve("Configure!2026");

        Assert.Equal("Configure!2026", password);
        Assert.False(File.Exists(store.SecretFilePath));
    }

    [Fact]
    public void Bootstrap_UsesEnvironmentVariable()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bn-boot-{Guid.NewGuid():N}");
        var store = new BootstrapSecretStore(new DpapiSecretProtector(), directory);
        var previous = Environment.GetEnvironmentVariable(BootstrapSecretStore.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(BootstrapSecretStore.EnvironmentVariable, "Env!2026");
            Assert.Equal("Env!2026", store.Resolve(null));
            Assert.False(File.Exists(store.SecretFilePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(BootstrapSecretStore.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Bootstrap_GeneratesProtectedSecret_WhenNothingProvided()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bn-boot-{Guid.NewGuid():N}");
        var protector = new DpapiSecretProtector();
        var store = new BootstrapSecretStore(protector, directory);
        var previous = Environment.GetEnvironmentVariable(BootstrapSecretStore.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(BootstrapSecretStore.EnvironmentVariable, null);

            var password = store.Resolve(null);

            Assert.NotNull(password);
            Assert.Equal(20, password!.Length);
            Assert.True(File.Exists(store.SecretFilePath));
            Assert.True(File.Exists(store.NoticeFilePath));

            // Le mot de passe stocké n'apparaît pas en clair dans le fichier protégé.
            var stored = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(store.SecretFilePath));
            Assert.DoesNotContain(password!, stored);

            // Le dépôt de communication (à supprimer après la 1re connexion) le contient, lui.
            Assert.Contains(password!, File.ReadAllText(store.NoticeFilePath));

            // Deuxième démarrage : le même mot de passe est relu, aucun nouveau fichier.
            Assert.Equal(password, store.Resolve(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(BootstrapSecretStore.EnvironmentVariable, previous);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    // ---- Rétention des fichiers -------------------------------------------------

    [Fact]
    public async Task Retention_PurgesOldFiles_KeepsRecent_AndHonoursDryRun()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bn-purge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Messages"));
        var old = Path.Combine(directory, "Messages", "vieux.edi");
        var recent = Path.Combine(directory, "Messages", "recent.edi");
        await File.WriteAllTextAsync(old, "UNB ancien");
        await File.WriteAllTextAsync(recent, "UNB récent");
        File.SetLastWriteTime(old, _clock.Now.AddDays(-400));
        File.SetLastWriteTime(recent, _clock.Now.AddDays(-2));

        var store = new ExchangeFileStore(_db, _clock);
        var service = new FileRetentionService(_db, store, _clock);

        var dryRun = service.PurgeDirectory(directory, keepDays: 365, dryRun: true);
        Assert.NotNull(dryRun);
        Assert.Equal(1, dryRun.FileCount);
        Assert.True(File.Exists(old));

        var report = service.PurgeDirectory(directory, keepDays: 365);
        Assert.Equal(1, report.FileCount);
        Assert.True(report.BytesFreed > 0);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task Retention_ZeroDays_KeepsEverything()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bn-purge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "archive.edi");
        await File.WriteAllTextAsync(file, "contenu");
        File.SetLastWriteTime(file, _clock.Now.AddYears(-2));

        var service = new FileRetentionService(_db, new ExchangeFileStore(_db, _clock), _clock);
        var reports = await service.PurgeAllAsync();

        Assert.Empty(reports);
        Assert.True(File.Exists(file));
        Directory.Delete(directory, recursive: true);
    }

    /// <summary>Horloge fixe (les délais de verrouillage et de rétention sont déterministes).</summary>
    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime Now { get; } = now;
    }

    /// <summary>Gestionnaire HTTP de test : capture la requête et retourne une réponse fixée.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    public void Dispose() => _db.Dispose();
}
