using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>État d'une empreinte (une des dix doigts) pour une personne.</summary>
public sealed record FingerprintInfo(short FingerNumber, bool Captured, int? Quality,
    DateTime? CaptureDate, string? Device);

/// <summary>
/// Biométrie des voyageurs (héritage : table Fingerprints liée à Persons,
/// écrans FingerPrintsForm / EnrollFromScanner / FingerRecordList).
/// Capture via appareil (simulateur en déploiement actuel), stockage
/// image + gabarit, contrôle de doublons à l'enrôlement.
/// </summary>
public class BiometricService
{
    /// <summary>Numérotation héritée des doigts : 1..10.</summary>
    public const short MinFingerNumber = 1;
    public const short MaxFingerNumber = 10;

    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IFingerprintMatcher _matcher;

    public BiometricService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit,
        IFingerprintMatcher matcher)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _matcher = matcher;
    }

    /// <summary>État des dix doigts d'une personne (capturé ou non, qualité).</summary>
    public async Task<IReadOnlyList<FingerprintInfo>> GetStatusAsync(int personId, CancellationToken ct = default)
    {
        var prints = await _db.Set<Fingerprint>().Where(f => f.PersonId == personId).ToListAsync(ct);
        return Enumerable.Range(MinFingerNumber, MaxFingerNumber - MinFingerNumber + 1)
            .Select(n =>
            {
                var print = prints.FirstOrDefault(p => p.FingerNumber == n);
                return new FingerprintInfo((short)n, print is not null, print?.Quality, print?.CaptureDate, print?.CaptureDevice);
            })
            .ToList();
    }

    public async Task<byte[]?> GetImageAsync(int personId, short fingerNumber, CancellationToken ct = default)
        => (await _db.Set<Fingerprint>().SingleOrDefaultAsync(
            f => f.PersonId == personId && f.FingerNumber == fingerNumber, ct))?.FingerprintImage;

    /// <summary>
    /// Stocke la capture (création ou remplacement — réenrôlement). L'héritage
    /// activait le contrôle de doublons à l'enrôlement
    /// (CheckForDuplicatedFingerprintsDuringEnrollment) : si le gabarit
    /// correspond à une autre personne, l'enrôlement est refusé.
    /// </summary>
    public async Task StoreAsync(int personId, FingerprintCapture capture, CancellationToken ct = default)
    {
        if (capture.FingerNumber is < MinFingerNumber or > MaxFingerNumber)
            throw new DomainValidationException("BIO_Error_FingerNumber");

        if (!await _db.Set<Person>().AnyAsync(p => p.Id == personId, ct))
            throw new DomainValidationException("PAL_Error_DocumentNotFound", personId.ToString());

        var duplicate = await IdentifyPersonAsync(capture.Template, ct);
        if (duplicate is not null && duplicate != personId)
            throw new DomainValidationException("BIO_Error_DuplicateFingerprint", duplicate.ToString());

        var print = await _db.Set<Fingerprint>().SingleOrDefaultAsync(
            f => f.PersonId == personId && f.FingerNumber == capture.FingerNumber, ct);
        var isNew = print is null;
        if (print is null)
        {
            print = new Fingerprint { PersonId = personId, FingerNumber = capture.FingerNumber };
            _db.Set<Fingerprint>().Add(print);
        }

        print.FingerprintImage = capture.Image;
        print.Template = capture.Template;
        print.Quality = capture.Quality;
        print.CaptureDate = _clock.Now;
        print.CaptureDevice = _currentUser.PcNumber;
        print.UpdateUser = _currentUser.UserId;
        print.BorderPointId = _currentUser.BorderPointId;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(isNew ? AuditEventTypes.RowAdded : AuditEventTypes.RowUpdated,
            nameof(Fingerprint), $"{personId}/{capture.FingerNumber}",
            $"Empreinte {capture.FingerNumber} (qualité {capture.Quality})", ct);
    }

    public async Task DeleteAsync(int personId, short fingerNumber, CancellationToken ct = default)
    {
        var print = await _db.Set<Fingerprint>().SingleOrDefaultAsync(
            f => f.PersonId == personId && f.FingerNumber == fingerNumber, ct);
        if (print is null) return;

        _db.Set<Fingerprint>().Remove(print);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Fingerprint), $"{personId}/{fingerNumber}",
            "Suppression d'empreinte", ct);
    }

    /// <summary>
    /// Identification : personne dont une empreinte correspond au gabarit scanné.
    /// Recherche directe sur les gabarits (l'héritage s'appuyait sur un index
    /// de hachage court — StopListFingerTemplates.HashName ; un index
    /// équivalent pourra être ajouté avec un moteur réel).
    /// </summary>
    public async Task<int?> IdentifyPersonAsync(byte[] template, CancellationToken ct = default)
    {
        var templates = await _db.Set<Fingerprint>()
            .Where(f => f.Template != null)
            .Select(f => new { f.PersonId, f.Template })
            .ToListAsync(ct);

        foreach (var candidate in templates)
            if (candidate.Template is not null && _matcher.IsMatch(candidate.Template, template))
                return candidate.PersonId;
        return null;
    }

    public Task<int> CountAsync(CancellationToken ct = default)
        => _db.Set<Fingerprint>().Select(f => f.PersonId).Distinct().CountAsync(ct);
}

