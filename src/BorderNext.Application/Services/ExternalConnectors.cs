using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Demande de contrôle à un système partenaire (document et état civil déclarés).</summary>
public sealed record ExternalCheckRequest(
    string? DocumentNumber = null,
    string? DocumentType = null,
    string? CountryOfIssue = null,
    string? Surname = null,
    string? GivenName = null,
    DateTime? DateOfBirth = null,
    string? Nationality = null,
    DateTime? DocumentExpiry = null);

/// <summary>Verdicts d'un contrôle externe.</summary>
public static class ExternalCheckVerdicts
{
    public const string Clear = "CLEAR";              // aucune opposition connue
    public const string Alert = "ALERT";              // opposition / entrée sur liste
    public const string Inconclusive = "INCONCLUSIVE"; // réponse incomplète
    public const string Error = "ERROR";              // système indisponible
}

/// <summary>Résultat d'un contrôle externe.</summary>
public sealed record ExternalCheckOutcome(
    string Channel,
    string ConnectorName,
    string Verdict,
    string? Reference,
    string Summary,
    IReadOnlyList<string> Details,
    bool IsLive,
    int DurationMs);

/// <summary>Paramètres d'un connecteur, portés par la définition d'échange (ExchangeTask).</summary>
public sealed record ConnectorSettings(string Channel, string Name, bool IsActive, string Mode, string? Endpoint,
    string? Parameters)
{
    public const string ModeMock = "MOCK";
    public const string ModeLive = "LIVE";

    /// <summary>
    /// Secret d'authentification du partenaire (jeton porteur), lu du réglage protégé
    /// <c>Exchange_Connector_{canal}_Secret</c> — jamais journalisé ni affiché en clair.
    /// </summary>
    public string? Secret { get; init; }

    public bool IsMock => !string.Equals(Mode, ModeLive, StringComparison.OrdinalIgnoreCase);

    /// <summary>Clé de réglage du secret d'un canal (valeur chiffrée par le protecteur du poste).</summary>
    public static string SecretSettingKey(string channel) => $"Exchange_Connector_{channel}_Secret";
}

/// <summary>Contrat d'un connecteur de contrôle externe (SIMIEC, Belize, système aérien).</summary>
public interface IExternalCheckConnector
{
    string Channel { get; }

    /// <summary>Clé i18n du nom du connecteur.</summary>
    string DisplayKey { get; }

    Task<ExternalCheckOutcome> CheckAsync(ExternalCheckRequest request, ConnectorSettings settings,
        CancellationToken ct);
}

/// <summary>
/// Connecteur SIMIEC (registre de l'immigration, SOAP) — mode simulé déterministe :
/// un numéro de document contenant « EXP » signale un titre retiré, un numéro se
/// terminant par 0 signale un séjour irrégulier, sinon aucune trace défavorable.
/// En mode direct, un appel POST du message XML est envoyé au point d'accès configuré.
/// </summary>
public sealed class SimiecConnector : ExternalCheckConnectorBase
{
    public const string ChannelCode = ExchangeChannels.Simiec;
    public const string EndpointSetting = "Exchange_Connector_SIMIEC_Endpoint";

    public SimiecConnector(Func<HttpClient> httpClientFactory) : base(httpClientFactory) { }

    public override string Channel => ChannelCode;
    public override string DisplayKey => "EX_Connector_Simiec";

    protected override Task<ExternalCheckOutcome> MockAsync(ExternalCheckRequest request, CancellationToken ct)
        => Task.FromResult(Mock(request));

    private ExternalCheckOutcome Mock(ExternalCheckRequest request)
    {
        var number = (request.DocumentNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length == 0)
            return new ExternalCheckOutcome(Channel, "SIMIEC", ExternalCheckVerdicts.Inconclusive, null,
                "Numéro de document absent : interrogation impossible.", Array.Empty<string>(), false, 0);

        var reference = $"SIM-{number}";
        if (number.Contains("EXP"))
            return new ExternalCheckOutcome(Channel, "SIMIEC", ExternalCheckVerdicts.Alert, reference,
                "SIMIEC (simulateur) : titre signalé retiré ou séjour irrégulier.",
                new[] { $"Enregistrement {reference} : opposition à l'entrée." }, false, 0);

        if (number.EndsWith('0'))
            return new ExternalCheckOutcome(Channel, "SIMIEC", ExternalCheckVerdicts.Alert, reference,
                "SIMIEC (simulateur) : séjour irrégulier signalé.",
                new[] { $"Enregistrement {reference} : sortie du territoire attendue." }, false, 0);

        return new ExternalCheckOutcome(Channel, "SIMIEC", ExternalCheckVerdicts.Clear, reference,
            "SIMIEC (simulateur) : aucune trace défavorable.",
            new[] { $"Enregistrement {reference} : dernier passage conforme." }, false, 0);
    }

