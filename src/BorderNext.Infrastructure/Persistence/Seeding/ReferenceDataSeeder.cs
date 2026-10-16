using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Infrastructure.Persistence.Seeding;

/// <summary>
/// Alimentation des référentiels depuis <c>reference-data.json</c> (ressource
/// embarquée). Les données proviennent des nomenclatures réelles du système
/// du poste (aucune donnée personnelle) — nomenclatures et référentiels uniquement.
/// Idempotent : chaque section n'est semée que si sa table est vide.
/// </summary>
public static class ReferenceDataSeeder
{
    private const string ResourceName = "BorderNext.Infrastructure.Persistence.Seeding.reference-data.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static async Task SeedAsync(BorderNextDbContext db, CancellationToken ct = default)
    {
        var seed = LoadSeed();
        // Chaque section n'est semée que si sa table est vide (idempotent, reprise partielle).
        await SeedLookupTypesAsync(db, seed, ct);
        await SeedControlsAsync(db, seed, ct);
        await SeedCountriesAsync(db, seed, ct);
        await SeedCitiesAsync(db, seed, ct);
        await SeedBorderPointsAsync(db, seed, ct);
        await EnsureStationSettingsAsync(db, seed, ct);
        await SeedCarriersAsync(db, seed, ct);
        await SeedTerminalsAsync(db, seed, ct);
        await SeedSettingsAsync(db, seed, ct);
        await SeedRoleControlsAsync(db, ct);
        // Remappage des réglages référençant un identifiant hérité (pays, terminal du poste) :
        // après le semis des pays, terminaux et réglages concernés.
        await RemapCountrySettingsAsync(db, seed, ct);
        // Lot 7 — tâches d'échange et réglages des connecteurs externes.
        await EnsureExchangeTasksAsync(db, ct);
        // Lot 9 — durcissement : seuils de sécurité et rétention des fichiers.
        await EnsureSecuritySettingsAsync(db, ct);
        // Lot 8 — types de questions, fiche sanitaire, coordonnées cartographiques.
        await HealthDataSeeder.SeedAsync(db, ct);
    }

