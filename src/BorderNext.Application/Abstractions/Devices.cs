namespace BorderNext.Application.Abstractions;

// ---------------------------------------------------------------------------
// Abstraction matériel (Lot 3). Héritage : lecteurs de documents Regula / ARH /
// 3M / Gemalto (Interop), caméras Camera_NET/DirectShow, SDK biométriques
// Neurotec 11.2 et VeriFinger — aucun de ces composants propriétaires n'est
// réutilisé : chaque interface est implémentée par un simulateur (déploiement
// actuel) et pourra être liée à un pilote réel sans changement des appelants.
// ---------------------------------------------------------------------------

/// <summary>Résultat d'une lecture de document (simulateur ou lecteur physique).</summary>
public sealed record DocumentReadResult(bool Success, string? ErrorCode,
    string? DocumentTypeCode, string? DocumentNumber, string? CountryAlpha3,
    string? Surname, string? GivenNames, string? NationalityAlpha3,
    DateTime? BirthDate, string? SexCode, DateTime? IssueDate, DateTime? ExpireDate,
    string? PersonalNumber, string? Mrz, string? RawJson);

/// <summary>Résultat d'une capture d'empreinte (image + gabarit + qualité).</summary>
public sealed record FingerprintCapture(short FingerNumber, byte[] Image, byte[] Template, int Quality);

/// <summary>Caméra (héritage : capture photo CameraPhoto via Camera_NET/DirectShow).</summary>
public interface ICameraDevice
{
    string Name { get; }

    /// <summary>Matériel réel connecté ? (false = simulateur).</summary>
    bool IsSimulated { get; }

    /// <summary>Capture une photo (portrait).</summary>
    Task<byte[]> CaptureAsync(CancellationToken ct = default);
}

/// <summary>Lecteur d'empreintes (héritage : scanners VeriFinger / Neurotec).</summary>
public interface IFingerprintScanner
{
    string Name { get; }
    bool IsSimulated { get; }

    /// <summary>Capture l'empreinte d'un doigt (numérotation héritée 1..10).</summary>
    Task<FingerprintCapture> CaptureAsync(short fingerNumber, CancellationToken ct = default);
}

/// <summary>Lecteur de documents (héritage : Regula/ARH/3M/Gemalto).</summary>
public interface IDocumentReader
{
    string Name { get; }
    bool IsSimulated { get; }

    /// <summary>Lit le document posé sur la vitre (données + MRZ).</summary>
    Task<DocumentReadResult> ReadAsync(CancellationToken ct = default);
}

/// <summary>
/// Comparaison de gabarits d'empreintes. L'héritage utilisait les moteurs
/// VeriFinger/Neurotec (propriétaires) ; le déploiement reconstruit fournit un
/// comparateur de démonstration et branchera un moteur certifié en production.
/// </summary>
public interface IFingerprintMatcher
{
    /// <summary>Score de similarité 0..100 (seuil de décision métier séparé).</summary>
    int Score(byte[] templateA, byte[] templateB);

    /// <summary>Correspondance au seuil configuré.</summary>
    bool IsMatch(byte[] templateA, byte[] templateB);
}

/// <summary>
/// Sélecteur de spécimen des simulateurs : permet de choisir « quel doigt » ou
/// « quel document de démonstration » on pose sur l'appareil simulé. Absent des
/// pilotes matériels réels.
/// </summary>
public interface ISimulatedIdentitySource
{
    IReadOnlyList<string> Identities { get; }
    string? SelectedIdentity { get; set; }
}

/// <summary>État d'un appareil (affiché dans les écrans capteurs).</summary>
public sealed record DeviceStatus(string Role, string Name, bool IsSimulated, string? ConfiguredType, string? Note);

/// <summary>Accès aux appareils configurés pour le poste (réglages hérités).</summary>
public interface IDeviceRegistry
{
    ICameraDevice Camera { get; }
    IFingerprintScanner Fingerprints { get; }
    IDocumentReader DocumentReader { get; }
    IFingerprintMatcher Matcher { get; }

    IReadOnlyList<DeviceStatus> GetStatus();
}
