using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BorderNext.Application.Abstractions;

namespace BorderNext.Infrastructure.Devices;

// ---------------------------------------------------------------------------
// Simulateurs d'appareils (Lot 3). L'héritage branchait Camera_NET/DirectShow,
// VeriFinger/Neurotec et les lecteurs Regula/ARH/3M/Gemalto (composants
// propriétaires non réutilisés). Chaque simulateur implémente la même
// interface que le pilote réel attendu.
// ---------------------------------------------------------------------------

/// <summary>Caméra simulée : portraits synthétiques (dégradé + silhouette), BMP 8 bits.</summary>
public sealed class SimulatedCameraDevice : ICameraDevice
{
    private int _captureCount;

    public string Name => "Caméra simulée";

    public bool IsSimulated => true;

    public Task<byte[]> CaptureAsync(CancellationToken ct = default)
    {
        var seed = Interlocked.Increment(ref _captureCount);
        var canvas = new GrayCanvas(240, 320, seed * 7919);
        canvas.VerticalGradient(190, 120);
        // Tête + épaules (silhouette de portrait)
        canvas.FillEllipse(120, 130, 58, 72, 96);
        canvas.FillEllipse(120, 300, 130, 90, 86);
        // Yeux (deux taches sombres) pour l'orientation visuelle
        canvas.FillEllipse(99, 118, 7, 4, 60);
        canvas.FillEllipse(141, 118, 7, 4, 60);
        canvas.Noise(6);
        return Task.FromResult(canvas.ToBmp());
    }
}

/// <summary>
/// Lecteur d'empreintes simulé : image de crêtes procédurale + gabarit
/// déterministe par identité simulée et par doigt.
/// </summary>
public sealed class SimulatedFingerprintScanner : IFingerprintScanner, ISimulatedIdentitySource
{
    private readonly string[] _identities = { "SIM-1 (démonstration)", "SIM-2", "SIM-3" };
    private int _rotation;

    public string Name => "Lecteur d'empreintes simulé";
    public bool IsSimulated => true;

    public IReadOnlyList<string> Identities => _identities;

    /// <summary>Identité simulée sélectionnée ; null = rotation automatique.</summary>
    public string? SelectedIdentity { get; set; }

    public Task<FingerprintCapture> CaptureAsync(short fingerNumber, CancellationToken ct = default)
    {
        var identity = SelectedIdentity
                       ?? _identities[(int)(Interlocked.Increment(ref _rotation) - 1) % _identities.Length];

        var quality = 70 + Math.Abs(Hash(identity) + fingerNumber) % 30; // 70..99
        var image = RenderFingerprint(identity, fingerNumber, quality);
        var template = SimulatedTemplate.Create(identity, fingerNumber, quality);
        return Task.FromResult(new FingerprintCapture(fingerNumber, image, template, quality));
    }

    private static int Hash(string value) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0);

    /// <summary>Crêtes concentriques elliptiques déformées — image 320×320, BMP 8 bits.</summary>
    private static byte[] RenderFingerprint(string identity, short fingerNumber, int quality)
    {
        var canvas = new GrayCanvas(320, 320, Hash(identity) ^ fingerNumber * 31);
        canvas.Fill(235);

        // Paramètres de crêtes déterministes par identité + doigt.
        var seed = Hash($"{identity}:{fingerNumber}");
        var rx = 118 + seed % 12;
        var ry = 150 + seed % 15;
        var frequency = 0.30 + (seed % 7) * 0.01;
        var swirl = (seed % 5 - 2) * 0.6;        // décalage du cœur (boucle/arc)
        var backgroundNoise = (100 - quality) + 4;

        for (var y = 0; y < 320; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                var dx = (x - 160.0) / rx;
                var dy = (y - 165.0) / ry;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > 1.12) continue;

                var angle = Math.Atan2(dy, dx);
                var phase = distance * 40.0 * frequency + Math.Sin(angle * 2 + swirl) * 2.2
                            + Math.Cos(distance * 6.0) * 0.8;
                var ridge = Math.Sin(phase * Math.PI) > 0 ? 40 : 220;

                // Atténuation du contraste vers le bord (doigt)
                var edge = distance > 0.92 ? (1.12 - distance) / 0.20 : 1.0;
                var value = (byte)Math.Clamp(235 + (ridge - 235) * edge, 0, 255);
                canvas.SetPixel(x, y, value);
            }
        }
        canvas.Noise(backgroundNoise);
        return canvas.ToBmp();
    }
}

