using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Devices;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BorderNext.Tests;

/// <summary>Tests des simulateurs d'appareils (Lot 3) — sans base de données.</summary>
public class SimulatedDevicesTests
{
    [Fact]
    public async Task Camera_Capture_ReturnsGrayscaleBmp()
    {
        var camera = new SimulatedCameraDevice();

        var bytes = await camera.CaptureAsync();

        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        Assert.True(bytes.Length > 240 * 320); // en-tête + palette + pixels
        Assert.True(camera.IsSimulated);
    }

    [Fact]
    public async Task Camera_SuccessiveCaptures_DifferSlightly()
    {
        var camera = new SimulatedCameraDevice();

        var first = await camera.CaptureAsync();
        var second = await camera.CaptureAsync();

        Assert.False(first.SequenceEqual(second));
    }

    [Fact]
    public async Task Fingerprint_SameIdentityAndFinger_ProducesSameTemplate()
    {
        var scanner = new SimulatedFingerprintScanner { SelectedIdentity = "SIM-1 (démonstration)" };

        var first = await scanner.CaptureAsync(7);
        var second = await scanner.CaptureAsync(7);

        Assert.Equal(first.Template, second.Template);
        Assert.True(SimulatedTemplate.IsValid(first.Template));
        Assert.InRange(first.Quality, 70, 99);
        Assert.Equal((short)7, first.FingerNumber);
        Assert.True(first.Image.Length > 320 * 320 / 2);
    }

    [Fact]
    public async Task Fingerprint_DifferentFingerOrIdentity_ProducesDifferentTemplate()
    {
        var scanner = new SimulatedFingerprintScanner { SelectedIdentity = "SIM-1 (démonstration)" };

        var index = await scanner.CaptureAsync(7);
        var thumb = await scanner.CaptureAsync(6);
        Assert.NotEqual(index.Template, thumb.Template);
        Assert.False(new SimulatedFingerprintMatcher().IsMatch(index.Template, thumb.Template));

        scanner.SelectedIdentity = "SIM-2";
        var otherPerson = await scanner.CaptureAsync(7);
        Assert.False(new SimulatedFingerprintMatcher().IsMatch(index.Template, otherPerson.Template));
    }

    [Fact]
    public async Task Matcher_IdenticalTemplates_Match_AtFullScore()
    {
        var scanner = new SimulatedFingerprintScanner { SelectedIdentity = "SIM-3" };
        var capture = await scanner.CaptureAsync(1);
        var matcher = new SimulatedFingerprintMatcher();

        Assert.Equal(100, matcher.Score(capture.Template, capture.Template));
        Assert.True(matcher.IsMatch(capture.Template, capture.Template));
    }

    [Fact]
    public void Matcher_MalformedTemplate_ScoresZero()
    {
        var matcher = new SimulatedFingerprintMatcher();

        Assert.Equal(0, matcher.Score(new byte[64], new byte[64]));
        Assert.Equal(0, matcher.Score(new byte[10], new byte[64]));
        Assert.False(matcher.IsMatch(new byte[64], new byte[64]));
    }