    protected override string BuildPayload(ExternalCheckRequest request)
        => $"""
           <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
             <soap:Body>
               <CheckImmigrationRecord xmlns="urn:simiec:v1">
                 <DocumentNumber>{Escape(request.DocumentNumber)}</DocumentNumber>
                 <DocumentType>{Escape(request.DocumentType)}</DocumentType>
                 <Nationality>{Escape(request.Nationality)}</Nationality>
               </CheckImmigrationRecord>
             </soap:Body>
           </soap:Envelope>
           """;
}

/// <summary>
/// Connecteur Belize (cartes et permis de séjour) — mode simulé : carte expirée si la
/// date d'expiration transmise est passée, carte valide sinon, indéterminé sans date.
/// </summary>
public sealed class BelizeConnector : ExternalCheckConnectorBase
{
    public const string ChannelCode = ExchangeChannels.Belize;
    public const string EndpointSetting = "Exchange_Connector_BELIZE_Endpoint";

    public BelizeConnector(Func<HttpClient> httpClientFactory) : base(httpClientFactory) { }

    public override string Channel => ChannelCode;
    public override string DisplayKey => "EX_Connector_Belize";

    protected override Task<ExternalCheckOutcome> MockAsync(ExternalCheckRequest request, CancellationToken ct)
        => Task.FromResult(Mock(request));

    private ExternalCheckOutcome Mock(ExternalCheckRequest request)
    {
        var number = (request.DocumentNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length == 0)
            return new ExternalCheckOutcome(Channel, "BELIZE", ExternalCheckVerdicts.Inconclusive, null,
                "Numéro de document absent : interrogation impossible.", Array.Empty<string>(), false, 0);

        var reference = $"BZ-{number}";
        if (request.DocumentExpiry is not { } expiry)
            return new ExternalCheckOutcome(Channel, "BELIZE", ExternalCheckVerdicts.Inconclusive, reference,
                "Belize (simulateur) : date d'expiration absente, carte indéterminée.",
                new[] { $"Référence {reference}." }, false, 0);

        return expiry.Date < DateTime.Now.Date
            ? new ExternalCheckOutcome(Channel, "BELIZE", ExternalCheckVerdicts.Alert, reference,
                $"Belize (simulateur) : carte/permit expiré le {expiry:dd/MM/yyyy}.",
                new[] { $"Référence {reference} : renouvellement requis." }, false, 0)
            : new ExternalCheckOutcome(Channel, "BELIZE", ExternalCheckVerdicts.Clear, reference,
                $"Belize (simulateur) : carte/permit valide jusqu'au {expiry:dd/MM/yyyy}.",
                new[] { $"Référence {reference}." }, false, 0);
    }

    protected override string BuildPayload(ExternalCheckRequest request)
        => JsonSerializer.Serialize(new
        {
            documentNumber = request.DocumentNumber,
            documentType = request.DocumentType,
            expiry = request.DocumentExpiry?.ToString("yyyy-MM-dd"),
            surname = request.Surname,
            givenName = request.GivenName,
        });
}

/// <summary>
/// Connecteur du système aérien (liste noire et dernier mouvement) — mode simulé :
/// la liste noire est lue du réglage « Exchange_Connector_AIR_MockBlacklist »
/// (noms séparés par des virgules), le dernier mouvement est dérivé de façon
/// déterministe du numéro de document.
/// </summary>
public sealed class AirConnector : ExternalCheckConnectorBase
{
    public const string ChannelCode = ExchangeChannels.Air;
    public const string EndpointSetting = "Exchange_Connector_AIR_Endpoint";
    public const string MockBlacklistSetting = "Exchange_Connector_AIR_MockBlacklist";

    private readonly DbContext _db;

    public AirConnector(DbContext db, Func<HttpClient> httpClientFactory) : base(httpClientFactory) => _db = db;

    public override string Channel => ChannelCode;
    public override string DisplayKey => "EX_Connector_Air";

    protected override async Task<ExternalCheckOutcome> MockAsync(ExternalCheckRequest request, CancellationToken ct)
    {
        var blacklist = await _db.Set<Setting>()
            .Where(s => s.Type == MockBlacklistSetting && s.StringValue != null)
            .Select(s => s.StringValue)
            .FirstOrDefaultAsync(ct);

        var name = string.Join(' ', new[] { request.Surname, request.GivenName }
            .Where(p => !string.IsNullOrWhiteSpace(p))).Trim().ToUpperInvariant();

        var entries = (blacklist ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var hit = name.Length > 0 && entries.Any(e => name.Contains(e.ToUpperInvariant()));
        if (hit)
            return new ExternalCheckOutcome(Channel, "AIR", ExternalCheckVerdicts.Alert, "AIR-BL",
                "Système aérien (simulateur) : passager présent sur la liste noire.",
                new[] { "Liste noire aéroportuaire : interdiction de transport." }, false, 0);

        var number = (request.DocumentNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length == 0)
            return new ExternalCheckOutcome(Channel, "AIR", ExternalCheckVerdicts.Inconclusive, null,
                "Numéro de document absent : dernier mouvement indéterminé.", Array.Empty<string>(), false, 0);

        // Dernier mouvement simulé : décalage déterministe (0-29 jours) dérivé du document.
        var hash = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(number)), 0);
        var lastMovement = DateTime.Now.Date.AddDays(-(hash % 30));
        return new ExternalCheckOutcome(Channel, "AIR", ExternalCheckVerdicts.Clear, $"AIR-{number}",
            $"Système aérien (simulateur) : dernier mouvement le {lastMovement:dd/MM/yyyy}.",
            new[] { $"Référence AIR-{number} : aucune opposition." }, false, 0);
    }

    protected override string BuildPayload(ExternalCheckRequest request)
        => JsonSerializer.Serialize(new
        {
            documentNumber = request.DocumentNumber,
            surname = request.Surname,
            givenName = request.GivenName,
        });
}