/// <summary>
/// Lecteur de documents simulé : spécimens synthétiques avec MRZ TD3 conforme
/// ICAO 9303 (clés de contrôle calculées). Aucun document réel n'est reproduit.
/// </summary>
public sealed class SimulatedDocumentReader : IDocumentReader, ISimulatedIdentitySource
{
    private readonly List<string> _specimenNames;
    private int _rotation;

    public string Name => "Lecteur de documents simulé";
    public bool IsSimulated => true;

    public IReadOnlyList<string> Identities => _specimenNames;

    public string? SelectedIdentity { get; set; }

    public SimulatedDocumentReader()
    {
        _specimenNames = Specimens.Select(s => s.Name).ToList();
    }

    public Task<DocumentReadResult> ReadAsync(CancellationToken ct = default)
    {
        var specimen = ResolveSpecimen();
        if (specimen is null)
            return Task.FromResult(new DocumentReadResult(false, "DEVICE_Error_Unreadable", null, null, null,
                null, null, null, null, null, null, null, null, null, null));

        var mrz = BuildMrz(specimen);
        var json = JsonSerializer.Serialize(new
        {
            specimen.Name,
            specimen.DocumentTypeCode,
            specimen.DocumentNumber,
            specimen.Surname,
            specimen.GivenNames,
            specimen.CountryAlpha3,
            specimen.NationalityAlpha3,
            BirthDate = specimen.BirthDate?.ToString("yyyy-MM-dd"),
            specimen.SexCode,
            IssueDate = specimen.IssueDate?.ToString("yyyy-MM-dd"),
            ExpireDate = specimen.ExpireDate?.ToString("yyyy-MM-dd"),
            specimen.PersonalNumber,
            Mrz = mrz
        }, new JsonSerializerOptions { WriteIndented = false });

        return Task.FromResult(new DocumentReadResult(true, null, specimen.DocumentTypeCode,
            specimen.DocumentNumber, specimen.CountryAlpha3, specimen.Surname, specimen.GivenNames,
            specimen.NationalityAlpha3, specimen.BirthDate, specimen.SexCode, specimen.IssueDate,
            specimen.ExpireDate, specimen.PersonalNumber, mrz, json));
    }

    private Specimen? ResolveSpecimen()
    {
        if (SelectedIdentity is { } selected)
        {
            // Le spécimen « document illisible » sert au test du chemin d'erreur.
            if (selected.StartsWith("Document illisible")) return null;
            return Specimens.FirstOrDefault(s => s.Name == selected);
        }
        return Specimens[(int)(Interlocked.Increment(ref _rotation) - 1) % Specimens.Count];
    }

    /// <summary>MRZ TD3 (2 × 44 caractères) avec clés de contrôle ICAO 9303.</summary>
    internal static string BuildMrz(Specimen specimen)
    {
        var line1 = $"P<{specimen.CountryAlpha3}{Fill(specimen.Surname)}<<{Fill(specimen.GivenNames.Replace(' ', '<'))}"
            .PadRight(44, '<')[..44];

        var documentNumber = specimen.DocumentNumber.PadRight(9, '<')[..9];
        var birth = specimen.BirthDate?.ToString("yyMMdd") ?? "000000";
        var expiry = specimen.ExpireDate?.ToString("yyMMdd") ?? "000000";
        var personal = (specimen.PersonalNumber ?? string.Empty).Replace('-', '<').PadRight(14, '<')[..14];

        var line2 = documentNumber + CheckDigit(documentNumber)
                    + specimen.NationalityAlpha3
                    + birth + CheckDigit(birth)
                    + specimen.SexCode
                    + expiry + CheckDigit(expiry)
                    + personal + CheckDigit(personal);
        var composite = documentNumber + CheckDigit(documentNumber) + birth + CheckDigit(birth)
                        + expiry + CheckDigit(expiry) + personal + CheckDigit(personal);
        line2 += CheckDigit(composite);

        return line1 + "\n" + line2;
    }