    /// <summary>
    /// Réglages de durcissement (Lot 9) : seuil d'échecs avant verrouillage, durée du
    /// verrouillage, rétention des fichiers d'échange. Idempotent : les installations
    /// existantes reçoivent les réglages manquants, sans écraser les valeurs en place.
    /// </summary>
    private static async Task EnsureSecuritySettingsAsync(BorderNextDbContext db, CancellationToken ct)
    {
        var defaults = new (string Type, int Value, string Description)[]
        {
            ("Security_MaxFailedAttempts", 5,
                "Échecs de connexion consécutifs avant verrouillage temporaire du compte."),
            ("Security_LockoutMinutes", 15,
                "Durée du verrouillage temporaire du compte après dépassement du seuil (minutes)."),
            (ExchangeFileStore.RetentionSetting, 0,
                "Conservation des fichiers d'échange (jours ; 0 = conservation illimitée)."),
        };

        foreach (var setting in defaults)
        {
            if (await db.Settings.AnyAsync(s => s.Type == setting.Type, ct)) continue;
            db.Settings.Add(new Setting
            {
                Type = setting.Type,
                Value = setting.Value,
                Description = setting.Description,
                UpdateDate = DateTime.Now,
                UpdateUser = "seed",
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static SeedFile LoadSeed()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Ressource embarquée introuvable : {ResourceName}");
        return JsonSerializer.Deserialize<SeedFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Jeu de référentiels illisible.");
    }

    private static async Task SeedLookupTypesAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        if (await db.LookupTypes.AnyAsync(ct))
        {
            await BackfillLookupCodesAsync(db, seed, ct);
            return;
        }

        var sort = 0;
        foreach (var t in seed.LookupTypes)
        {
            var type = new LookupType
            {
                Code = t.Code,
                NameFr = t.NameFr,
                NameEn = t.NameEn,
                SortOrder = sort += 10
            };
            var itemSort = 0;
            foreach (var i in t.Items)
            {
                var lookup = new Lookup { Code = string.IsNullOrWhiteSpace(i.Code) ? null : i.Code.Trim(), SortOrder = i.SortOrder > 0 ? i.SortOrder : (itemSort += 10) };
                lookup.Cultures.Add(new LookupCulture { Culture = "fr", Description = i.Fr });
                lookup.Cultures.Add(new LookupCulture { Culture = "en", Description = i.En });
                type.Items.Add(lookup);
            }
            db.LookupTypes.Add(type);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Complète les codes d'items absents (extractions antérieures au 2026-10) depuis le seed,
    /// en appariant la description française.</summary>
    private static async Task BackfillLookupCodesAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        var missing = await db.Lookups
            .Include(l => l.Cultures)
            .Where(l => l.Code == null)
            .ToListAsync(ct);
        if (missing.Count == 0) return;

        // Appariement par description puis, pour les libellés dupliqués (ex. deux
        // entrées INTERPOL), par rang dans la liste du seed.
        var byDescription = new Dictionary<string, Queue<string>>();
        foreach (var t in seed.LookupTypes)
        foreach (var i in t.Items)
            if (!string.IsNullOrWhiteSpace(i.Code) && !string.IsNullOrWhiteSpace(i.Fr))
            {
                if (!byDescription.TryGetValue(i.Fr!, out var codes))
                    byDescription[i.Fr!] = codes = new Queue<string>();
                codes.Enqueue(i.Code!.Trim());
            }

        var changed = false;
        foreach (var l in missing)
        {
            var fr = l.Cultures.FirstOrDefault(c => c.Culture == "fr")?.Description;
            if (fr != null && byDescription.TryGetValue(fr, out var codes) && codes.Count > 0)
            {
                l.Code = codes.Dequeue();
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    private static async Task SeedControlsAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        if (await db.Controls.AnyAsync(ct)) return;

        foreach (var c in seed.Controls)
        {
            var control = new Control { Code = c.Code, SortOrder = c.SortOrder };
            control.Cultures.Add(new ControlCulture { Culture = "fr", Description = c.Fr });
            control.Cultures.Add(new ControlCulture { Culture = "en", Description = c.En });
            db.Controls.Add(control);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCountriesAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        if (await db.Countries.AnyAsync(ct)) return;

        // Régions : la nomenclature REGION doit être semée avant (résolution par code).
        var regions = await db.Lookups.Where(l => l.Type!.Code == "REGION")
            .ToDictionaryAsync(l => l.Code ?? string.Empty, l => l.Id, ct);

        foreach (var c in seed.Countries)
        {
            var country = new Country
            {
                // Identifiant hérité (lCountries.ID) : nécessaire pour traduire les
                // réglages qui référencent les pays de l'ancienne base.
                ExternalId = c.OldId > 0 ? c.OldId.ToString() : null,
                Alpha2 = string.IsNullOrWhiteSpace(c.Alpha2) ? null : c.Alpha2,
                Alpha3 = string.IsNullOrWhiteSpace(c.Alpha3) ? null : c.Alpha3,
                SortOrder = c.SortOrder,
                RegionLookupId = regions.TryGetValue(c.RegionCode ?? string.Empty, out var regionId) ? regionId : null
            };
            country.Cultures.Add(new CountryCulture { Culture = "fr", Name = c.Fr });
            country.Cultures.Add(new CountryCulture { Culture = "en", Name = c.En });
            db.Countries.Add(country);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Réglages de poste hérités (BorderPoint, Terminal, MovementDirection) :
    /// absents de la sauvegarde analysée car propres à chaque poste de travail ; semés ici
    /// avec les valeurs du déploiement d'origine, puis complétés s'ils manquent
    /// (bases créées avant le Lot 6).</summary>
    private static readonly string[] StationSettingTypes = { "BorderPoint", "Terminal", "MovementDirection" };

    private static async Task EnsureStationSettingsAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        var wanted = seed.Settings.Where(x => StationSettingTypes.Contains(x.Type)).ToList();
        if (wanted.Count == 0) return;

        var existing = await db.Settings.Where(x => StationSettingTypes.Contains(x.Type))
            .Select(x => x.Type).ToListAsync(ct);
        var added = false;
        foreach (var setting in wanted.Where(x => !existing.Contains(x.Type)))
        {
            db.Settings.Add(new Setting
            {
                Type = setting.Type,
                Value = setting.Value,
                Description = setting.Description,
                OwnerType = "Station",
                UpdateDate = DateTime.Now,
                UpdateUser = "seed"
            });
            added = true;
        }
        if (added) await db.SaveChangesAsync(ct);
    }

    /// <summary>Compagnies de transport (héritage : lCarriers — codes IATA, type de transport, pays).</summary>
    private static async Task SeedCarriersAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        if (await db.Carriers.AnyAsync(ct) || seed.Carriers.Count == 0) return;

        var transportTypes = await db.Lookups
            .Where(l => l.Type!.Code == "TRANSPORT_TYPE")
            .ToDictionaryAsync(l => l.Code ?? string.Empty, l => l.Id, ct);
        var countries = await db.Countries
            .Where(c => c.Alpha3 != null)
            .ToDictionaryAsync(c => c.Alpha3!, c => c.Id, ct);

        foreach (var c in seed.Carriers)
        {
            var carrier = new Carrier
            {
                LegacyId = c.OldId,
                Code = c.Code,
                TransportTypeId = transportTypes.TryGetValue(c.Type ?? string.Empty, out var typeId) ? typeId : null,
                CountryId = countries.TryGetValue(c.CountryAlpha3 ?? string.Empty, out var countryId) ? countryId : null,
                SortOrder = c.SortOrder
            };
            carrier.Cultures.Add(new CarrierCulture { Culture = "fr", Description = Truncate(c.Fr, 150) });
            carrier.Cultures.Add(new CarrierCulture { Culture = "en", Description = Truncate(c.En, 150) });
            db.Carriers.Add(carrier);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Tâches d'échange du Lot 7 (héritage : sync.ImportExportManager) et réglages des
    /// connecteurs : imports/exports EDIFACT et APIS, contrôles SIMIEC, Belize et système
    /// aérien (mode simulé et activation par défaut). Idempotent par canal/direction.
    /// </summary>
    private static async Task EnsureExchangeTasksAsync(BorderNextDbContext db, CancellationToken ct)
    {
        var defaults = new (string Name, string Channel, string Direction, string? Parameters, int Priority)[]
        {
            ("Import PAXLST (EDIFACT)", ExchangeChannels.Edifact, ExchangeDirections.Import, null, 10),
            ("Export PAXLST (EDIFACT)", ExchangeChannels.Edifact, ExchangeDirections.Export, null, 20),
            ("Export messages APIS (XML)", ExchangeChannels.Apis, ExchangeDirections.Export, null, 30),
            ("SIMIEC — registre de l'immigration", ExchangeChannels.Simiec, ExchangeDirections.Check,
                "MODE=MOCK", 40),
            ("Belize — cartes et permis de séjour", ExchangeChannels.Belize, ExchangeDirections.Check,
                "MODE=MOCK", 50),
            ("Système aérien — liste noire / dernier mouvement", ExchangeChannels.Air, ExchangeDirections.Check,
                "MODE=MOCK", 60),
        };

        var existing = await db.ExchangeTasks
            .Select(t => new { t.Channel, t.Direction })
            .ToListAsync(ct);

        foreach (var task in defaults)
        {
            if (existing.Any(e => e.Channel == task.Channel && e.Direction == task.Direction)) continue;
            db.ExchangeTasks.Add(new ExchangeTask
            {
                Name = task.Name,
                Channel = task.Channel,
                Direction = task.Direction,
                IsActive = true,
                DependingOnBorderPoint = false,
                Parameters = task.Parameters,
                Priority = task.Priority,
                CreatedDate = DateTime.Now,
                UpdateUser = "seed",
            });
        }

        // Réglages par défaut des échanges (aucune donnée personnelle).
        var settings = new (string Type, string? Value, string Description)[]
        {
            (ExchangeFileStore.DirectorySetting, null,
                "Répertoire de conservation des fichiers échangés (vide = répertoire de l'application)."),
            (AirConnector.MockBlacklistSetting, null,
                "Liste noire simulée du système aérien (noms séparés par des virgules) — mode simulé uniquement."),
        };

        foreach (var setting in settings)
        {
            if (await db.Settings.AnyAsync(s => s.Type == setting.Type, ct)) continue;
            db.Settings.Add(new Setting
            {
                Type = setting.Type,
                StringValue = setting.Value,
                Description = setting.Description,
                UpdateDate = DateTime.Now,
                UpdateUser = "seed",
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Terminaux (héritage : lTerminals — code IATA, type de transport, pays, ville).</summary>
    private static async Task SeedTerminalsAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        if (await db.Terminals.AnyAsync(ct) || seed.Terminals.Count == 0) return;

        var transportTypes = await db.Lookups
            .Where(l => l.Type!.Code == "TRANSPORT_TYPE")
            .ToDictionaryAsync(l => l.Code ?? string.Empty, l => l.Id, ct);
        var countries = await db.Countries
            .Where(c => c.Alpha3 != null)
            .ToDictionaryAsync(c => c.Alpha3!, c => c.Id, ct);
        var cities = await db.Cities
            .Where(c => c.ExternalId != null)
            .ToDictionaryAsync(c => c.ExternalId!, c => c.Id, ct);

        foreach (var t in seed.Terminals)
        {
            var terminal = new Terminal
            {
                LegacyId = t.OldId,
                Code = t.Code,
                TransportTypeId = transportTypes.TryGetValue(t.Type ?? string.Empty, out var typeId) ? typeId : null,
                CountryId = countries.TryGetValue(t.CountryAlpha3 ?? string.Empty, out var countryId) ? countryId : null,
                CityId = cities.TryGetValue(t.CityCode ?? string.Empty, out var cityId) ? cityId : null,
                SortOrder = t.SortOrder
            };
            terminal.Cultures.Add(new TerminalCulture { Culture = "fr", Description = Truncate(t.Fr, 150) });
            terminal.Cultures.Add(new TerminalCulture { Culture = "en", Description = Truncate(t.En, 150) });
            db.Terminals.Add(terminal);
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);

    /// <summary>Réglages référençant un pays par son identifiant hérité (lCountries.ID) :
    /// traduits vers l'identifiant BorderNext. Corrige aussi les bases semées avant la
    /// conservation de l'identifiant hérité (ExternalId contenant l'alpha-3).</summary>
    private static readonly string[] CountrySettingTypes =
    {
        "Country", "DefaultValueResidentCountryEntry", "DefaultValueResidentCountryExit"
    };

    /// <summary>Réglages référençant un terminal par son identifiant hérité (lTerminals.ID).</summary>
    private static readonly string[] TerminalSettingTypes = { "Terminal" };

    private static async Task RemapCountrySettingsAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        var countries = await db.Countries.ToListAsync(ct);
        if (countries.Count == 0) return;

        // Rattrapage : ExternalId non numérique → identifiant hérité du seed (apparié par alpha-3).
        var byAlpha3 = seed.Countries.ToDictionary(c => c.Alpha3, c => c.OldId.ToString());
        var externalFixed = false;
        foreach (var country in countries)
        {
            if (country.ExternalId != null && !int.TryParse(country.ExternalId, out _)
                && byAlpha3.TryGetValue(country.Alpha3 ?? string.Empty, out var legacyId)
                && country.ExternalId != legacyId)
            {
                country.ExternalId = legacyId;
                externalFixed = true;
            }
        }
        if (externalFixed) await db.SaveChangesAsync(ct);

        // Remappage des réglages : les lignes portant encore la valeur héritée du
        // seed sont réécrites vers l'identifiant BorderNext (pays, ou terminal du poste
        // désigné par son identifiant hérité). Règle idempotente : seules les valeurs
        // égales à la valeur héritée du seed sont corrigées (un choix opérationnel
        // différent n'est jamais écrasé).
        var legacyValues = seed.Settings
            .Where(s => CountrySettingTypes.Contains(s.Type) && s.Value != null)
            .GroupBy(s => s.Type)
            .ToDictionary(g => g.Key, g => g.First().Value!.Value);

        var settings = await db.Settings
            .Where(s => CountrySettingTypes.Contains(s.Type) && s.Value != null)
            .ToListAsync(ct);
        var byLegacyId = new Dictionary<string, int>();
        foreach (var country in countries)
            if (country.ExternalId != null) byLegacyId[country.ExternalId] = country.Id;

        var changed = false;

        // Réglage Terminal : identifiant hérité → identifiant BorderNext du terminal.
        var terminalSettings = await db.Settings
            .Where(s => TerminalSettingTypes.Contains(s.Type) && s.Value != null)
            .ToListAsync(ct);
        if (terminalSettings.Count > 0)
        {
            var terminalsByLegacy = await db.Terminals
                .Where(t => t.LegacyId != null)
                .ToDictionaryAsync(t => t.LegacyId!.Value, t => t.Id, ct);
            var legacyTerminal = seed.Settings.FirstOrDefault(x => x.Type == "Terminal")?.Value;
            foreach (var setting in terminalSettings)
            {
                if (legacyTerminal is not { } legacy || setting.Value != legacy) continue;   // valeur héritée attendue
                if (terminalsByLegacy.TryGetValue(legacy, out var resolved) && setting.Value != resolved)
                {
                    setting.Value = resolved;
                    changed = true;
                }
            }
        }

        foreach (var setting in settings)
        {
            if (!legacyValues.TryGetValue(setting.Type, out var legacyValue) || setting.Value != legacyValue) continue;
            if (byLegacyId.TryGetValue(legacyValue.ToString(), out var resolvedId) && setting.Value != resolvedId)
            {
                setting.Value = resolvedId;
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCitiesAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        if (await db.Cities.AnyAsync(ct)) return;

        var countries = await db.Countries.Where(c => c.Alpha2 != null)
            .ToDictionaryAsync(c => c.Alpha2!, c => c.Id, ct);

        foreach (var c in seed.Cities)
        {
            if (!countries.TryGetValue(c.CountryAlpha2, out var countryId)) continue;
            var city = new City { CountryId = countryId, ExternalId = c.Code, SortOrder = c.SortOrder };
            city.Cultures.Add(new CityCulture { Culture = "fr", Name = c.Fr, Abbreviation = c.Abbreviation });
            city.Cultures.Add(new CityCulture { Culture = "en", Name = c.En, Abbreviation = c.Abbreviation });
            db.Cities.Add(city);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedBorderPointsAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        if (await db.BorderPoints.AnyAsync(ct)) return;

        foreach (var b in seed.BorderPoints)
        {
            // Id hérité conservé (clé métier, gérée par l'application — ValueGeneratedNever).
            var point = new BorderPoint { Id = (short)b.OldId, ExternalId = b.Code, SortOrder = (short)b.SortOrder };
            point.Cultures.Add(new BorderPointCulture { Culture = "fr", Name = b.Fr, RegionalSite = NullIfEmpty(b.SiteFr) });
            point.Cultures.Add(new BorderPointCulture { Culture = "en", Name = b.En, RegionalSite = NullIfEmpty(b.SiteEn) });
            db.BorderPoints.Add(point);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Paramétrage de départ (valeurs réelles du poste FIH, portée globalisée).</summary>
    private static async Task SeedSettingsAsync(BorderNextDbContext db, SeedFile seed, CancellationToken ct)
    {
        if (await db.Settings.AnyAsync(ct)) return;

        foreach (var s in seed.Settings)
        {
            db.Settings.Add(new Setting
            {
                Type = s.Type,
                Value = s.Value,
                Description = s.Description,
                OwnerType = "App",
                UpdateDate = DateTime.Now,
                UpdateUser = "seed"
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Droits par défaut : tous les contrôles ouverts aux rôles ADMIN et SUPERUSER.</summary>
    private static async Task SeedRoleControlsAsync(BorderNextDbContext db, CancellationToken ct)
    {
        if (await db.ControlsToRoles.AnyAsync(ct)) return;

        var adminRoleIds = await db.UserRoles.Where(r => r.Code == "ADMIN" || r.Code == "SUPERUSER")
            .Select(r => r.Id).ToListAsync(ct);
        if (adminRoleIds.Count == 0) return;

        var controls = await db.Controls.Select(c => c.Id).ToListAsync(ct);
        foreach (var roleId in adminRoleIds)
            foreach (var controlId in controls)
                db.ControlsToRoles.Add(new ControlToRole { UserRoleId = roleId, ControlId = controlId, Permission = true, Visibility = true });

        await db.SaveChangesAsync(ct);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // ---- Contrat du fichier de seed ------------------------------------------

    private sealed class SeedFile
    {
        public List<SeedLookupType> LookupTypes { get; set; } = new();
        public List<SeedControl> Controls { get; set; } = new();
        public List<SeedCountry> Countries { get; set; } = new();
        public List<SeedCity> Cities { get; set; } = new();
        public List<SeedBorderPoint> BorderPoints { get; set; } = new();
        public List<SeedSetting> Settings { get; set; } = new();
        public List<SeedCarrier> Carriers { get; set; } = new();
        public List<SeedTerminal> Terminals { get; set; } = new();
    }

    private sealed class SeedCarrier
    {
        [JsonPropertyName("oldId")] public int OldId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? CountryAlpha3 { get; set; }
        public int SortOrder { get; set; }
        public string Fr { get; set; } = string.Empty;
        public string En { get; set; } = string.Empty;
    }

    private sealed class SeedTerminal
    {
        [JsonPropertyName("oldId")] public int OldId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? CountryAlpha3 { get; set; }
        public string? CityCode { get; set; }
        public int SortOrder { get; set; }
        public string Fr { get; set; } = string.Empty;
        public string En { get; set; } = string.Empty;
    }

    private sealed class SeedSetting
    {
        public string Type { get; set; } = string.Empty;
        public int? Value { get; set; }
        public string? Description { get; set; }
    }

    private sealed class SeedLookupType
    {
        public string Code { get; set; } = string.Empty;
        public string NameFr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public List<SeedLookupItem> Items { get; set; } = new();
    }

    private sealed class SeedLookupItem
    {
        public string? Code { get; set; }
        public int SortOrder { get; set; }
        public string Fr { get; set; } = string.Empty;
        public string En { get; set; } = string.Empty;
    }

    private sealed class SeedControl
    {
        public string Code { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public string Fr { get; set; } = string.Empty;
        public string En { get; set; } = string.Empty;
    }    private sealed class SeedCountry
    {
        [JsonPropertyName("oldId")] public int OldId { get; set; }
        public string Alpha2 { get; set; } = string.Empty;
        public string Alpha3 { get; set; } = string.Empty;
        public string? RegionCode { get; set; }
        public int SortOrder { get; set; }
        public string Fr { get; set; } = string.Empty;
        public string En { get; set; } = string.Empty;
    }

    private sealed class SeedCity
    {
        public string Code { get; set; } = string.Empty;
        public string CountryAlpha2 { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public string Fr { get; set; } = string.Empty;
        public string En { get; set; } = string.Empty;
        public string? Abbreviation { get; set; }
    }

    private sealed class SeedBorderPoint
    {
        public int OldId { get; set; }
        public string Code { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public string Fr { get; set; } = string.Empty;
        public string En { get; set; } = string.Empty;
        public string? SiteFr { get; set; }
        public string? SiteEn { get; set; }
    }
}
