using System.Security.Cryptography;
using System.Text;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>
/// Conservation des fichiers échangés (héritage : APIMessagePath / APIMessageFiles —
/// le contenu est déposé sur disque, la base ne garde que le registre).
/// </summary>
public class ExchangeFileStore
{
    /// <summary>Réglage du répertoire racine des échanges (StringValue).</summary>
    public const string DirectorySetting = "Exchange_MessageDirectory";

    /// <summary>Réglage de conservation des fichiers d'échange en jours (Value ; 0 = illimitée).</summary>
    public const string RetentionSetting = "Exchange_MessageRetentionDays";

    private readonly DbContext _db;
    private readonly Abstractions.IClock _clock;

    public ExchangeFileStore(DbContext db, Abstractions.IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string> GetRootAsync(CancellationToken ct = default)
    {
        var configured = await _db.Set<Setting>()
            .Where(s => s.Type == DirectorySetting && s.StringValue != null)
            .Select(s => s.StringValue)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "Exchanges")
            : configured!;
    }

    /// <summary>Dépose un contenu dans le sous-répertoire donné et retourne chemin, empreinte
    /// SHA-256 et taille. Le nom d'origine est conservé en suffixe du nom de fichier.</summary>
    public async Task<(string Path, string Hash, long Size)> SaveAsync(string? fileName, string content,
        string? subDirectory = "Messages", CancellationToken ct = default)
    {
        var directory = Path.Combine(await GetRootAsync(ct), subDirectory ?? "Messages");
        Directory.CreateDirectory(directory);

        var safeName = SanitizeFileName(fileName);
        var path = Path.Combine(directory, $"{_clock.Now:yyyyMMdd_HHmmss_fff}_{safeName}");
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return (path, hash, new FileInfo(path).Length);
    }

    /// <summary>Vrai si une empreinte de contenu est déjà enregistrée (détection de doublons d'import).</summary>
    public async Task<ApisMessageFile?> FindByHashAsync(string hash, CancellationToken ct = default)
        => await _db.Set<ApisMessageFile>().AsNoTracking()
            .Where(f => f.ContentHash == hash)
            .OrderBy(f => f.Id)
            .FirstOrDefaultAsync(ct);

    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "message.edifact";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "message.edifact" : cleaned;
    }
}