    private static string Fill(string value) => value.ToUpperInvariant().Replace(' ', '<');

    /// <summary>Clé de contrôle ICAO 9303 : poids 7,3,1 ; A=10..Z=35 ; '&lt;'=0.</summary>
    internal static char CheckDigit(string input)
    {
        var weights = new[] { 7, 3, 1 };
        var sum = 0;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            var value = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'Z' => c - 'A' + 10,
                _ => 0
            };
            sum += value * weights[i % 3];
        }
        return (char)('0' + sum % 10);
    }

    internal sealed record Specimen(string Name, string DocumentTypeCode, string DocumentNumber,
        string Surname, string GivenNames, string CountryAlpha3, string NationalityAlpha3,
        DateTime? BirthDate, string SexCode, DateTime? IssueDate, DateTime? ExpireDate, string PersonalNumber);

    // Spécimens synthétiques (aucune personne réelle).
    internal static readonly List<Specimen> Specimens = new()
    {
        new Specimen("Passeport — MBALA Jean (RDC)", "OP", "OP0321458", "Mbala", "Jean Pierre", "COD", "COD",
            new DateTime(1990, 5, 20), "M", new DateTime(2024, 1, 10), new DateTime(2030, 1, 9), "PN-0001"),
        new Specimen("Passeport — MARTIN Claire (France)", "OP", "12FR45678", "Martin", "Claire", "FRA", "FRA",
            new DateTime(1985, 11, 3), "F", new DateTime(2023, 6, 15), new DateTime(2033, 6, 14), "FR-7788"),
        new Specimen("Carte d'identité — KABONGO Aline (RDC)", "ID", "ID7741250", "Kabongo", "Aline", "COD", "COD",
            new DateTime(1998, 2, 27), "F", new DateTime(2022, 3, 1), new DateTime(2027, 2, 28), "ID-9911"),
        new Specimen("Laissez-passer — ONU", "Laisssez-Passer", "LPD000128", "Dlamini", "Grace", "COD", "000",
            new DateTime(1979, 8, 12), "F", new DateTime(2025, 1, 5), new DateTime(2026, 1, 4), "UN-000128"),
        new Specimen("Document illisible (test du rejet)", "", "", "", "", "", "", null, "", null, null, "")
    };
}

/// <summary>
/// Comparateur de gabarits du simulateur (démonstration) : similarité octet à
/// octet au seuil 95 % avec doigt identique. Un moteur biométrique certifié
/// (VeriFinger/Neurotec en héritage) se branche derrière la même interface.
/// </summary>
public sealed class SimulatedFingerprintMatcher : IFingerprintMatcher
{
    public const int MatchThreshold = 95;

    public int Score(byte[] templateA, byte[] templateB)
    {
        if (!SimulatedTemplate.IsValid(templateA) || !SimulatedTemplate.IsValid(templateB)) return 0;
        if (SimulatedTemplate.FingerOf(templateA) != SimulatedTemplate.FingerOf(templateB)) return 0;

        var matched = 0;
        for (var i = 0; i < SimulatedTemplate.Size; i++)
            if (templateA[i] == templateB[i]) matched++;
        return matched * 100 / SimulatedTemplate.Size;
    }

    public bool IsMatch(byte[] templateA, byte[] templateB) => Score(templateA, templateB) >= MatchThreshold;
}