/// <summary>
/// Base des connecteurs : exécution simulée par défaut, appel HTTP direct si le mode
/// est « LIVE » et un point d'accès est configuré. Le contrat de réponse attendu est
/// un objet JSON { verdict, reference, summary, details } ; toute réponse illisible
/// donne « indéterminé », toute panne donne « erreur » (journalisés par le service).
/// </summary>
public abstract class ExternalCheckConnectorBase : IExternalCheckConnector
{
    private readonly Func<HttpClient> _httpClientFactory;

    protected ExternalCheckConnectorBase(Func<HttpClient> httpClientFactory) => _httpClientFactory = httpClientFactory;

    public abstract string Channel { get; }
    public abstract string DisplayKey { get; }

    public async Task<ExternalCheckOutcome> CheckAsync(ExternalCheckRequest request, ConnectorSettings settings,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        if (settings.IsMock || string.IsNullOrWhiteSpace(settings.Endpoint))
            return await MockAsync(request, ct);

        try
        {
            using var client = _httpClientFactory();
            using var content = new StringContent(BuildPayload(request), Encoding.UTF8, "application/json");

            // Authentification du partenaire : jeton porteur issu du réglage protégé (durcissement).
            if (!string.IsNullOrWhiteSpace(settings.Secret))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.Secret);
            }

            using var response = await client.PostAsync(settings.Endpoint, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return Outcome(ExternalCheckVerdicts.Error, null,
                    $"Point d'accès en erreur ({(int)response.StatusCode}) : {Truncate(body)}",
                    Array.Empty<string>(), true, stopwatch);

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var verdict = root.TryGetProperty("verdict", out var v) ? v.GetString() : null;
                var reference = root.TryGetProperty("reference", out var r) ? r.GetString() : null;
                var summary = root.TryGetProperty("summary", out var s) ? s.GetString() : null;

                var details = new List<string>();
                if (root.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Array)
                    details.AddRange(d.EnumerateArray().Select(e => e.GetString()).Where(e => e is not null)!);

                var normalized = verdict?.Trim().ToUpperInvariant();
                if (normalized is not (ExternalCheckVerdicts.Clear or ExternalCheckVerdicts.Alert
                    or ExternalCheckVerdicts.Inconclusive or ExternalCheckVerdicts.Error))
                    return Outcome(ExternalCheckVerdicts.Inconclusive, reference,
                        summary ?? $"Réponse non reconnue : {Truncate(body)}", details, true, stopwatch);

                return Outcome(normalized, reference, summary ?? normalized, details, true, stopwatch);
            }
            catch (JsonException)
            {
                return Outcome(ExternalCheckVerdicts.Inconclusive, null,
                    $"Réponse illisible : {Truncate(body)}", Array.Empty<string>(), true, stopwatch);
            }
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Outcome(ExternalCheckVerdicts.Error, null, "Délai d'attente dépassé.", Array.Empty<string>(), true, stopwatch);
        }
        catch (HttpRequestException exception)
        {
            return Outcome(ExternalCheckVerdicts.Error, null, $"Système indisponible : {exception.Message}",
                Array.Empty<string>(), true, stopwatch);
        }
    }

    /// <summary>Réponse simulée par défaut : indéterminé (chaque connecteur fournit la sienne).</summary>
    protected virtual Task<ExternalCheckOutcome> MockAsync(ExternalCheckRequest request, CancellationToken ct)
        => Task.FromResult(new ExternalCheckOutcome(Channel, GetType().Name.Replace("Connector", string.Empty),
            ExternalCheckVerdicts.Inconclusive, null, "Simulateur non implémenté pour ce connecteur.",
            Array.Empty<string>(), false, 0));

    protected abstract string BuildPayload(ExternalCheckRequest request);

    private ExternalCheckOutcome Outcome(string verdict, string? reference, string summary,
        IReadOnlyList<string> details, bool live, Stopwatch stopwatch) =>
        new(Channel, GetType().Name.Replace("Connector", string.Empty), verdict, reference, summary, details, live,
            (int)stopwatch.ElapsedMilliseconds);

    protected static string Escape(string? value) => System.Security.SecurityElement.Escape(value ?? string.Empty)
        ?? string.Empty;

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];
}