    [Fact]
    public async Task DocumentReader_ReadsSpecimen_WithAllFieldsAndTd3Mrz()
    {
        var reader = new SimulatedDocumentReader { SelectedIdentity = "Passeport — MBALA Jean (RDC)" };

        var result = await reader.ReadAsync();

        Assert.True(result.Success);
        Assert.Equal("OP", result.DocumentTypeCode);
        Assert.Equal("Mbala", result.Surname);
        Assert.Equal("COD", result.NationalityAlpha3);
        Assert.Equal(new DateTime(1990, 5, 20), result.BirthDate);
        Assert.Equal("M", result.SexCode);

        var lines = result.Mrz!.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.Equal(44, l.Length));
        Assert.StartsWith("P<CODMBALA<<JEAN<PIERRE", lines[0]);
        Assert.Contains("OP0321458", lines[1]);
    }

    [Fact]
    public async Task DocumentReader_MrzCheckDigits_AreIcaoValid()
    {
        var reader = new SimulatedDocumentReader { SelectedIdentity = "Passeport — MARTIN Claire (France)" };
        var result = await reader.ReadAsync();
        var line2 = result.Mrz!.Split('\n')[1];

        // Recalcule les clés de contrôle TD3 : n° document, naissance, expiration, composite.
        Assert.Equal(SimulatedDocumentReader.CheckDigit(line2[..9]), line2[9]);
        Assert.Equal(SimulatedDocumentReader.CheckDigit(line2[13..19]), line2[19]);
        Assert.Equal(SimulatedDocumentReader.CheckDigit(line2[21..27]), line2[27]);
        Assert.Equal(SimulatedDocumentReader.CheckDigit(line2[28..42]), line2[42]); // clé du n° personnel
        var composite = line2[..10] + line2[13..20] + line2[21..28] + line2[28..42];
        Assert.Equal(SimulatedDocumentReader.CheckDigit(composite), line2[43]);     // clé finale composite
    }

    [Fact]
    public async Task DocumentReader_UnreadableSpecimen_ReturnsErrorCode()
    {
        var reader = new SimulatedDocumentReader { SelectedIdentity = "Document illisible (test du rejet)" };

        var result = await reader.ReadAsync();

        Assert.False(result.Success);
        Assert.Equal("DEVICE_Error_Unreadable", result.ErrorCode);
        Assert.Null(result.Mrz);
    }

    [Fact]
    public async Task DocumentReader_RotatesSpecimensWhenNoSelection()
    {
        var reader = new SimulatedDocumentReader();

        var first = await reader.ReadAsync();
        var second = await reader.ReadAsync();

        Assert.True(first.Success || second.Success);
        Assert.NotEqual(first.DocumentNumber, second.DocumentNumber);
    }
}

/// <summary>Tests du registre d'appareils (réglage hérité DocumentReader).</summary>
public class DeviceRegistryTests : IDisposable
{
    private readonly BorderNextDbContext _db;