/// <summary>État de l'enrôlement biométrique d'un opérateur (deux doigts hérités).</summary>
public sealed record UserFingerprintInfo(int Slot, bool Enrolled);

/// <summary>
/// Biométrie des opérateurs (héritage : Users.FirstFingerPrint /
/// SecondFingerPrint + gabarits, contrôlés à la connexion par
/// UserLoginForm.CheckFingerAgainstUsers).
/// </summary>
public class UserBiometricsService
{
    public const int MaxSlots = 2;

    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IFingerprintMatcher _matcher;

    public UserBiometricsService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit,
        IFingerprintMatcher matcher)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _matcher = matcher;
    }

    public async Task<IReadOnlyList<UserFingerprintInfo>> GetStatusAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", userId);
        return Enumerable.Range(1, MaxSlots)
            .Select(slot => new UserFingerprintInfo(slot, TemplateOf(user, slot) is not null))
            .ToList();
    }

    /// <summary>Enrôle (ou remplace) l'empreinte d'un des deux doigts de l'opérateur.</summary>
    public async Task EnrollAsync(string userId, int slot, FingerprintCapture capture, CancellationToken ct = default)
    {
        if (slot is < 1 or > MaxSlots) throw new DomainValidationException("BIO_Error_Slot");

        var user = await _db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", userId);

        if (slot == 1)
        {
            user.FirstFingerprint = capture.Image;
            user.FirstFingerprintTemplate = capture.Template;
        }
        else
        {
            user.SecondFingerprint = capture.Image;
            user.SecondFingerprintTemplate = capture.Template;
        }
        user.UpdateDate = _clock.Now;
        user.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(User), userId,
            $"Enrôlement biométrique opérateur (doigt {slot}, qualité {capture.Quality})", ct);
    }

    public async Task ClearAsync(string userId, int slot, CancellationToken ct = default)
    {
        var user = await _db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", userId);

        if (slot == 1)
        {
            user.FirstFingerprint = null;
            user.FirstFingerprintTemplate = null;
        }
        else
        {
            user.SecondFingerprint = null;
            user.SecondFingerprintTemplate = null;
        }
        user.UpdateDate = _clock.Now;
        user.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(User), userId,
            $"Suppression biométrie opérateur (doigt {slot})", ct);
    }

    /// <summary>
    /// Identification de l'opérateur par empreinte (héritage :
    /// UserLoginForm.CheckFingerAgainstUsers). Retourne l'identifiant utilisateur
    /// ou null si aucune correspondance.
    /// </summary>
    public async Task<string?> IdentifyUserAsync(byte[] template, CancellationToken ct = default)
    {
        var users = await _db.Set<User>()
            .Where(u => !u.Revoke && (u.FirstFingerprintTemplate != null || u.SecondFingerprintTemplate != null))
            .Select(u => new { u.Id, u.FirstFingerprintTemplate, u.SecondFingerprintTemplate })
            .ToListAsync(ct);

        foreach (var user in users)
        {
            if (user.FirstFingerprintTemplate is not null && _matcher.IsMatch(user.FirstFingerprintTemplate, template))
                return user.Id;
            if (user.SecondFingerprintTemplate is not null && _matcher.IsMatch(user.SecondFingerprintTemplate, template))
                return user.Id;
        }
        return null;
    }

    internal static byte[]? TemplateOf(User user, int slot)
        => slot == 1 ? user.FirstFingerprintTemplate : user.SecondFingerprintTemplate;
}
