using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Migration.Legacy;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Migration;

/// <summary>
/// Reprise de l'historique du système précédent vers le schéma cible. Le traitement
/// est ordonné (référentiels, puis personnes, documents, mouvements, visas, listes et
/// permis), reprenable (les lignes déjà migrées — repérées par leur identifiant hérité —
/// sont ignorées) et sans effet en mode simulation. Toute référence introuvable est
/// comptée comme rejet avec son motif : la migration ne fabrique jamais de donnée.
/// </summary>
public sealed class HistoryMigrator
{
    private readonly ILegacySource _source;
    private readonly BorderNextDbContext _target;
    private readonly MigrationOptions _options;
    private readonly Action<string> _log;

    private readonly Dictionary<short, int> _countries = new();
    private readonly Dictionary<short, short> _borderPoints = new();
    private readonly Dictionary<string, int> _lookups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _persons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _visas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _movements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _permits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pendingVisaLinks = new(StringComparer.Ordinal);

    public HistoryMigrator(ILegacySource source, BorderNextDbContext target, MigrationOptions options,
        Action<string>? log = null)
    {
        _source = source;
        _target = target;
        _options = options;
        _log = log ?? (_ => { });
    }

    public MigrationCounters Counters { get; } = new();

    /// <summary>Nomenclatures héritées reprises (table d'origine → type cible).</summary>
    public static readonly LegacyLookup[] Lookups =
    {
        new("lDocumentTypes", "lDocumentTypesCultures", "DOCUMENT_TYPE"),
        new("lMovementDirections", "lMovementDirectionsCultures", "MOVEMENT_DIRECTION"),
        new("lSex", "lSexCultures", "SEX"),
        new("lTravelReasons", "lTravelReasonsCultures", "TRAVEL_REASON"),
        new("lAccomodationTypes", "lAccomodationTypesCultures", "ACCOMODATION_TYPE"),
        new("lTransportTypes", "lTransportTypesCultures", "TRANSPORT_TYPE"),
        new("lStopListSources", "lStopListSourcesCultures", "STOP_LIST_SOURCE"),
        new("lCheckingReason", "lCheckingReasonCultures", "CHECKING_REASON"),
        new("lMatchInStopListActions", "lMatchInStopListActionsCultures", "MATCH_STOPLIST_ACTION"),
        new("lVisaTypes", "lVisaTypesCultures", "VISA_TYPE"),
        new("lVisaEntry", "lVisaEntryCultures", "VISA_ENTRY"),
        new("lVisaNecessity", "lVisaNecessityCultures", "VISA_NECESSITY"),
        new("lMaritalStatus", "lMaritalStatusCultures", "MARITAL_STATUS"),
        new("lOccupation", "lOccupationCultures", "OCCUPATION"),
        new("lQualification", "lQualificationCultures", "QUALIFICATION"),
        new("lCourseOfStudy", "lCourseOfStudyCultures", "COURSE_OF_STUDY"),
        new("lBloodTypes", "lBloodTypesCultures", "BLOOD_TYPE"),
        new("lRanks", "lRanksCultures", "RANK"),
        new("lTitles", "lTitlesCultures", "TITLE"),
        new("lAgeCategory", "lAgeCategoryCultures", "AGE_CATEGORY"),
        new("lNationalityCategory", "lNationalityCategoryCultures", "NATIONALITY_CATEGORY"),
        new("lPeriodTypes", "lPeriodTypesCultures", "PERIOD_TYPE"),
        new("lRelation", "lRelationCultures", "RELATION"),
        new("lUniversities", "lUniversitiesCultures", "UNIVERSITY"),
        new("lVesselTypes", "lVesselTypesCultures", "VESSEL_TYPE"),
        new("lRegions", "lRegionsCultures", "REGION"),
        new("lLanguages", "lLanguagesCultures", "LANGUAGE"),
        new("lEthnicGroups", "lEthnicGroupsCultures", "ETHNIC_GROUP"),
        new("lCurrency", "lCurrencyCultures", "CURRENCY"),
        new("lIndustry", "lIndustryCultures", "INDUSTRY"),
        new("lTradeType", "lTradeTypeCultures", "TRADE_TYPE"),
        new("lLocalArea", "lLocalAreaCultures", "LOCAL_AREA"),
        new("lEmployeeRange", "lEmployeeRangeCultures", "EMPLOYEE_RANGE"),
        new("lPlaceOfResidenceTypes", "lPlaceOfResidenceTypesCultures", "PLACE_OF_RESIDENCE_TYPE"),
        new("lPermitTypeDocumentList", "lPermitTypeDocumentListCultures", "PERMIT_TYPE"),
        new("lPermitStatus", "lPermitStatusCultures", "PERMIT_STATUS"),
        new("lPermitCondition", "lPermitConditionCultures", "PERMIT_CONDITION"),
        new("lPermitClassification", "lPermitClassificationCultures", "PERMIT_CLASSIFICATION"),
        new("lEntryDocumentTypes", "lEntryDocumentTypesCultures", "ENTRY_DOCUMENT_TYPE"),
        new("lDeportationReasons", "lDeportationReasonsCultures", "DEPORTATION_REASON"),
        new("lRefusalJustifications", "lRefusalJustificationsCultures", "REFUSAL_JUSTIFICATION"),
        new("lRemovalOrderTypes", "lRemovalOrderTypesCultures", "REMOVAL_ORDER_TYPE"),
        new("lPenaltyTicketReason", "lPenaltyTicketReasonCultures", "PENALTY_TICKET_REASON"),
        new("lActions", "lActionsCultures", "ACTION"),
    };

    // -----------------------------------------------------------------------
    // Exécution
    // -----------------------------------------------------------------------

    public async Task<MigrationCounters> RunAsync(CancellationToken ct = default)
    {
        await MigrateLookupsAsync(ct);
        await MigrateCountriesAsync(ct);
        await MigrateBorderPointsAsync(ct);
        await MigratePersonsAsync(ct);
        // Les mouvements créent les documents (le dossier frontalier est le pivot) : les
        // visas, qui pointent un document, viennent après, et le rattachement des visas
        // aux mouvements est fait à la fin.
        await MigrateMovementsAsync(ct);
        await MigrateVisasAsync(ct);
        await LinkMovementVisasAsync(ct);
        await MigratePermitsAsync(ct);
        await MigratePersonStopListAsync(ct);
        await MigrateDocumentStopListAsync(ct);
        await MigrateDoubtablePersonsAsync(ct);
        return Counters;
    }