    public DeviceRegistryTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"devices-{Guid.NewGuid()}").Options);
    }

    private DeviceRegistry CreateRegistry()
        => new(_db, NullLogger<DeviceRegistry>.Instance);

    [Fact]
    public void NoSetting_UsesSimulatorWithExplicitNote()
    {
        var registry = CreateRegistry();

        var status = registry.GetStatus().Single(s => s.Role == "DocumentReader");
        Assert.True(status.IsSimulated);
        Assert.Null(status.ConfiguredType);
        Assert.Contains("Aucun lecteur", status.Note);
    }

    [Fact]
    public void ConfiguredHardwareReader_FallsBackToSimulatorWithWarning()
    {
        _db.Settings.Add(new Setting { Type = "DocumentReader", Description = "ARH", MachineName = Environment.MachineName });
        _db.SaveChanges();

        var registry = CreateRegistry();

        var status = registry.GetStatus().Single(s => s.Role == "DocumentReader");
        Assert.True(status.IsSimulated);
        Assert.Equal("ARH", status.ConfiguredType);
        Assert.Contains("ARH", status.Note);
        Assert.Contains("non déployé", status.Note);
    }

    [Fact]
    public void MachineScopedSetting_IsPreferredOverGenericRow()
    {
        _db.Settings.Add(new Setting { Type = "DocumentReader", Description = "WIA", MachineName = "AUTRE-POSTE" });
        _db.Settings.Add(new Setting { Type = "DocumentReader", Description = "REGULA", MachineName = Environment.MachineName });
        _db.SaveChanges();

        var registry = CreateRegistry();

        Assert.Equal("REGULA", registry.GetStatus().Single(s => s.Role == "DocumentReader").ConfiguredType);
    }

    [Fact]
    public void Registry_ExposesCameraAndScannerAsSimulated()
    {
        var registry = CreateRegistry();

        Assert.True(registry.Camera.IsSimulated);
        Assert.True(registry.Fingerprints.IsSimulated);
        Assert.Equal(3, registry.GetStatus().Count);
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>Tests du stockage biométrique des voyageurs (Lot 3).</summary>
public class BiometricServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly BiometricService _service;
    private readonly SimulatedFingerprintScanner _scanner = new();

    public BiometricServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"biometrics-{Guid.NewGuid()}").Options);
        var clock = new SystemClock();
        _service = new BiometricService(_db, _currentUser, clock,
            new AuditWriter(_db, _currentUser, clock), new SimulatedFingerprintMatcher());
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3, BorderPointId = 2 }, "IMMIG_OFF");
        _currentUser.SetPcNumber("A01");

        _db.Persons.Add(new Person { Id = 1, UpdateDate = DateTime.Now });
        _db.Persons.Add(new Person { Id = 2, UpdateDate = DateTime.Now });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Store_CreatesPrint_AndStatusReflectsIt()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        var capture = await _scanner.CaptureAsync(1);

        await _service.StoreAsync(1, capture);

        var status = await _service.GetStatusAsync(1);
        Assert.Equal(10, status.Count);
        var thumb = status.Single(s => s.FingerNumber == 1);
        Assert.True(thumb.Captured);
        Assert.InRange(thumb.Quality!.Value, 70, 99);
        Assert.Equal("A01", thumb.Device);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(Fingerprint));
    }

    [Fact]
    public async Task Store_SameFingerTwice_ReplacesCapture()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        await _service.StoreAsync(1, await _scanner.CaptureAsync(1));

        _scanner.SelectedIdentity = "SIM-2";
        await _service.StoreAsync(1, await _scanner.CaptureAsync(1));

        var stored = _db.Fingerprints.Single(f => f.PersonId == 1 && f.FingerNumber == 1);
        var expected = await _scanner.CaptureAsync(1); // SIM-2 encore sélectionné
        Assert.Equal(expected.Template, stored.Template);
    }

    [Fact]
    public async Task Store_DuplicateFromAnotherPerson_IsRejected()
    {
        _scanner.SelectedIdentity = "SIM-3";
        var capture = await _scanner.CaptureAsync(4);
        await _service.StoreAsync(1, capture);

        // Même doigt, même identité simulée, autre personne → doublon (contrôle hérité d'enrôlement).
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.StoreAsync(2, capture));
        Assert.Equal("BIO_Error_DuplicateFingerprint", ex.ErrorKey);
    }

    [Fact]
    public async Task Store_InvalidFingerNumber_IsRejected()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        var capture = await _scanner.CaptureAsync(11);
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.StoreAsync(1, capture));
        Assert.Equal("BIO_Error_FingerNumber", ex.ErrorKey);
    }

    [Fact]
    public async Task Store_UnknownPerson_IsRejected()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        var capture = await _scanner.CaptureAsync(1);
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.StoreAsync(99, capture));
        Assert.Equal("PAL_Error_DocumentNotFound", ex.ErrorKey);
    }

    [Fact]
    public async Task IdentifyPerson_MatchesStoredTemplate_AndIgnoresUnknown()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        var capture = await _scanner.CaptureAsync(5);
        await _service.StoreAsync(1, capture);

        Assert.Equal(1, await _service.IdentifyPersonAsync(capture.Template));

        _scanner.SelectedIdentity = "SIM-2";
        var unknown = await _scanner.CaptureAsync(5);
        Assert.Null(await _service.IdentifyPersonAsync(unknown.Template));
    }

    [Fact]
    public async Task Image_RoundTrips_AndDeleteRemoves()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        var capture = await _scanner.CaptureAsync(9);
        await _service.StoreAsync(1, capture);

        var image = await _service.GetImageAsync(1, 9);
        Assert.NotNull(image);
        Assert.Equal(capture.Image.Length, image!.Length);

        Assert.Equal(1, await _service.CountAsync());

        await _service.DeleteAsync(1, 9);
        Assert.Null(await _service.GetImageAsync(1, 9));
        Assert.False((await _service.GetStatusAsync(1)).Single(s => s.FingerNumber == 9).Captured);
        Assert.Equal(0, await _service.CountAsync());
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>Tests de la biométrie opérateur (connexion par empreinte).</summary>
public class UserBiometricsServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly UserBiometricsService _service;
    private readonly SimulatedFingerprintScanner _scanner = new();

    public UserBiometricsServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"userbio-{Guid.NewGuid()}").Options);
        var clock = new SystemClock();
        _service = new UserBiometricsService(_db, _currentUser, clock,
            new AuditWriter(_db, _currentUser, clock), new SimulatedFingerprintMatcher());
        _currentUser.Set(new User { Id = "ADM001", UserName = "admin", UserRoleId = 1 }, "ADMIN");

        _db.Users.Add(new User { Id = "U001", UserName = "jdoe", PasswordHash = "x", UserRoleId = 3 });
        _db.Users.Add(new User { Id = "U002", UserName = "kasa", PasswordHash = "x", UserRoleId = 3, Revoke = true });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Enroll_ThenIdentify_ReturnsOperator()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        var capture = await _scanner.CaptureAsync(1);

        await _service.EnrollAsync("U001", slot: 1, capture);

        var status = await _service.GetStatusAsync("U001");
        Assert.True(status.Single(s => s.Slot == 1).Enrolled);
        Assert.False(status.Single(s => s.Slot == 2).Enrolled);
        Assert.Equal("U001", await _service.IdentifyUserAsync(capture.Template));
        Assert.Contains(_db.AuditLogs, a => a.Entity == nameof(User) && a.Details!.Contains("Enrôlement biométrique"));
    }

    [Fact]
    public async Task Enroll_SecondSlot_IsSeparate_AndClearWorks()
    {
        _scanner.SelectedIdentity = "SIM-2";
        var first = await _scanner.CaptureAsync(1);
        _scanner.SelectedIdentity = "SIM-3";
        var second = await _scanner.CaptureAsync(2);

        await _service.EnrollAsync("U001", 1, first);
        await _service.EnrollAsync("U001", 2, second);

        Assert.Equal("U001", await _service.IdentifyUserAsync(second.Template));

        await _service.ClearAsync("U001", 1);
        Assert.Null(await _service.IdentifyUserAsync(first.Template));
        Assert.Equal("U001", await _service.IdentifyUserAsync(second.Template));
    }

    [Fact]
    public async Task Identify_RevokedUser_IsNotReturned()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        var capture = await _scanner.CaptureAsync(1);
        await _service.EnrollAsync("U002", 1, capture);

        Assert.Null(await _service.IdentifyUserAsync(capture.Template));
    }

    [Fact]
    public async Task Enroll_InvalidSlotOrUser_IsRejected()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        var capture = await _scanner.CaptureAsync(1);

        var slotEx = await Assert.ThrowsAsync<DomainValidationException>(() => _service.EnrollAsync("U001", 3, capture));
        Assert.Equal("BIO_Error_Slot", slotEx.ErrorKey);

        var userEx = await Assert.ThrowsAsync<DomainValidationException>(() => _service.EnrollAsync("GHOST", 1, capture));
        Assert.Equal("Admin_Error_ItemNotFound", userEx.ErrorKey);
    }

    [Fact]
    public async Task Identify_UnknownTemplate_ReturnsNull()
    {
        _scanner.SelectedIdentity = "SIM-1 (démonstration)";
        await _service.EnrollAsync("U001", 1, await _scanner.CaptureAsync(1));

        _scanner.SelectedIdentity = "SIM-2";
        Assert.Null(await _service.IdentifyUserAsync((await _scanner.CaptureAsync(1)).Template));
    }

    public void Dispose() => _db.Dispose();
}
