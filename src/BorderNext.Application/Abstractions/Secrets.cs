namespace BorderNext.Application.Abstractions;

/// <summary>
/// Protection des secrets de configuration (durcissement). L'implémentation Windows
/// s'appuie sur DPAPI : le secret n'est déchiffrable que par le même compte de service
/// sur la même machine — jamais de secret en clair dans un fichier de configuration.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Vrai si la protection forte (chiffrement machine/utilisateur) est disponible.</summary>
    bool IsProtected { get; }

    /// <summary>Chiffre un secret (retourne les octets à stocker).</summary>
    byte[] Protect(string secret);

    /// <summary>Déchiffre un secret précédemment protégé.</summary>
    string Unprotect(byte[] protectedSecret);
}

/// <summary>
/// Résolution du mot de passe du compte d'amorçage (première installation) :
/// variable d'environnement (recommandé), puis fichier protégé DPAPI, sinon génération
/// aléatoire — le mot de passe n'est jamais écrit en clair dans un fichier de configuration.
/// </summary>
public interface IBootstrapSecretStore
{
    /// <summary>
    /// Retourne le mot de passe d'amorçage à appliquer, ou null si aucun n'est nécessaire
    /// (installation déjà initialisée ou secret déjà en place).
    /// </summary>
    string? Resolve(string? configuredValue);
}