    /// <summary>Vrai si la table d'origine existe (sinon la section est ignorée et notée).</summary>
    private async Task<bool> HasAsync(LegacyTable table, string section, CancellationToken ct)
    {
        if (!_options.Includes(section)) return false;
        if (await _source.TableExistsAsync(table, ct)) return true;
        _log($"  table {table} absente de l'origine : section ignorée.");
        return false;
    }

    // -----------------------------------------------------------------------
    // Référentiels
    // -----------------------------------------------------------------------

    /// <summary>
    /// Nomenclatures : les éléments existants sont rapprochés par code externe ; les
    /// éléments absents du référentiel cible (postes de travail réels) sont créés avec
    /// leurs libellés, sans jamais écraser un élément déjà en place.
    /// </summary>
    private async Task MigrateLookupsAsync(CancellationToken ct)
    {
        if (!_options.Includes("lookups")) return;

        var types = await _target.LookupTypes.Include(t => t.Items).ThenInclude(i => i.Cultures)
            .ToDictionaryAsync(t => t.Code, t => t, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var lookup in Lookups)
        {
            IReadOnlyList<LegacyLookupItem> items;
            try
            {
                items = await _source.ReadLookupAsync(lookup, ct);
            }
            catch (Exception ex)
            {
                _log($"  nomenclature {lookup.LegacyTable} illisible : {ex.Message}");
                continue;
            }

            var counters = Counters.For(lookup.LookupTypeCode);
            counters.AddRead(items.Count);
            if (items.Count == 0) continue;

            if (!types.TryGetValue(lookup.LookupTypeCode, out var type))
            {
                foreach (var item in items) counters.AddRejected(item.Id.ToString(), "type de nomenclature absent");
                continue;
            }

            foreach (var item in items)
            {
                var code = NormalizeCode(item.ExternalId);
                var target = FindByCode(type, code) ?? FindByLabel(type, item.Labels);

                if (target is null)
                {
                    if (_options.DryRun)
                    {
                        counters.AddWritten();
                        continue;
                    }

                    target = new Lookup
                    {
                        LookupTypeId = type.Id,
                        Code = code,
                        SortOrder = item.SortOrder,
                        Revoke = item.Revoke,
                        RevokeDate = item.RevokeDate,
                        UpdateDate = DateTime.Now,
                        UpdateUser = "migration",
                    };
                    foreach (var (culture, label) in item.Labels)
                        target.Cultures.Add(new LookupCulture { Culture = culture, Description = label });
                    _target.Lookups.Add(target);
                    await _target.SaveChangesAsync(ct);

                    // Les libellés manquants d'une culture sont complétés sans doublon.
                    foreach (var (culture, label) in item.Labels)
                    {
                        if (target.Cultures.Any(c => c.Culture == culture)) continue;
                        target.Cultures.Add(new LookupCulture { Culture = culture, Description = label });
                    }
                    counters.AddWritten();
                }
                else
                {
                    await CompleteCulturesAsync(target, item.Labels, ct);
                    counters.AddSkipped();
                }

                _lookups[Key(lookup.LookupTypeCode, item.Id)] = target.Id;
            }

            if (!_options.DryRun) await _target.SaveChangesAsync(ct);
        }
    }

