using BorderNext.Application.Reporting;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Éditions des profils (permis et enregistrements d'étrangers), des visas,
/// des listes d'alerte, du transport, des échanges et de la santé.</summary>
public partial class ReportService
{
    // -----------------------------------------------------------------------
    // Profils — permis de séjour (héritage : GetReportsForeignerRegistration*)
    // -----------------------------------------------------------------------

    /// <summary>Ligne de permis allégée pour les analyses de profils.</summary>
    private sealed record PermitRow(
        int Id,
        DateTime? Issued,
        string? PermitType,
        string Number,
        string? Surname,
        string? GivenName,
        string? DocumentNumber,
        DateTime? BirthDate,
        string? MaritalStatus,
        string? Occupation,
        string? Qualification,
        string? Province,
        bool Revoked,
        bool MedicalRecorded,
        bool SecurityRecorded,
        int Dependants);

    private async Task<List<PermitRow>> LoadPermitsAsync(ReportParameters p, CancellationToken ct)
    {
        var query = _db.Set<Permit>().AsNoTracking().AsQueryable();
        if (p.From is { } from) query = query.Where(x => (x.IssueDate ?? x.CreateDate) >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(x => (x.IssueDate ?? x.CreateDate) < end);
        }
        if (p.BorderPointId is { } borderPoint) query = query.Where(x => x.BorderPointId == borderPoint);
        if (p.PermitTypeId is { } permitType) query = query.Where(x => x.PermitTypeId == permitType);

        var raw = await query
            .OrderBy(x => x.IssueDate ?? x.CreateDate).ThenBy(x => x.Id)
            .Take(MaxRows)
            .Select(x => new
            {
                x.Id,
                Issued = x.IssueDate ?? x.CreateDate,
                x.PermitTypeId,
                x.Number,
                x.MaritalStatusId,
                x.OccupationId,
                x.QualificationId,
                x.Revoke,
                ProvinceId = x.Details!.CurrentProvinceId,
                Surname = x.Document!.Surname,
                GivenName = x.Document!.GivenName,
                DocumentNumber = x.Document!.DocumentNumber,
                BirthDate = x.Document!.DateOfBirth,
                Dependants = x.Dependants.Count,
            })
            .ToListAsync(ct);

        var permitIds = raw.Select(x => x.Id).ToList();
        var medicalIds = await _db.Set<PermitMedicalHistory>().AsNoTracking()
            .Where(h => permitIds.Contains(h.PermitId))
            .Select(h => h.PermitId).Distinct().ToListAsync(ct);
        var securityIds = await _db.Set<PermitSecurityRecord>().AsNoTracking()
            .Where(r => permitIds.Contains(r.PermitId))
            .Select(r => r.PermitId).Distinct().ToListAsync(ct);
        var medical = medicalIds.ToHashSet();
        var security = securityIds.ToHashSet();

        var lookups = await _db.Set<Lookup>().AsNoTracking().Include(l => l.Cultures)
            .ToDictionaryAsync(l => l.Id, ct);
        var provinces = await _db.Set<Province>().AsNoTracking().Include(pr => pr.Cultures)
            .ToDictionaryAsync(pr => pr.Id, ct);

        string? LookupFr(int? id) => id is { } key && lookups.TryGetValue(key, out var lookup)
            ? Description(lookup.Cultures, "fr") : null;

        return raw.Select(x => new PermitRow(
            x.Id, x.Issued, LookupFr(x.PermitTypeId), x.Number ?? "—", x.Surname, x.GivenName, x.DocumentNumber,
            x.BirthDate, LookupFr(x.MaritalStatusId), LookupFr(x.OccupationId), LookupFr(x.QualificationId),
            x.ProvinceId is { } provinceKey && provinces.TryGetValue(provinceKey, out var province)
                ? Description(province.Cultures, "fr") : null,
            x.Revoke, medical.Contains(x.Id), security.Contains(x.Id), x.Dependants)).ToList();
    }

    private async Task<ReportTemplate> BuildForeignersSummaryAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadPermitsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.ForeignersSummary)!;

        var tableRows = rows.GroupBy(x => x.PermitType ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[]
            {
                g.Key, N(g.Count()), N(g.Count(x => !x.Revoked)), N(g.Count(x => x.Revoked)),
                N(g.Count(x => x.MedicalRecorded)), N(g.Count(x => x.SecurityRecorded)),
                N(g.Sum(x => x.Dependants)), Pct(g.Count(), rows.Count),
            }).ToList();
        tableRows.Add(new object?[]
        {
            "Total", N(rows.Count), N(rows.Count(x => !x.Revoked)), N(rows.Count(x => x.Revoked)),
            N(rows.Count(x => x.MedicalRecorded)), N(rows.Count(x => x.SecurityRecorded)),
            N(rows.Sum(x => x.Dependants)), Pct(rows.Count, rows.Count),
        });

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_PermitType"),
                    new ReportColumnTemplate("RP_Col_Permits", true),
                    new ReportColumnTemplate("RP_Col_Active", true),
                    new ReportColumnTemplate("RP_Col_Revoked", true),
                    new ReportColumnTemplate("RP_Col_MedicalRecorded", true),
                    new ReportColumnTemplate("RP_Col_SecurityRecorded", true),
                    new ReportColumnTemplate("RP_Col_Dependants", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_PermitTypes", N(tableRows.Count - 1)),
                new("RP_Sum_WithMedical", N(rows.Count(x => x.MedicalRecorded))),
                new("RP_Sum_WithSecurity", N(rows.Count(x => x.SecurityRecorded))),
            },
        };
    }

    /// <summary>Répartition des enregistrements d'étrangers selon un critère, ventilée
    /// par tranche d'âge (héritage : procédures …AgeBreakdown).</summary>
    private async Task<ReportTemplate> BuildForeignersByCriterionAsync(ReportParameters p, string criterion,
        CancellationToken ct)
    {
        var rows = await LoadPermitsAsync(p, ct);
        var definition = ReportCatalog.Find(criterion switch
        {
            "marital" => ReportCatalog.ForeignersByMaritalStatus,
            "occupation" => ReportCatalog.ForeignersByOccupation,
            "province" => ReportCatalog.ForeignersByProvince,
            "qualification" => ReportCatalog.ForeignersByQualification,
            _ => ReportCatalog.ForeignersByAge,
        })!;

        var reference = _clock.Now;
        var (labelKey, selector) = criterion switch
        {
            "marital" => ("RP_Col_MaritalStatus", (Func<PermitRow, string?>)(x => x.MaritalStatus)),
            "occupation" => ("RP_Col_Occupation", x => x.Occupation),
            "province" => ("RP_Col_Province", x => x.Province),
            "qualification" => ("RP_Col_Qualification", x => x.Qualification),
            _ => ("RP_Col_AgeCategory", x => AgeBand(x.BirthDate, reference)),
        };

        var groups = rows.GroupBy(x => selector(x) ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();

        var columns = new List<ReportColumnTemplate> { new(labelKey), new("RP_Col_Total", true) };
        columns.AddRange(AgeBands.Select(band => new ReportColumnTemplate(Literal + band, true)));
        columns.Add(new ReportColumnTemplate("RP_Col_Share", true));

        var tableRows = groups.Select(g => new object?[] { g.Key, N(g.Count()) }
            .Concat(AgeBands.Select(band =>
                (object?)N(g.Count(x => AgeBand(x.BirthDate, reference) == band))))
            .Concat(new object?[] { Pct(g.Count(), rows.Count) }).ToArray()).ToList();

        tableRows.Add(new object?[] { "Total", N(rows.Count) }
            .Concat(AgeBands.Select(band => (object?)N(rows.Count(x => AgeBand(x.BirthDate, reference) == band))))
            .Concat(new object?[] { Pct(rows.Count, rows.Count) }).ToArray());

        return Template(definition, p) with
        {
            Tables = new[] { Table(null, columns.ToArray(), tableRows, null) },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Criteria", N(groups.Count)),
                new("RP_Sum_WithoutCriterion", N(rows.Count(x => selector(x) is null))),
            },
        };
    }

    /// <summary>Antécédents médicaux des permis (héritage : GetReportsForeignerRegistrationMedical) :
    /// comptage par maladie contractée, vaccination et indicateur courant.</summary>
    private async Task<ReportTemplate> BuildForeignersMedicalAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.ForeignersMedical)!;

        var query = _db.Set<PermitMedicalHistory>().AsNoTracking().AsQueryable();
        if (p.From is { } from) query = query.Where(h => (h.UpdateDate ?? DateTime.MinValue) >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(h => (h.UpdateDate ?? DateTime.MinValue) < end);
        }

        var histories = await query.Take(MaxRows).ToListAsync(ct);

        object?[] Line(string label, Func<PermitMedicalHistory, bool> predicate) =>
            new object?[] { label, N(histories.Count(predicate)), Pct(histories.Count(predicate), histories.Count) };

        var tableRows = new List<object?[]>
        {
            Line("Paludisme contracté", h => h.MalariaSuffered),
            Line("Fièvre jaune contractée", h => h.YellowFeverSuffered),
            Line("Tuberculose contractée", h => h.TuberculosisSuffered),
            Line("Rougeole contractée", h => h.MeaslesSuffered),
            Line("Choléra contracté", h => h.CholeraSuffered),
            Line("Typhoïde contractée", h => h.TyphoidSuffered),
            Line("Jaunisse contractée", h => h.JaundiceSuffered),
            Line("Toux chronique", h => h.ChronicCoughSuffered),
            Line("Autre antécédent", h => !string.IsNullOrWhiteSpace(h.OtherSuffered)),
            Line("Vaccination fièvre jaune", h => h.YellowFeverVaccinated),
            Line("Vaccination ROR", h => h.MMRVaccinated),
            Line("Vaccination diphtérie", h => h.DiphtheriaVaccinated),
            Line("Vaccination polio", h => h.PolioVaccinated),
            Line("Maladie actuelle déclarée", h => !string.IsNullOrWhiteSpace(h.CurrentIllness)),
            Line("Traitement en cours", h => !string.IsNullOrWhiteSpace(h.CurrentMedication)),
            Line("Passage en centre de santé", h => h.HealthCentreVisited),
            Line("Maladie chronique", h => h.ChronicDisease),
            Line("Maladie infectieuse", h => h.InfectiousDisease),
        };

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_MedicalItem"),
                    new ReportColumnTemplate("RP_Col_Count", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_MedicalFiles", N(histories.Count)),
                new("RP_Sum_PublicHealthCentre", N(histories.Count(h => h.IsPublicHealthCentre))),
            },
        };
    }

    /// <summary>Dossiers de sécurité des permis (héritage : GetReportsForeignerRegistrationSecurity).</summary>
    private async Task<ReportTemplate> BuildForeignersSecurityAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.ForeignersSecurity)!;

        var query = _db.Set<PermitSecurityRecord>().AsNoTracking().AsQueryable();
        if (p.From is { } from) query = query.Where(r => r.Permit!.CreateDate >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(r => r.Permit!.CreateDate < end);
        }

        var records = await query.Take(MaxRows).ToListAsync(ct);

        var tableRows = records.GroupBy(r => r.RecordType ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[] { g.Key, N(g.Count()), Pct(g.Count(), records.Count) })
            .ToList();
        tableRows.Add(new object?[] { "Total", N(records.Count), Pct(records.Count, records.Count) });

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_SecurityType"),
                    new ReportColumnTemplate("RP_Col_Records", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_SecurityRecords", N(records.Count)),
                new("RP_Sum_PermitsConcerned", N(records.Select(r => r.PermitId).Distinct().Count())),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Visas — héritage : GetVisaReportAdvancedStatisticsbyIssueDate
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildVisaStatisticsAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.VisaStatistics)!;

        var query = _db.Set<Visa>().AsNoTracking().AsQueryable();
        if (p.From is { } from) query = query.Where(v => (v.IssueDate ?? v.CreateDate) >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(v => (v.IssueDate ?? v.CreateDate) < end);
        }
        if (p.BorderPointId is { } borderPoint) query = query.Where(v => v.BorderPointId == borderPoint);
        if (p.VisaTypeId is { } visaType) query = query.Where(v => v.VisaTypeId == visaType);

        var visas = await query
            .Select(v => new { v.Id, v.Number, v.VisaTypeId, v.IssueDate, v.ExpireDate, v.Duration, v.Revoke })
            .OrderBy(v => v.IssueDate).Take(MaxRows).ToListAsync(ct);

        var types = await _db.Set<Lookup>().AsNoTracking().Include(l => l.Cultures)
            .Where(l => l.Type!.Code == "VISA_TYPE").ToDictionaryAsync(l => l.Id, ct);

        var tableRows = visas.GroupBy(v => types.TryGetValue(v.VisaTypeId, out var type)
                ? Description(type.Cultures, "fr") ?? v.VisaTypeId.ToString() : v.VisaTypeId.ToString())
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[]
            {
                g.Key, N(g.Count()), N(g.Count(v => !v.Revoke)), N(g.Count(v => v.Revoke)),
                g.Where(v => v.Duration is not null).Select(v => (double)v.Duration!).DefaultIfEmpty(0).Average()
                    .ToString("N0", Format),
                D(g.Min(v => v.IssueDate)), D(g.Max(v => v.IssueDate)), Pct(g.Count(), visas.Count),
            }).ToList();
        tableRows.Add(new object?[]
        {
            "Total", N(visas.Count), N(visas.Count(v => !v.Revoke)), N(visas.Count(v => v.Revoke)),
            visas.Where(v => v.Duration is not null).Select(v => (double)v.Duration!).DefaultIfEmpty(0).Average()
                .ToString("N0", Format),
            D(visas.Count == 0 ? null : visas.Min(v => v.IssueDate)),
            D(visas.Count == 0 ? null : visas.Max(v => v.IssueDate)), Pct(visas.Count, visas.Count),
        });

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("Visa_Type"),
                    new ReportColumnTemplate("RP_Col_Visas", true),
                    new ReportColumnTemplate("RP_Col_Active", true),
                    new ReportColumnTemplate("RP_Col_Revoked", true),
                    new ReportColumnTemplate("RP_Col_AverageDuration", true),
                    new ReportColumnTemplate("RP_Col_FirstIssue"),
                    new ReportColumnTemplate("RP_Col_LastIssue"),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Visas", N(visas.Count)),
                new("RP_Sum_VisaTypes", N(tableRows.Count - 1)),
                new("RP_Sum_ExpiringSoon", N(visas.Count(v => v.ExpireDate is { } expiry
                    && expiry >= _clock.Now.Date && expiry < _clock.Now.Date.AddMonths(1)))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Listes d'alerte — héritage : GetReportPersonsStopListByCondition
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildStopListPersonsAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.StopListPersons)!;

        var query = _db.Set<PersonStopList>().AsNoTracking()
            .Include(x => x.Citizenship).ThenInclude(c => c!.Cultures)
            .Include(x => x.MatchInStopListAction).ThenInclude(a => a!.Cultures)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Surname))
        {
            var surname = p.Surname.Trim().ToUpper();
            query = query.Where(x => x.Surname != null && x.Surname.ToUpper().Contains(surname));
        }
        if (!string.IsNullOrWhiteSpace(p.GivenName))
        {
            var given = p.GivenName.Trim().ToUpper();
            query = query.Where(x => x.GivenName != null && x.GivenName.ToUpper().Contains(given));
        }
        if (!string.IsNullOrWhiteSpace(p.DocumentNumber))
        {
            var number = p.DocumentNumber.Trim().ToUpper();
            query = query.Where(x => x.DocumentNumber != null && x.DocumentNumber.ToUpper().Contains(number));
        }
        if (p.CountryId is { } citizenship) query = query.Where(x => x.CitizenshipId == citizenship);

        var entries = await query.OrderBy(x => x.Surname).ThenBy(x => x.GivenName)
            .Take(MaxRows).ToListAsync(ct);

        var tableRows = entries.Select(x => new object?[]
        {
            x.Surname, x.GivenName, D(x.DateOfBirth),
            x.Citizenship is null ? "—" : Description(x.Citizenship.Cultures, "fr"),
            x.DocumentNumber, x.PersonalNumber,
            x.MatchInStopListAction is null ? "—" : $"{x.MatchInStopListAction.Code} — {Description(x.MatchInStopListAction.Cultures, "fr")}",
            D(x.SetUpDate), D(x.CancelDate),
            x.CancelDate is null ? "Active" : "Annulée",
        }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("WL_Col_Surname"), new ReportColumnTemplate("WL_Col_GivenName"),
                    new ReportColumnTemplate("WL_Col_BirthDate"), new ReportColumnTemplate("WL_Col_Citizenship"),
                    new ReportColumnTemplate("WL_Col_Document"), new ReportColumnTemplate("WL_Col_PersonalNumber"),
                    new ReportColumnTemplate("WL_Col_Action"), new ReportColumnTemplate("WL_Col_SetUpDate"),
                    new ReportColumnTemplate("WL_Col_CancelDate"), new ReportColumnTemplate("RP_Col_State"),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Entries", N(entries.Count)),
                new("RP_Sum_Active", N(entries.Count(x => x.CancelDate is null))),
                new("RP_Sum_CancelledEntries", N(entries.Count(x => x.CancelDate is not null))),
            },
        };
    }

    private async Task<ReportTemplate> BuildStopListDocumentsAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.StopListDocuments)!;

        var query = _db.Set<DocumentStopList>().AsNoTracking()
            .Include(x => x.Country).ThenInclude(c => c!.Cultures)
            .Include(x => x.MatchInStopListAction).ThenInclude(a => a!.Cultures)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.DocumentNumber))
        {
            var number = p.DocumentNumber.Trim().ToUpper();
            query = query.Where(x => x.DocumentNumber != null && x.DocumentNumber.ToUpper().Contains(number));
        }
        if (!string.IsNullOrWhiteSpace(p.Surname))
        {
            var surname = p.Surname.Trim().ToUpper();
            query = query.Where(x => x.Surname != null && x.Surname.ToUpper().Contains(surname));
        }
        if (p.CountryId is { } country) query = query.Where(x => x.CountryId == country);

        var entries = await query.OrderBy(x => x.DocumentNumber).Take(MaxRows).ToListAsync(ct);

        var tableRows = entries.Select(x => new object?[]
        {
            x.DocumentNumber, x.PersonalNumber,
            x.Country is null ? "—" : Description(x.Country.Cultures, "fr"),
            x.Surname, x.GivenName, D(x.DateOfBirth),
            x.MatchInStopListAction is null ? "—" : $"{x.MatchInStopListAction.Code} — {Description(x.MatchInStopListAction.Cultures, "fr")}",
            D(x.SetUpDate), D(x.CancelDate),
            x.CancelDate is null ? "Active" : "Annulée",
        }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("WL_Col_Document"), new ReportColumnTemplate("WL_Col_PersonalNumber"),
                    new ReportColumnTemplate("WL_Col_Citizenship"), new ReportColumnTemplate("WL_Col_Surname"),
                    new ReportColumnTemplate("WL_Col_GivenName"), new ReportColumnTemplate("WL_Col_BirthDate"),
                    new ReportColumnTemplate("WL_Col_Action"), new ReportColumnTemplate("WL_Col_SetUpDate"),
                    new ReportColumnTemplate("WL_Col_CancelDate"), new ReportColumnTemplate("RP_Col_State"),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Entries", N(entries.Count)),
                new("RP_Sum_Active", N(entries.Count(x => x.CancelDate is null))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Transport — héritage : GetReportsStatisticsByTransport / GetReportsVesselsStatistics
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildByTransportAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.StatisticsByTransport)!;

        var tableRows = rows.GroupBy(r => r.TransportTypeFr ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[]
            {
                g.Key, N(g.Count()), N(g.Count(IsEntry)), N(g.Count(IsExit)),
                N(g.Select(r => r.Flight).Distinct().Count()), Pct(g.Count(), rows.Count),
            }).ToList();
        tableRows.Add(new object?[]
        {
            "Total", N(rows.Count), N(rows.Count(IsEntry)), N(rows.Count(IsExit)),
            N(rows.Select(r => r.Flight).Distinct().Count()), Pct(rows.Count, rows.Count),
        });

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("BO_Col_Type"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("PAL_Direction_Entry", true),
                    new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_TransportUnits", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_TransportTypes", N(tableRows.Count - 1)),
                new("RP_Sum_Drivers", N(rows.Count(r => r.IsDriver))),
            },
        };
    }

    private async Task<ReportTemplate> BuildVesselsAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = (await LoadMovementsAsync(p, ct))
            .Where(r => r.TransportTypeFr is not null
                && r.TransportTypeFr.Contains("navire", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var definition = ReportCatalog.Find(ReportCatalog.StatisticsVessels)!;

        var groups = rows.GroupBy(r => r.Flight ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();

        var tableRows = groups.Select(g => new object?[]
        {
            g.Key, N(g.Count()), N(g.Count(IsEntry)), N(g.Count(IsExit)),
            N(g.Count(r => r.IsDriver)), N(g.Sum(r => r.Children)),
        }).ToList();
        tableRows.Add(new object?[]
        {
            "Total", N(rows.Count), N(rows.Count(IsEntry)), N(rows.Count(IsExit)),
            N(rows.Count(r => r.IsDriver)), N(rows.Sum(r => r.Children)),
        });

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("BO_Col_Trip"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("PAL_Direction_Entry", true),
                    new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_Drivers", true),
                    new ReportColumnTemplate("RP_Col_Children", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Vessels", N(groups.Count)),
                new("RP_Sum_WithoutVessel", N(rows.Count(r => r.Flight is null))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Échanges — messages APIS par statut
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildApisAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.ApisByStatus)!;

        var query = _db.Set<ApisMessage>().AsNoTracking().Include(m => m.Passengers).AsQueryable();
        if (p.From is { } from) query = query.Where(m => m.CreatedDate >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(m => m.CreatedDate < end);
        }
        if (p.BorderPointId is { } borderPoint) query = query.Where(m => m.BorderPointId == borderPoint);

        var messages = await query.OrderBy(m => m.CreatedDate).Take(MaxRows).ToListAsync(ct);

        var tableRows = messages.GroupBy(m => m.Status)
            .OrderBy(g => g.Key)
            .Select(g => new object?[]
            {
                g.Key switch
                {
                    ApisMessageStatus.Received => "Reçu",
                    ApisMessageStatus.Reconciled => "Rapproché",
                    ApisMessageStatus.Alerted => "Alerte",
                    _ => "Exporté",
                },
                N(g.Count()), N(g.Sum(m => m.Passengers.Count(x => x.IsPassenger))),
                N(g.Sum(m => m.Passengers.Count(x => !x.IsPassenger))),
                N(g.Sum(m => m.Passengers.Count(x => x.Status == ApisPassengerStatus.Doubtful))),
                Pct(g.Count(), messages.Count),
            }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("EX_Col_Status"),
                    new ReportColumnTemplate("RP_Col_Messages", true),
                    new ReportColumnTemplate("RP_Col_Passengers", true),
                    new ReportColumnTemplate("EX_Type_Crew", true),
                    new ReportColumnTemplate("RP_Col_Doubtful", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Messages", N(messages.Count)),
                new("RP_Sum_Passengers", N(messages.Sum(m => m.Passengers.Count(x => x.IsPassenger)))),
                new("RP_Sum_Doubtful", N(messages.Sum(m => m.Passengers.Count(x => x.Status == ApisPassengerStatus.Doubtful)))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Santé — héritage : GetReportsQuestionnaireStatistics
    // -----------------------------------------------------------------------

    private async Task<List<AppliedRow>> LoadAppliedQuestionnairesAsync(ReportParameters p, CancellationToken ct)
    {
        var query = _db.Set<AppliedQuestionnaire>().AsNoTracking()
            .Include(a => a.Questionnaire)
            .Include(a => a.Document)
            .Include(a => a.Results).ThenInclude(r => r.QuestionAnswer)
            .AsQueryable();

        if (p.From is { } from) query = query.Where(a => a.AppliedDate >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(a => a.AppliedDate < end);
        }
        if (p.BorderPointId is { } borderPoint) query = query.Where(a => a.BorderPointId == borderPoint);

        var applications = await query.OrderBy(a => a.AppliedDate).Take(MaxRows).ToListAsync(ct);
        var endings = await _db.Set<QuestionAnswerQuestionnaireEnding>().AsNoTracking()
            .Include(x => x.EndingMessage).ToListAsync(ct);

        var now = _clock.Now;
        return applications.Select(a => new AppliedRow(
            a.Id,
            a.Questionnaire?.Description ?? "—",
            a.AppliedDate,
            a.ExpiresAt,
            a.IsExpired(now),
            a.Document?.DocumentNumber,
            PersonName(a.Document?.Surname, a.Document?.GivenName),
            a.Results.Count,
            EndingFor(a, endings)?.EndingMessage?.Title)).ToList();
    }

    private sealed record AppliedRow(int Id, string Questionnaire, DateTime AppliedDate, DateTime? ExpiresAt,
        bool Expired, string? DocumentNumber, string? Person, int AnswerCount, string? Ending);

    /// <summary>Message de fin applicable (héritage : QuestionAnswerQuestionnaireEndings) :
    /// première règle satisfaite dans l'ordre des réponses enregistrées.</summary>
    private static QuestionAnswerQuestionnaireEnding? EndingFor(AppliedQuestionnaire applied,
        IReadOnlyList<QuestionAnswerQuestionnaireEnding> endings)
    {
        foreach (var result in applied.Results.OrderBy(r => r.Id))
        {
            var ending = endings.FirstOrDefault(x => x.QuestionAnswerId == result.QuestionAnswerId);
            if (ending is not null) return ending;
        }

        return null;
    }

    private async Task<ReportTemplate> BuildQuestionnaireStatisticsAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadAppliedQuestionnairesAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.QuestionnaireStatistics)!;

        var tableRows = rows.GroupBy(r => r.Questionnaire)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[]
            {
                g.Key, N(g.Count()), N(g.Select(r => r.DocumentNumber).Distinct().Count()),
                N(g.Count(r => r.Expired)), g.Average(r => r.AnswerCount).ToString("N1", Format),
                N(g.Count(r => r.Ending is not null)), Pct(g.Count(), rows.Count),
            }).ToList();
        tableRows.Add(new object?[]
        {
            "Total", N(rows.Count), N(rows.Select(r => r.DocumentNumber).Distinct().Count()),
            N(rows.Count(r => r.Expired)),
            rows.Count == 0 ? "—" : rows.Average(r => r.AnswerCount).ToString("N1", Format),
            N(rows.Count(r => r.Ending is not null)), Pct(rows.Count, rows.Count),
        });

        var endings = rows.Where(r => r.Ending is not null).GroupBy(r => r.Ending!)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new ReportTableTemplate(null, new[]
            {
                new ReportColumnTemplate("RP_Col_EndingMessage"),
                new ReportColumnTemplate("RP_Col_Count", true),
                new ReportColumnTemplate("RP_Col_Share", true),
            }, new List<object?[]>
            {
                new object?[]
                {
                    g.Key, N(g.Count()), Pct(g.Count(), rows.Count(r => r.Ending is not null)),
                },
            })).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_Questionnaire"),
                    new ReportColumnTemplate("RP_Col_Applied", true),
                    new ReportColumnTemplate("RP_Col_DistinctDocuments", true),
                    new ReportColumnTemplate("RP_Col_Expired", true),
                    new ReportColumnTemplate("RP_Col_AverageAnswers", true),
                    new ReportColumnTemplate("RP_Col_WithEnding", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows),
            }.Concat(endings).ToArray(),
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Questionnaires", N(tableRows.Count - 1)),
                new("RP_Sum_Valid", N(rows.Count(r => !r.Expired))),
                new("RP_Sum_Expired", N(rows.Count(r => r.Expired))),
            },
        };
    }

    /// <summary>Répartition des réponses par question (toutes applications de la période).</summary>
    private async Task<ReportTemplate> BuildQuestionnaireAnswersAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.QuestionnaireAnswers)!;

        var query = _db.Set<QuestionnaireResult>().AsNoTracking()
            .Include(r => r.QuestionAnswer).ThenInclude(a => a!.Question)
            .Include(r => r.AppliedQuestionnaire)
            .AsQueryable();

        if (p.From is { } from) query = query.Where(r => (r.AnsweredAt ?? r.AppliedQuestionnaire!.AppliedDate) >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(r => (r.AnsweredAt ?? r.AppliedQuestionnaire!.AppliedDate) < end);
        }

        var results = await query.Take(MaxRows).ToListAsync(ct);

        // Répartition par question : une ligne par réponse, regroupée par question.
        var tables = results
            .GroupBy(r => r.QuestionAnswer?.Question?.Title ?? "—")
            .OrderBy(g => g.Key)
            .Select(g => new ReportTableTemplate(g.Key, new[]
            {
                new ReportColumnTemplate("RP_Col_Answer"),
                new ReportColumnTemplate("RP_Col_Count", true),
                new ReportColumnTemplate("RP_Col_Share", true),
            }, g.GroupBy(r => r.QuestionAnswer?.Answer ?? "—")
                .OrderByDescending(x => x.Count()).ThenBy(x => x.Key)
                .Select(x => new object?[]
                {
                    x.Key, N(x.Count()), Pct(x.Count(), g.Count()),
                }).ToList()))
            .ToList();

        if (tables.Count == 0)
            tables.Add(new ReportTableTemplate("RP_Table_NoAnswers", new[]
            {
                new ReportColumnTemplate("RP_Col_Answer"),
            }, Array.Empty<object?[]>()));

        return Template(definition, p) with
        {
            Tables = tables,
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_AnsweredQuestions", N(results.Select(r => r.QuestionAnswer?.QuestionId).Distinct().Count())),
                new("RP_Sum_Answers", N(results.Count)),
                new("RP_Sum_OtherAnswers", N(results.Count(r => !string.IsNullOrWhiteSpace(r.OtherAnswer)))),
            },
        };
    }
}
