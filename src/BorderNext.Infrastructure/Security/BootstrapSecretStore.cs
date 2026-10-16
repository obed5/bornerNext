using System.Security.Cryptography;
using System.Text;
using BorderNext.Application.Abstractions;

namespace BorderNext.Infrastructure.Security;

/// <summary>
/// Mot de passe du compte d'amorçage (première installation), dans l'ordre :
/// 1. valeur explicitement configurée (déploiement automatisé) ;
/// 2. variable d'environnement <c>BORDERNEXT_BOOTSTRAP_PASSWORD</c> (recommandé) ;
/// 3. fichier protégé DPAPI du poste (créé au premier démarrage) ;
/// 4. à défaut, génération aléatoire, stockage protégé et dépôt d'un fichier de
///    communication à usage unique, à supprimer après la première connexion.
/// Aucun mot de passe n'est écrit en clair dans un fichier de configuration.
/// </summary>
public sealed class BootstrapSecretStore : IBootstrapSecretStore
{
    /// <summary>Variable d'environnement lue en priorité après la configuration explicite.</summary>
    public const string EnvironmentVariable = "BORDERNEXT_BOOTSTRAP_PASSWORD";

    private readonly ISecretProtector _protector;
    private readonly string _directory;

    public BootstrapSecretStore(ISecretProtector protector, string? directory = null)
    {
        _protector = protector;
        _directory = directory ?? DefaultDirectory;
    }

    /// <summary>Répertoire de travail (données de service) : %ProgramData%\BorderNext.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BorderNext");

    /// <summary>Fichier du mot de passe protégé.</summary>
    public string SecretFilePath => Path.Combine(_directory, "bootstrap.secret");

    /// <summary>Fichier de communication à usage unique (créé uniquement en cas de génération).</summary>
    public string NoticeFilePath => Path.Combine(_directory, "mot-de-passe-initial.txt");

    public string? Resolve(string? configuredValue)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue)) return configuredValue;

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;

        if (File.Exists(SecretFilePath))
            return _protector.Unprotect(File.ReadAllBytes(SecretFilePath));

        // Premier démarrage sans secret fourni : mot de passe aléatoire, conservé protégé.
        var generated = GeneratePassword();
        WriteSecret(generated);
        WriteNotice(generated);
        return generated;
    }

    /// <summary>Écrit (ou remplace) le mot de passe d'amorçage protégé.</summary>
    public void WriteSecret(string password)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(SecretFilePath, _protector.Protect(password));
    }

    /// <summary>
    /// Mot de passe aléatoire lisible (20 caractères, alphabet sans caractères ambigus).
    /// </summary>
    public static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(20);
        var builder = new StringBuilder(20);
        foreach (var b in bytes) builder.Append(alphabet[b % alphabet.Length]);
        return builder.ToString();
    }

    private void WriteNotice(string password)
    {
        var text = string.Join(Environment.NewLine, new[]
        {
            "BorderNext — mot de passe du compte d'administration initial",
            "",
            "  utilisateur : admin",
            $"  mot de passe : {password}",
            "",
            "Ce fichier n'existe que parce qu'aucun mot de passe d'amorçage n'avait été fourni.",
            $"Pour un déploiement automatisé, définissez la variable d'environnement {EnvironmentVariable}",
            "avant le premier démarrage : aucun fichier ne sera alors créé.",
            "",
            "SUPPRIMEZ CE FICHIER après la première connexion (le changement de mot de passe",
            "est de toute façon imposé à la première connexion).",
        });
        File.WriteAllText(NoticeFilePath, text, new UTF8Encoding(false));
    }
}
