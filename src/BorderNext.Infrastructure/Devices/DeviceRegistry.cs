using BorderNext.Application.Abstractions;
using BorderNext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BorderNext.Infrastructure.Devices;

/// <summary>
/// Résolution des appareils pour le poste de travail, pilotée par les réglages
/// hérités (dbo.Settings) :
///   - <c>DocumentReader</c> : type de lecteur configuré pour la machine
///     (valeurs observées : ARH, Regula, 3M, Gemalto, WIA) ;
///   - repli sur le simulateur lorsque le pilote matériel n'est pas déployé,
///     avec trace explicite (jamais d'échec silencieux).
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private static readonly string[] KnownHardwareReaders = { "ARH", "REGULA", "3M", "GEMALTO", "WIA", "MMM" };

    private readonly ILogger<DeviceRegistry> _logger;
    private readonly string? _configuredReaderType;
    private readonly string? _note;
    private readonly List<DeviceStatus> _statuses = new();

    public DeviceRegistry(BorderNextDbContext db, ILogger<DeviceRegistry> logger)
    {
        _logger = logger;
        Camera = new SimulatedCameraDevice();
        Fingerprints = new SimulatedFingerprintScanner();
        DocumentReader = new SimulatedDocumentReader();
        Matcher = new SimulatedFingerprintMatcher();

        // Réglage hérité : ligne de la machine courante (ou ligne générique sans machine).
        var machine = Environment.MachineName;
        var setting = db.Settings.AsNoTracking()
            .Where(s => s.Type == "DocumentReader" && (s.MachineName == machine || s.MachineName == null || s.MachineName == ""))
            .OrderByDescending(s => s.MachineName == machine)
            .FirstOrDefault();

        _configuredReaderType = FirstMeaningful(setting?.Description, setting?.StringValue, setting?.ShortDescription);
        if (_configuredReaderType is { } type && KnownHardwareReaders.Contains(type.ToUpperInvariant()))
        {
            _note = $"Pilote matériel « {type} » configuré mais non déployé — simulation utilisée.";
            _logger.LogWarning("Lecteur de documents {Type} configuré pour {Machine} : pilote non déployé, simulateur actif", type, machine);
        }
        else if (_configuredReaderType is null)
        {
            _note = "Aucun lecteur configuré — simulation utilisée.";
        }
        else
        {
            _note = $"Type configuré « {_configuredReaderType} » — simulation utilisée.";
        }

        BuildStatuses();
    }

    public ICameraDevice Camera { get; }
    public IFingerprintScanner Fingerprints { get; }
    public IDocumentReader DocumentReader { get; }
    public IFingerprintMatcher Matcher { get; }

    public IReadOnlyList<DeviceStatus> GetStatus() => _statuses;

    private void BuildStatuses()
    {
        _statuses.Clear();
        _statuses.Add(new DeviceStatus("Camera", Camera.Name, Camera.IsSimulated, null,
            "Capture photo du titulaire (héritage : CameraPhoto / Camera_NET)."));
        _statuses.Add(new DeviceStatus("FingerprintScanner", Fingerprints.Name, Fingerprints.IsSimulated, null,
            "Capture des empreintes 1..10 (héritage : VeriFinger / Neurotec)."));
        _statuses.Add(new DeviceStatus("DocumentReader", DocumentReader.Name, DocumentReader.IsSimulated,
            _configuredReaderType, _note));
    }

    private static string? FirstMeaningful(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