    private static Lookup? FindByCode(LookupType type, string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : type.Items.FirstOrDefault(i => string.Equals(NormalizeCode(i.Code), code, StringComparison.OrdinalIgnoreCase));

    private static Lookup? FindByLabel(LookupType type, IReadOnlyDictionary<string, string> labels)
    {
        var reference = labels.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (reference is null) return null;
        return type.Items.FirstOrDefault(i => i.Cultures.Any(c =>
            string.Equals(c.Description.Trim(), reference.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private async Task CompleteCulturesAsync(Lookup lookup, IReadOnlyDictionary<string, string> labels,
        CancellationToken ct)
    {
        if (_options.DryRun) return;
        var added = false;
        foreach (var (culture, label) in labels)
        {
            if (lookup.Cultures.Any(c => string.Equals(c.Culture, culture, StringComparison.OrdinalIgnoreCase))) continue;
            lookup.Cultures.Add(new LookupCulture { Culture = culture, Description = label });
            added = true;
        }
        if (added) await _target.SaveChangesAsync(ct);
    }

    private static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        return trimmed.Length > 100 ? trimmed[..100] : trimmed;
    }

    private static string Key(string group, short id) => $"{group}:{id}";

    private int? Lookup(string group, short? legacyId)
        => legacyId is { } id && _lookups.TryGetValue(Key(group, id), out var targetId) ? targetId : null;

    private async Task MigrateCountriesAsync(CancellationToken ct)
    {
        if (!_options.Includes("countries")) return;

        var countries = await _source.ReadCountriesAsync(ct);
        var counters = Counters.For("Countries");
        counters.AddRead(countries.Count);

        var existing = await _target.Countries.Include(c => c.Cultures).ToListAsync(ct);

        foreach (var country in countries)
        {
            var legacyId = country.Id.ToString();
            var target = existing.FirstOrDefault(c => c.ExternalId == legacyId)
                ?? existing.FirstOrDefault(c => !string.IsNullOrWhiteSpace(country.Alpha3)
                                                && !string.IsNullOrWhiteSpace(c.Alpha3)
                                                && c.Alpha3 == country.Alpha3);

            if (target is null)
            {
                if (_options.DryRun)
                {
                    counters.AddWritten();
                    _countries[country.Id] = 0;
                    continue;
                }

                target = new Country
                {
                    ExternalId = legacyId,
                    Alpha2 = string.IsNullOrWhiteSpace(country.Alpha2) ? null : country.Alpha2!.Trim(),
                    Alpha3 = string.IsNullOrWhiteSpace(country.Alpha3) ? null : country.Alpha3!.Trim(),
                    SortOrder = country.SortOrder,
                    Revoke = country.Revoke,
                    UpdateDate = DateTime.Now,
                    UpdateUser = "migration",
                };
                foreach (var (culture, name) in country.Names)
                    target.Cultures.Add(new CountryCulture { Culture = culture, Name = name });
                _target.Countries.Add(target);
                await _target.SaveChangesAsync(ct);
                existing.Add(target);
                counters.AddWritten();
            }
            else
            {
                counters.AddSkipped();
            }

            _countries[country.Id] = target.Id;
        }
    }

    private async Task MigrateBorderPointsAsync(CancellationToken ct)
    {
        if (!_options.Includes("border-points")) return;

        var points = await _source.ReadBorderPointsAsync(ct);
        var counters = Counters.For("BorderPoints");
        counters.AddRead(points.Count);
        var existing = await _target.BorderPoints.Select(p => p.Id).ToListAsync(ct);
        var known = existing.ToHashSet();

        foreach (var point in points)
        {
            _borderPoints[point.Id] = point.Id;
            if (known.Contains(point.Id)) continue;

            if (_options.DryRun)
            {
                counters.AddWritten();
                continue;
            }

            var target = new BorderPoint
            {
                Id = point.Id,
                ExternalId = point.ExternalId ?? point.Id.ToString(),
                SortOrder = point.SortOrder,
            };
            foreach (var (culture, name) in point.Names)
                target.Cultures.Add(new BorderPointCulture { Culture = culture, Name = name });
            _target.BorderPoints.Add(target);
            counters.AddWritten();
        }

        if (!_options.DryRun) await _target.SaveChangesAsync(ct);
    }

    // -----------------------------------------------------------------------
    // Dossier frontalier
    // -----------------------------------------------------------------------

    private async Task MigratePersonsAsync(CancellationToken ct)
    {
        if (!await HasAsync(LegacyTable.Persons, "persons", ct)) return;
        var counters = Counters.For("Persons");

        await foreach (var page in _source.ReadPersonsAsync(_options.PageSize, ct))
        {
            counters.AddRead(page.Count);
            var ids = page.Select(p => p.Id).ToList();
            var existing = await _target.Persons.Where(p => p.LegacyId != null && ids.Contains(p.LegacyId))
                .Select(p => new { p.Id, p.LegacyId }).ToListAsync(ct);
            foreach (var row in existing) _persons[row.LegacyId!] = row.Id;

            foreach (var person in page)
            {
                if (_persons.ContainsKey(person.Id))
                {
                    counters.AddSkipped();
                    continue;
                }

                if (_options.DryRun)
                {
                    counters.AddWritten();
                    continue;
                }

                _target.Persons.Add(new Person
                {
                    LegacyId = person.Id,
                    UpdateUser = Clean(person.UpdateUser, 60),
                    UpdateDate = person.UpdateDate,
                    BorderPointId = BorderPoint(person.BorderPointId),
                });
                counters.AddWritten();
            }

            if (!_options.DryRun) await SaveAndIndexPersonsAsync(page, ct);
        }
    }

    private async Task SaveAndIndexPersonsAsync(IReadOnlyList<LegacyPerson> page, CancellationToken ct)
    {
        await _target.SaveChangesAsync(ct);
        var ids = page.Select(p => p.Id).Where(id => !_persons.ContainsKey(id)).ToList();
        if (ids.Count == 0) return;
        var rows = await _target.Persons.Where(p => p.LegacyId != null && ids.Contains(p.LegacyId))
            .Select(p => new { p.Id, p.LegacyId }).ToListAsync(ct);
        foreach (var row in rows) _persons[row.LegacyId!] = row.Id;
    }

    /// <summary>
    /// Crée les documents manquants d'une page de mouvements, rattachés à la personne du
    /// mouvement (l'origine ne porte pas de lien document → personne : le dossier
    /// frontalier est le pivot de la reprise). Les photos sont reprises sur option.
    /// </summary>
    private async Task EnsureDocumentsAsync(IReadOnlyCollection<(string DocumentId, string PersonId)> links,
        CancellationToken ct)
    {
        var counters = Counters.For("Documents");
        var wanted = links.Select(l => l.DocumentId).Distinct().ToList();

        var known = await _target.TravelDocuments
            .Where(d => d.LegacyId != null && wanted.Contains(d.LegacyId))
            .Select(d => d.LegacyId!).ToListAsync(ct);
        var knownSet = known.ToHashSet(StringComparer.Ordinal);
        var missing = wanted.Where(id => !knownSet.Contains(id)).ToList();
        if (missing.Count == 0) return;

        var rows = await _source.ReadDocumentsByIdsAsync(missing, ct);
        counters.AddRead(rows.Count);
        var personByDocument = links
            .GroupBy(l => l.DocumentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().PersonId, StringComparer.Ordinal);

        if (_options.DryRun)
        {
            counters.AddWritten(rows.Count);
            return;
        }

        foreach (var document in rows)
        {
            if (!personByDocument.TryGetValue(document.Id, out var legacyPersonId)
                || !_persons.TryGetValue(legacyPersonId, out var personId))
            {
                counters.AddRejected(document.Id, "personne absente de la cible");
                continue;
            }

            var documentTypeId = Lookup("DOCUMENT_TYPE", document.DocumentTypeId);
            if (documentTypeId is null)
            {
                counters.AddRejected(document.Id, "type de document inconnu");
                continue;
            }

            _target.TravelDocuments.Add(new TravelDocument
            {
                LegacyId = document.Id,
                PersonId = personId,
                DocumentTypeId = documentTypeId.Value,
                DocumentNumber = Clean(document.DocumentNumber, 40) ?? string.Empty,
                CountryOfIssueId = Country(document.CountryOfIssueId),
                IssueDate = document.IssueDate,
                ExpireDate = document.ExpireDate,
                Surname = Clean(document.Surname, 80),
                GivenName = Clean(document.GivenName, 80),
                FatherName = Clean(document.FatherName, 80),
                DateOfBirth = document.DateOfBirth,
                SexId = Lookup("SEX", document.SexId),
                CitizenshipId = Country(document.Citizenship),
                PersonalNumber = Clean(document.PersonalNumber, 40),
                CreateDate = document.CreateDate,
                CreatorUser = Clean(document.CreatorUser, 100),
                UpdateDate = document.UpdateDate,
                UpdateUser = Clean(document.UpdateUser, 100),
                BorderPointId = BorderPoint(document.BorderPointId),
            });
            counters.AddWritten();
        }

        await _target.SaveChangesAsync(ct);
        var created = await _target.TravelDocuments
            .Where(d => d.LegacyId != null && missing.Contains(d.LegacyId))
            .Select(d => new { d.Id, d.LegacyId }).ToListAsync(ct);
        foreach (var row in created) _documents[row.LegacyId!] = row.Id;

        if (!_options.WithPhotos || created.Count == 0) return;

        var photoCounters = Counters.For("Photos");
        var photos = await _source.ReadPhotosAsync(created.Select(c => c.LegacyId!).ToList(), ct);
        photoCounters.AddRead(photos.Count);

        foreach (var photo in photos)
        {
            if (!_documents.TryGetValue(photo.DocumentId, out var documentId)) continue;
            if (photo.Photo is null && photo.ScannedDocument is null)
            {
                photoCounters.AddSkipped();
                continue;
            }
            if (await _target.Set<DocumentImage>().AnyAsync(i => i.DocumentId == documentId, ct))
            {
                photoCounters.AddSkipped();
                continue;
            }

            _target.Set<DocumentImage>().Add(new DocumentImage
            {
                DocumentId = documentId,
                Photo = photo.Photo,
                ScannedDocument = photo.ScannedDocument,
                UpdateDate = DateTime.Now,
                UpdateUser = "migration",
            });
            photoCounters.AddWritten();
        }

        await _target.SaveChangesAsync(ct);
    }

    private async Task MigrateVisasAsync(CancellationToken ct)
    {
        if (!await HasAsync(LegacyTable.Visa, "visas", ct)) return;
        var counters = Counters.For("Visa");
        var childCounters = Counters.For("VisaChildren");

        await foreach (var page in _source.ReadVisasAsync(_options.PageSize, ct))
        {
            counters.AddRead(page.Count);
            var ids = page.Select(v => v.Id).ToList();
            var existing = await _target.Visas.Where(v => v.LegacyId != null && ids.Contains(v.LegacyId))
                .Select(v => new { v.Id, v.LegacyId }).ToListAsync(ct);
            foreach (var row in existing) _visas[row.LegacyId!] = row.Id;

            foreach (var visa in page)
            {
                if (_visas.ContainsKey(visa.Id))
                {
                    counters.AddSkipped();
                    continue;
                }

                var visaTypeId = Lookup("VISA_TYPE", visa.VisaTypeId);
                if (visaTypeId is null)
                {
                    counters.AddRejected(visa.Id, "type de visa inconnu");
                    continue;
                }

                int? documentId = null;
                if (!string.IsNullOrWhiteSpace(visa.DocumentId))
                {
                    if (!_documents.TryGetValue(visa.DocumentId!, out var docId))
                    {
                        counters.AddRejected(visa.Id, "document porteur absent de la cible");
                        continue;
                    }
                    documentId = docId;
                }

                if (_options.DryRun)
                {
                    counters.AddWritten();
                    continue;
                }

                _target.Visas.Add(new Visa
                {
                    LegacyId = visa.Id,
                    Number = Clean(visa.Number, 40),
                    VisaTypeId = visaTypeId.Value,
                    VisaEntryId = Lookup("VISA_ENTRY", visa.VisaEntryId),
                    IssueDate = visa.IssueDate,
                    ExpireDate = visa.ExpireDate,
                    Duration = visa.Duration,
                    DocumentId = documentId,
                    ReferenceNumber = Clean(visa.ReferenceNumber, 40),
                    ReceiptNumber = Clean(visa.ReceiptNumber, 40),
                    Address = Clean(visa.Address, 200),
                    ContactNumber = Clean(visa.ContactNumber, 40),
                    Employer = Clean(visa.Employer, 100),
                    InvitedBy = Clean(visa.InvitedBy, 100),
                    RelationId = Lookup("RELATION", visa.RelationId),
                    UniversityId = Lookup("UNIVERSITY", visa.UniversityId),
                    PermitStatusId = Lookup("PERMIT_STATUS", visa.PermitStatusId),
                    Occupation = Clean(visa.Occupation, 100),
                    PeriodTypeId = Lookup("PERIOD_TYPE", visa.PeriodTypeId),
                    Note = Clean(visa.Note, 500),
                    VoucherNumber = Clean(visa.VoucherNumber, 40),
                    PermitConditionId = Lookup("PERMIT_CONDITION", visa.PermitConditionId),
                    ProvinceId = null,
                    Barcode = Clean(visa.Barcode, 100),
                    PcNumber = Clean(visa.PcNumber, 20),
                    Revoke = visa.Revoke,
                    RevokeDate = visa.RevokeDate,
                    CreateDate = visa.CreateDate,
                    CreatorUser = Clean(visa.CreatorUser, 100),
                    UpdateDate = visa.UpdateDate,
                    UpdateUser = Clean(visa.UpdateUser, 100),
                    BorderPointId = BorderPoint(visa.BorderPointId),
                });
                counters.AddWritten();
            }

            if (_options.DryRun) continue;
            await _target.SaveChangesAsync(ct);
            var savedIds = page.Select(v => v.Id).Where(id => !_visas.ContainsKey(id)).ToList();
            if (savedIds.Count > 0)
            {
                var rows = await _target.Visas.Where(v => v.LegacyId != null && savedIds.Contains(v.LegacyId))
                    .Select(v => new { v.Id, v.LegacyId }).ToListAsync(ct);
                foreach (var row in rows) _visas[row.LegacyId!] = row.Id;
            }
        }

        await foreach (var page in _source.ReadVisaChildrenAsync(_options.PageSize, ct))
        {
            childCounters.AddRead(page.Count);

            // Les enfants n'ont pas d'identifiant d'origine conservé côté cible :
            // l'unicité se fait par (visa, nom, prénom, naissance) pour rester reprenable.
            var visaIds = page.Select(c => c.VisaId).Distinct().ToList();
            var known = await _target.VisaChildren
                .Where(c => c.Visa!.LegacyId != null
                            && visaIds.Contains(c.Visa.LegacyId)
                            && _visas.Values.Contains(c.VisaId))
                .Select(c => new { c.VisaId, c.Surname, c.GivenName, c.DateOfBirth })
                .ToListAsync(ct);

            foreach (var child in page)
            {
                if (!_visas.TryGetValue(child.VisaId, out var visaId))
                {
                    childCounters.AddRejected(child.Id, "visa absent de la cible");
                    continue;
                }

                var duplicate = known.Any(k => k.VisaId == visaId
                                               && string.Equals(k.Surname, Clean(child.Surname, 80),
                                                   StringComparison.OrdinalIgnoreCase)
                                               && string.Equals(k.GivenName, Clean(child.GivenName, 80),
                                                   StringComparison.OrdinalIgnoreCase)
                                               && k.DateOfBirth == child.DateOfBirth);
                if (duplicate)
                {
                    childCounters.AddSkipped();
                    continue;
                }
                if (_options.DryRun)
                {
                    childCounters.AddWritten();
                    continue;
                }

                _target.VisaChildren.Add(new VisaChild
                {
                    VisaId = visaId,
                    Surname = Clean(child.Surname, 80),
                    GivenName = Clean(child.GivenName, 80),
                    DateOfBirth = child.DateOfBirth,
                    SexId = Lookup("SEX", child.SexId),
                    CitizenshipId = Country(child.Citizenship),
                });
                childCounters.AddWritten();
            }

            if (!_options.DryRun) await _target.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Applique les rattachements de visas différés (le visa est migré après le mouvement
    /// qui le référence). Les identifiants sont mis à jour par lots, sans relire l'origine.
    /// </summary>
    private async Task LinkMovementVisasAsync(CancellationToken ct)
    {
        if (_options.DryRun || _pendingVisaLinks.Count == 0) return;

        var counters = Counters.For("PassportRegistrationData");
        var linked = 0;
        foreach (var (legacyMovementId, legacyVisaId) in _pendingVisaLinks)
        {
            if (!_movements.TryGetValue(legacyMovementId, out var movementId)) continue;
            if (!_visas.TryGetValue(legacyVisaId, out var visaId)) continue;

            var movement = await _target.Movements.FirstOrDefaultAsync(m => m.Id == movementId, ct);
            if (movement is null || movement.VisaId == visaId) continue;
            movement.VisaId = visaId;
            linked++;

            if (linked % 200 == 0) await _target.SaveChangesAsync(ct);
        }

        if (linked > 0)
        {
            await _target.SaveChangesAsync(ct);
            _log($"  rattachement de {linked} mouvement(s) à leur visa.");
        }
    }

    private async Task MigrateMovementsAsync(CancellationToken ct)
    {
        if (!await HasAsync(LegacyTable.PassportRegistrationData, "movements", ct)) return;
        var counters = Counters.For("PassportRegistrationData");
        var childCounters = Counters.For("Children");

        await foreach (var page in _source.ReadMovementsAsync(_options.PageSize, ct))
        {
            counters.AddRead(page.Count);
            var ids = page.Select(m => m.Id).ToList();
            var existing = await _target.Movements.Where(m => m.LegacyId != null && ids.Contains(m.LegacyId))
                .Select(m => new { m.Id, m.LegacyId }).ToListAsync(ct);
            foreach (var row in existing) _movements[row.LegacyId!] = row.Id;

            // Documents de la page créés d'abord, rattachés à la personne du mouvement.
            await EnsureDocumentsAsync(
                page.Select(m => (m.DocumentId, m.PersonId)).Distinct().ToList(), ct);

            foreach (var movement in page)
            {
                if (_movements.ContainsKey(movement.Id))
                {
                    counters.AddSkipped();
                    continue;
                }

                if (!_documents.TryGetValue(movement.DocumentId, out var documentId))
                {
                    counters.AddRejected(movement.Id, "document absent de la cible");
                    continue;
                }
                if (!_persons.TryGetValue(movement.PersonId, out var personId))
                {
                    counters.AddRejected(movement.Id, "personne absente de la cible");
                    continue;
                }

                var directionId = Lookup("MOVEMENT_DIRECTION", movement.MovementDirectionId);
                if (directionId is null)
                {
                    counters.AddRejected(movement.Id, "sens de passage inconnu");
                    continue;
                }

                if (movement.TravelDate == DateTime.MinValue || movement.RegistrationTime == DateTime.MinValue)
                {
                    counters.AddRejected(movement.Id, "date de voyage ou d'enregistrement absente");
                    continue;
                }

                // Le visa n'est migré qu'après les mouvements : le rattachement est
                // mémorisé ici puis appliqué par LinkMovementVisasAsync.
                int? visaId = null;
                if (!string.IsNullOrWhiteSpace(movement.VisaId))
                {
                    if (_visas.TryGetValue(movement.VisaId!, out var vId)) visaId = vId;
                    else _pendingVisaLinks[movement.Id] = movement.VisaId!;
                }

                if (_options.DryRun)
                {
                    counters.AddWritten();
                    continue;
                }

                _target.Movements.Add(new Movement
                {
                    LegacyId = movement.Id,
                    PersonId = personId,
                    DocumentId = documentId,
                    TravelDate = movement.TravelDate,
                    DirectionLookupId = directionId.Value,
                    TransportTypeId = Lookup("TRANSPORT_TYPE", movement.TransportTypeId),
                    TransportCrossBorderId = Clean(movement.TransportCrossBorderId, 40),
                    IsDriver = movement.IsDriver,
                    TravelReasonId = Lookup("TRAVEL_REASON", movement.TravelReasonId),
                    Granted = movement.Granted,
                    DeportationReasonId = Lookup("DEPORTATION_REASON", movement.DeportationReasonId),
                    IsTransit = movement.IsTransit,
                    HasVisa = movement.HasVisa,
                    VisaId = visaId,
                    ChildrenCount = movement.ChildrenCount,
                    Note = Clean(movement.Note, 500),
                    ResidentCountryId = Country(movement.ResidentCountryId),
                    DestinationCountryId = Country(movement.DestinationCountryId),
                    Occupation = Clean(movement.Occupation, 100),
                    AccommodationAddress = Clean(movement.AccommodationAddress, 200),
                    AccommodationTypeId = Lookup("ACCOMODATION_TYPE", movement.AccommodationTypeId),
                    DurationOfStay = movement.DurationOfStay,
                    Inspector = Clean(movement.Inspector, 100),
                    PcNumber = Clean(movement.PcNumber, 20),
                    RegistrationTime = movement.RegistrationTime,
                    UpdateDate = movement.UpdateDate,
                    BorderPointId = BorderPoint(movement.BorderPointId),
                });
                counters.AddWritten();
            }

            if (_options.DryRun) continue;
            await _target.SaveChangesAsync(ct);
            var savedIds = page.Select(m => m.Id).Where(id => !_movements.ContainsKey(id)).ToList();
            if (savedIds.Count > 0)
            {
                var rows = await _target.Movements.Where(m => m.LegacyId != null && savedIds.Contains(m.LegacyId))
                    .Select(m => new { m.Id, m.LegacyId }).ToListAsync(ct);
                foreach (var row in rows) _movements[row.LegacyId!] = row.Id;
            }
        }

        if (!await _source.TableExistsAsync(LegacyTable.Children, ct))
        {
            _log("  table Children absente de l'origine : section ignorée.");
        }
        else
        await foreach (var page in _source.ReadChildrenAsync(_options.PageSize, ct))
        {
            childCounters.AddRead(page.Count);
            var movementIds = page.Select(c => c.MovementId).Distinct().ToList();
            var known = await _target.MovementChildren
                .Where(c => c.Movement!.LegacyId != null && movementIds.Contains(c.Movement.LegacyId))
                .Select(c => new { MovementLegacyId = c.Movement!.LegacyId, c.Surname, c.GivenName, c.DateOfBirth })
                .ToListAsync(ct);

            foreach (var child in page)
            {
                if (!_movements.TryGetValue(child.MovementId, out var movementId))
                {
                    childCounters.AddRejected(child.Id, "mouvement absent de la cible");
                    continue;
                }

                // Les enfants n'ont pas d'identifiant hérité conservé : unicité par
                // (mouvement, nom, prénom, naissance) pour rester reprenable.
                var duplicate = known.Any(k => k.MovementLegacyId == child.MovementId
                                               && string.Equals(k.Surname, child.Surname, StringComparison.OrdinalIgnoreCase)
                                               && string.Equals(k.GivenName, child.GivenName, StringComparison.OrdinalIgnoreCase)
                                               && k.DateOfBirth == child.DateOfBirth);
                if (duplicate)
                {
                    childCounters.AddSkipped();
                    continue;
                }

                if (_options.DryRun)
                {
                    childCounters.AddWritten();
                    continue;
                }

                _target.MovementChildren.Add(new MovementChild
                {
                    MovementId = movementId,
                    Surname = child.Surname,
                    GivenName = child.GivenName,
                    DateOfBirth = child.DateOfBirth,
                    SexId = Lookup("SEX", child.SexId),
                    CitizenshipId = Country(child.Citizenship),
                    DocumentTypeId = Lookup("DOCUMENT_TYPE", child.DocumentTypeId),
                    DocumentNumber = Clean(child.DocumentNumber, 40),
                });
                childCounters.AddWritten();
            }

            if (!_options.DryRun) await _target.SaveChangesAsync(ct);
        }
    }

    private async Task MigratePermitsAsync(CancellationToken ct)
    {
        if (!await HasAsync(LegacyTable.Permit, "permits", ct)) return;
        var counters = Counters.For("Permit");
        var medicalCounters = Counters.For("PermitMedicalHistory");

        await foreach (var page in _source.ReadPermitsAsync(_options.PageSize, ct))
        {
            counters.AddRead(page.Count);
            var ids = page.Select(p => p.Id).ToList();
            var existing = await _target.Permits.Where(p => p.LegacyId != null && ids.Contains(p.LegacyId))
                .Select(p => new { p.Id, p.LegacyId }).ToListAsync(ct);
            foreach (var row in existing) _permits[row.LegacyId!] = row.Id;

            foreach (var permit in page)
            {
                if (_permits.ContainsKey(permit.Id))
                {
                    counters.AddSkipped();
                    continue;
                }
                if (!_documents.TryGetValue(permit.DocumentId, out var documentId))
                {
                    counters.AddRejected(permit.Id, "document absent de la cible");
                    continue;
                }

                if (_options.DryRun)
                {
                    counters.AddWritten();
                    continue;
                }

                int? visaId = null;
                if (!string.IsNullOrWhiteSpace(permit.VisaId) && _visas.TryGetValue(permit.VisaId!, out var vId))
                    visaId = vId;

                _target.Permits.Add(new Permit
                {
                    LegacyId = permit.Id,
                    DocumentId = documentId,
                    VisaId = visaId,
                    PermitTypeId = Lookup("PERMIT_TYPE", permit.PermitTypeId),
                    Number = Clean(permit.Number, 40),
                    IssueDate = permit.IssueDate,
                    ExpireDate = permit.ExpireDate,
                    StatusId = Lookup("PERMIT_STATUS", permit.StatusId),
                    MaritalStatusId = Lookup("MARITAL_STATUS", permit.MaritalStatusId),
                    OccupationId = Lookup("OCCUPATION", permit.OccupationId),
                    OccupationOther = Clean(permit.OccupationOther, 100),
                    QualificationId = Lookup("QUALIFICATION", permit.QualificationId),
                    CourseOfStudyId = Lookup("COURSE_OF_STUDY", permit.CourseOfStudyId),
                    BloodTypeId = Lookup("BLOOD_TYPE", permit.BloodTypeId),
                    DocumentIssuingAuthority = Clean(permit.DocumentIssuingAuthority, 100),
                    Note = Clean(permit.Note, 500),
                    PcNumber = Clean(permit.PcNumber, 20),
                    CreateDate = permit.CreateDate,
                    CreatorUser = Clean(permit.CreateUser, 100),
                    UpdateDate = permit.UpdateDate,
                    UpdateUser = Clean(permit.UpdateUser, 100),
                });
                counters.AddWritten();
            }

            if (_options.DryRun) continue;
            await _target.SaveChangesAsync(ct);
            var savedIds = page.Select(p => p.Id).Where(id => !_permits.ContainsKey(id)).ToList();
            if (savedIds.Count > 0)
            {
                var rows = await _target.Permits.Where(p => p.LegacyId != null && savedIds.Contains(p.LegacyId))
                    .Select(p => new { p.Id, p.LegacyId }).ToListAsync(ct);
                foreach (var row in rows) _permits[row.LegacyId!] = row.Id;
            }
        }

        // Antécédents médicaux (titulaire uniquement : les personnes à charge du permis
        // ne sont pas reprises par cette version — voir la note de migration).
        if (!await _source.TableExistsAsync(LegacyTable.PermitMedicalHistory, ct))
        {
            _log("  table PermitMedicalHistory absente de l'origine : section ignorée.");
            return;
        }
        await foreach (var page in _source.ReadMedicalHistoriesAsync(_options.PageSize, ct))
        {
            medicalCounters.AddRead(page.Count);
            foreach (var history in page)
            {
                if (!_permits.TryGetValue(history.PermitId, out var permitId))
                {
                    medicalCounters.AddRejected(history.PermitId, "permis absent de la cible");
                    continue;
                }
                if (_options.DryRun)
                {
                    medicalCounters.AddWritten();
                    continue;
                }
                if (await _target.Set<PermitMedicalHistory>().AnyAsync(h => h.PermitId == permitId, ct))
                {
                    medicalCounters.AddSkipped();
                    continue;
                }

                _target.Set<PermitMedicalHistory>().Add(new PermitMedicalHistory
                {
                    PermitId = permitId,
                    CurrentIllness = Clean(history.CurrentIllness, 200),
                    CurrentMedication = Clean(history.CurrentMedication, 200),
                    MalariaSuffered = history.MalariaSuffered,
                    YellowFeverSuffered = history.YellowFeverSuffered,
                    TuberculosisSuffered = history.TuberculosisSuffered,
                    MeaslesSuffered = history.MeaslesSuffered,
                    CholeraSuffered = history.CholeraSuffered,
                    TyphoidSuffered = history.TyphoidSuffered,
                    JaundiceSuffered = history.JaundiceSuffered,
                    ChronicCoughSuffered = history.ChronicCoughSuffered,
                    OtherSuffered = Clean(history.OtherSuffered, 200),
                    YellowFeverVaccinated = history.YellowFeverVaccinated,
                    MMRVaccinated = history.MMRVaccinated,
                    DiphtheriaVaccinated = history.DiphtheriaVaccinated,
                    PolioVaccinated = history.PolioVaccinated,
                    HealthCentreVisited = history.HealthCentreVisited,
                    ChronicDisease = history.ChronicDisease,
                    InfectiousDisease = history.InfectiousDisease,
                    IsPublicHealthCentre = history.IsPublicHealthCentre,
                    UpdateDate = history.UpdateDate,
                    UpdateUser = Clean(history.UpdateUser, 100),
                });
                medicalCounters.AddWritten();
            }

            if (!_options.DryRun) await _target.SaveChangesAsync(ct);
        }
    }

    private async Task MigratePersonStopListAsync(CancellationToken ct)
    {
        if (!await HasAsync(LegacyTable.PersonsStopList, "watchlists", ct)) return;
        var counters = Counters.For("PersonsStopList");

        await foreach (var page in _source.ReadPersonStopListAsync(_options.PageSize, ct))
        {
            counters.AddRead(page.Count);
            var ids = page.Select(s => s.Id).ToList();
            var existing = await _target.PersonsStopList
                .Where(s => s.LegacyId != null && ids.Contains(s.LegacyId))
                .Select(s => s.LegacyId!).ToListAsync(ct);
            var known = existing.ToHashSet(StringComparer.Ordinal);

            foreach (var entry in page)
            {
                if (known.Contains(entry.Id))
                {
                    counters.AddSkipped();
                    continue;
                }
                if (_options.DryRun)
                {
                    counters.AddWritten();
                    continue;
                }

                _target.PersonsStopList.Add(new PersonStopList
                {
                    LegacyId = entry.Id,
                    LegacyOldId = Clean(entry.OldId, 40),
                    Surname = Clean(entry.Surname, 80),
                    GivenName = Clean(entry.GivenName, 80),
                    FathersName = Clean(entry.FathersName, 80),
                    SurnameAlt = Clean(entry.SurnameAlt, 80),
                    GivenNameAlt = Clean(entry.GivenNameAlt, 80),
                    FathersNameAlt = Clean(entry.FathersNameAlt, 80),
                    DateOfBirth = entry.DateOfBirth,
                    SexId = Lookup("SEX", entry.SexId),
                    CitizenshipId = Country(entry.Citizenship),
                    WhoSetUp = Clean(entry.WhoSetUp, 100),
                    SetUpDate = entry.SetUpDate,
                    CancelDate = entry.CancelDate,
                    StopListSourceId = Lookup("STOP_LIST_SOURCE", entry.StopListSourceId),
                    SourceDocument = Clean(entry.SourceDocument, 100),
                    Info = Clean(entry.Info, 1000),
                    Photo = entry.Photo,
                    DocumentTypeId = Lookup("DOCUMENT_TYPE", entry.DocumentTypeId),
                    DocumentNumber = Clean(entry.DocumentNumber, 40),
                    PersonalNumber = Clean(entry.PersonalNumber, 40),
                    ActionRequired = Clean(entry.ActionRequired, 200),
                    MatchInStopListActionId = Lookup("MATCH_STOPLIST_ACTION", entry.MatchInStopListActionId),
                    UpdateDate = entry.UpdateDate ?? DateTime.Now,
                    LastUpdateUser = Clean(entry.LastUpdateUser, 100),
                    TimeStamp = entry.TimeStamp,
                    CancelStopListSourceId = Lookup("STOP_LIST_SOURCE", entry.CancelStopListSourceId),
                    CancelSourceDocument = Clean(entry.CancelSourceDocument, 100),
                    IsAuthorized = entry.IsAuthorized,
                });
                counters.AddWritten();
            }

            if (!_options.DryRun) await _target.SaveChangesAsync(ct);
        }
    }

    private async Task MigrateDocumentStopListAsync(CancellationToken ct)
    {
        if (!await HasAsync(LegacyTable.DocumentsStopList, "watchlists", ct)) return;
        var counters = Counters.For("DocumentsStopList");

        await foreach (var page in _source.ReadDocumentStopListAsync(_options.PageSize, ct))
        {
            counters.AddRead(page.Count);
            var ids = page.Select(s => s.Id).ToList();
            var existing = await _target.DocumentsStopList
                .Where(s => s.LegacyId != null && ids.Contains(s.LegacyId))
                .Select(s => s.LegacyId!).ToListAsync(ct);
            var known = existing.ToHashSet(StringComparer.Ordinal);

            foreach (var entry in page)
            {
                if (known.Contains(entry.Id))
                {
                    counters.AddSkipped();
                    continue;
                }
                if (_options.DryRun)
                {
                    counters.AddWritten();
                    continue;
                }

                _target.DocumentsStopList.Add(new DocumentStopList
                {
                    LegacyId = entry.Id,
                    LegacyOldId = Clean(entry.OldId, 40),
                    DocumentTypeId = Lookup("DOCUMENT_TYPE", entry.DocumentTypeId),
                    DocumentNumber = Clean(entry.DocumentNumber, 40),
                    PersonalNumber = Clean(entry.PersonalNumber, 40),
                    CountryId = Country(entry.CountryId),
                    Surname = Clean(entry.Surname, 80),
                    GivenName = Clean(entry.GivenName, 80),
                    FathersName = Clean(entry.FathersName, 80),
                    DateOfBirth = entry.DateOfBirth,
                    SexId = Lookup("SEX", entry.SexId),
                    SetUpDate = entry.SetUpDate,
                    CancelDate = entry.CancelDate,
                    StopListSourceId = Lookup("STOP_LIST_SOURCE", entry.StopListSourceId),
                    SourceDocument = Clean(entry.SourceDocument, 100),
                    Info = Clean(entry.Info, 1000),
                    ActionRequired = Clean(entry.ActionRequired, 200),
                    MatchInStopListActionId = Lookup("MATCH_STOPLIST_ACTION", entry.MatchInStopListActionId),
                    UpdateDate = entry.UpdateDate ?? DateTime.Now,
                    LastUpdateUser = Clean(entry.LastUpdateUser, 100),
                    TimeStamp = entry.TimeStamp,
                    CancelStopListSourceId = Lookup("STOP_LIST_SOURCE", entry.CancelStopListSourceId),
                    CancelSourceDocument = Clean(entry.CancelSourceDocument, 100),
                });
                counters.AddWritten();
            }

            if (!_options.DryRun) await _target.SaveChangesAsync(ct);
        }
    }

    private async Task MigrateDoubtablePersonsAsync(CancellationToken ct)
    {
        if (!await HasAsync(LegacyTable.DoubtablePersons, "doubtful", ct)) return;
        var counters = Counters.For("DoubtablePersons");

        await foreach (var page in _source.ReadDoubtablePersonsAsync(_options.PageSize, ct))
        {
            counters.AddRead(page.Count);
            var movementIds = page.Select(d => d.MovementId).ToList();
            var known = await _target.Set<DoubtablePerson>()
                .Where(d => d.Movement!.LegacyId != null && movementIds.Contains(d.Movement.LegacyId))
                .Select(d => d.Movement!.LegacyId!).ToListAsync(ct);
            var knownSet = known.ToHashSet(StringComparer.Ordinal);

            foreach (var doubt in page)
            {
                if (knownSet.Contains(doubt.MovementId))
                {
                    counters.AddSkipped();
                    continue;
                }
                if (!_movements.TryGetValue(doubt.MovementId, out var movementId))
                {
                    counters.AddRejected(doubt.MovementId, "mouvement absent de la cible");
                    continue;
                }
                if (_options.DryRun)
                {
                    counters.AddWritten();
                    continue;
                }

                _target.Set<DoubtablePerson>().Add(new DoubtablePerson
                {
                    MovementId = movementId,
                    Status = doubt.Status ?? 1,
                    Supervisor = Clean(doubt.Supervisor, 100),
                    SupervisorRegistrationTime = doubt.SupervisorRegistrationTime,
                    SupervisorPcNumber = Clean(doubt.SupervisorPcNumber, 20),
                    InspectorNote = Clean(doubt.InspectorsNote, 500),
                    SupervisorNote = Clean(doubt.SupervisorsNote, 500),
                    PerformedActionId = Lookup("ACTION", doubt.PerformedActionId),
                    CheckingReasonId = Lookup("CHECKING_REASON", doubt.CheckingReason),
                    RefusalOfficerName = Clean(doubt.RefusalOfficerName, 100),
                    RefusalOfficerRank = Clean(doubt.RefusalOfficerRank, 50),
                    RefusalDocNumber = Clean(doubt.RefusalDocNumber, 40),
                    RefusalCountryId = Country(doubt.RefusalCountryId),
                    RefusalJustificationId = Lookup("REFUSAL_JUSTIFICATION", doubt.RefusalJustificationId),
                    RefusalJustificationOther = Clean(doubt.RefusalJustificationOther, 200),
                    RefusalCreateDate = doubt.RefusalCreateDate,
                    // Date de création (colonne obligatoire du schéma cible) : la date de
                    // seconde inspection du dossier d'origine fait foi quand elle existe.
                    CreateDate = doubt.SupervisorRegistrationTime ?? DateTime.Now,
                    CreatedByUserId = Clean(doubt.Supervisor, 100),
                    BorderPointId = null,
                });
                counters.AddWritten();
            }

            if (!_options.DryRun) await _target.SaveChangesAsync(ct);
        }
    }

    // -----------------------------------------------------------------------
    // Aides
    // -----------------------------------------------------------------------

    private int? Country(short? legacyId)
        => legacyId is { } id && _countries.TryGetValue(id, out var targetId) && targetId != 0 ? targetId : null;

    private short? BorderPoint(short? legacyId)
        => legacyId is { } id && _borderPoints.ContainsKey(id) ? id : null;

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
