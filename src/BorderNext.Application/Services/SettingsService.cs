using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>
/// Accès aux réglages applicatifs (table Settings) — requêtes paramétrées,
/// portée optionnelle par poste/utilisateur/point frontière.
/// </summary>
public class SettingsService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly BorderNext.Application.Abstractions.ISecretProtector _protector;

    public SettingsService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit,
        BorderNext.Application.Abstractions.ISecretProtector protector)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _protector = protector;
    }

    public async Task<int?> GetIntAsync(string type, short? borderPointId = null, CancellationToken ct = default)
        => (await FindAsync(type, borderPointId, ct))?.Value;

    public async Task<string?> GetStringAsync(string type, short? borderPointId = null, CancellationToken ct = default)
        => (await FindAsync(type, borderPointId, ct))?.StringValue;

    public async Task SetIntAsync(string type, int value, short? borderPointId = null, CancellationToken ct = default)
    {
        var setting = await FindAsync(type, borderPointId, ct);
        if (setting is null)
        {
            setting = NewSetting(type, borderPointId);
            _db.Set<Setting>().Add(setting);
        }
        setting.Value = value;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetStringAsync(string type, string value, short? borderPointId = null, CancellationToken ct = default)
    {
        var setting = await FindAsync(type, borderPointId, ct);
        if (setting is null)
        {
            setting = NewSetting(type, borderPointId);
            _db.Set<Setting>().Add(setting);
        }
        setting.StringValue = value;
        await _db.SaveChangesAsync(ct);
    }

    private Setting NewSetting(string type, short? borderPointId) => new()
    {
        Type = type,
        BorderPointId = borderPointId,
        MachineName = _currentUser.MachineName,
        UserId = _currentUser.UserId,
        UpdateDate = _clock.Now,
        UpdateUser = _currentUser.UserId
    };

    // ---- Secrets de configuration (durcissement) ------------------------------

    /// <summary>
    /// Vrai si la clé de réglage désigne un secret (mot de passe, jeton, clé d'API) :
    /// ces valeurs ne sont jamais stockées en clair ni affichées par l'administration.
    /// </summary>
    public static bool IsSecretKey(string type)
    {
        var key = type ?? string.Empty;
        return key.EndsWith("Secret", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("Password", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("Token", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith("ApiKey", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Vrai si un secret est enregistré pour cette clé (valeur jamais retournée en clair).</summary>
    public async Task<bool> HasSecretAsync(string type, short? borderPointId = null, CancellationToken ct = default)
        => (await FindAsync(type, borderPointId, ct))?.EncryptedValue is { Length: > 0 };

    /// <summary>Enregistre un secret (chiffré par le protecteur du poste).</summary>
    public async Task SetSecretAsync(string type, string secret, short? borderPointId = null, CancellationToken ct = default)
    {
        var setting = await FindAsync(type, borderPointId, ct);
        if (setting is null)
        {
            setting = NewSetting(type, borderPointId);
            _db.Set<Setting>().Add(setting);
        }

        setting.EncryptedValue = string.IsNullOrEmpty(secret) ? null : _protector.Protect(secret);
        setting.StringValue = null;
        setting.UpdateDate = _clock.Now;
        setting.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Setting), setting.Id.ToString(),
            $"{type} — secret {(string.IsNullOrEmpty(secret) ? "effacé" : "enregistré")}", ct);
    }

    /// <summary>
    /// Déchiffre un secret pour un usage interne (appel d'un connecteur). Jamais exposé
    /// par l'administration, qui n'affiche que « défini / non défini ».
    /// </summary>
    public async Task<string?> GetSecretAsync(string type, short? borderPointId = null, CancellationToken ct = default)
    {
        var bytes = (await FindAsync(type, borderPointId, ct))?.EncryptedValue;
        return bytes is { Length: > 0 } ? _protector.Unprotect(bytes) : null;
    }

    private Task<Setting?> FindAsync(string type, short? borderPointId, CancellationToken ct)
        => _db.Set<Setting>()
            .Where(s => s.Type == type && s.BorderPointId == borderPointId && s.UserId == _currentUser.UserId)
            .OrderByDescending(s => s.UpdateDate)
            .FirstOrDefaultAsync(ct);

    // ---- Administration du paramétrage (onglet Paramètres) -------------------

    /// <summary>Liste complète des réglages (vue administration, avec portée).</summary>
    public async Task<IReadOnlyList<SettingDto>> ListAsync(string? search = null, CancellationToken ct = default)
    {
        var settings = await _db.Set<Setting>()
            .Include(s => s.BorderPoint)
            .OrderBy(s => s.Type).ThenBy(s => s.BorderPointId).ThenBy(s => s.UserId)
            .ToListAsync(ct);

        return settings
            .Where(s => string.IsNullOrWhiteSpace(search)
                        || s.Type.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (s.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (s.StringValue?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(s => new SettingDto(s.Id, s.Type, s.Value, s.StringValue, s.Description, s.ShortDescription,
                s.BorderPointId, s.BorderPoint?.ExternalId, s.UserId, s.MachineName, s.UpdateDate, s.UpdateUser))
            .ToList();
    }

    /// <summary>Crée (id null) ou met à jour un réglage. Un seul des deux champs valeur est attendu selon le type.</summary>
    public async Task<int> UpsertAsync(int? id, string type, int? value, string? stringValue, string? description,
        short? borderPointId, CancellationToken ct = default)
    {
        type = (type ?? string.Empty).Trim();
        if (type.Length == 0) throw new DomainValidationException("Admin_Error_SettingTypeRequired");

        Setting setting;
        if (id is { } existingId)
        {
            setting = await _db.Set<Setting>().SingleOrDefaultAsync(s => s.Id == existingId, ct)
                ?? throw new DomainValidationException("Admin_Error_ItemNotFound", existingId.ToString());
        }
        else
        {
            setting = new Setting { MachineName = _currentUser.MachineName };
            _db.Set<Setting>().Add(setting);
        }

        setting.Type = type;
        setting.Value = value;
        setting.StringValue = string.IsNullOrWhiteSpace(stringValue) ? null : stringValue.Trim();
        setting.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        setting.BorderPointId = borderPointId;
        setting.UpdateDate = _clock.Now;
        setting.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(id is null ? AuditEventTypes.RowAdded : AuditEventTypes.RowUpdated,
            nameof(Setting), setting.Id.ToString(), type, ct);
        return setting.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var setting = await _db.Set<Setting>().SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", id.ToString());

        _db.Set<Setting>().Remove(setting);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(Setting), id.ToString(), $"Suppression « {setting.Type} »", ct);
    }
}

/// <summary>Vue admin d'un réglage applicatif.</summary>
public sealed record SettingDto(int Id, string Type, int? Value, string? StringValue, string? Description,
    string? ShortDescription, short? BorderPointId, string? BorderPointCode, string? UserId,
    string? MachineName, DateTime? UpdateDate, string? UpdateUser);
