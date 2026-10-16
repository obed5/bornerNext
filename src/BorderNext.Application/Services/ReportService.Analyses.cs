using BorderNext.Application.Reporting;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Éditions d'analyse : mouvements (1.x), secondes inspections (2.x),
/// nationalités (3.x), profils (4.x), transport, échanges et santé.
///
/// Convention de libellés : les en-têtes fixes sont des clés i18n ; un en-tête
/// préfixé « @ » est un texte littéral (ex. nom de nationalité en colonne) ; les
/// cellules de données sont des valeurs déjà mises en forme (français du poste).</summary>
public partial class ReportService
{
    private const string Literal = "@";

    // -----------------------------------------------------------------------
    // 1.1 — Synthèse des mouvements
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildMovementSummaryAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.MovementSummary)!;

        object?[] Line(string label, List<MovRow> subset) => new object?[]
        {
            label, N(subset.Count), N(subset.Count(r => r.Granted)), N(subset.Count(r => !r.Granted)),
            N(subset.Count(r => r.IsTransit)), N(subset.Select(r => r.DocumentNumber).Distinct().Count()),
            N(subset.Sum(r => r.Children)),
        };

        var tableRows = new List<object?[]>
        {
            Line("Entrées", rows.Where(IsEntry).ToList()),
            Line("Sorties", rows.Where(IsExit).ToList()),
            Line("Total", rows),
        };

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("PAL_Col_Direction"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("RP_Col_Granted", true),
                    new ReportColumnTemplate("RP_Col_Refused", true),
                    new ReportColumnTemplate("RP_Col_Transit", true),
                    new ReportColumnTemplate("RP_Col_DistinctDocuments", true),
                    new ReportColumnTemplate("RP_Col_Children", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_DistinctPersons", N(rows.Select(r => $"{r.Surname}|{r.GivenName}|{r.BirthDate?.Ticks}").Distinct().Count())),
                new("RP_Sum_RefusalRate", Pct(rows.Count(r => !r.Granted), rows.Count)),
                new("RP_Sum_WithVisa", N(rows.Count(r => r.HasVisa))),
                new("RP_Sum_Drivers", N(rows.Count(r => r.IsDriver))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 1.2 — Mouvements par poste frontière
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildByBorderPointAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.MovementsByBorderPoint)!;

        // Tous les postes sont listés, y compris sans activité (comme l'écran d'accueil).
        var points = await _db.Set<BorderPoint>().AsNoTracking().Include(b => b.Cultures).ToListAsync(ct);
        var names = points.Select(b => new
        {
            Name = Description(b.Cultures, "fr") ?? b.ExternalId,
            ExternalId = b.ExternalId,
        }).OrderBy(b => b.Name).ToList();

        var tableRows = new List<object?[]>();
        foreach (var point in names)
        {
            var subset = rows.Where(r => r.BorderPointName == point.Name || r.BorderPointName == point.ExternalId).ToList();
            tableRows.Add(new object?[]
            {
                point.Name, N(subset.Count), N(subset.Count(IsEntry)), N(subset.Count(IsExit)),
                N(subset.Count(r => !r.Granted)), N(subset.Select(r => r.DocumentNumber).Distinct().Count()),
            });
        }

        tableRows.Add(new object?[]
        {
            "Total", N(rows.Count), N(rows.Count(IsEntry)), N(rows.Count(IsExit)),
            N(rows.Count(r => !r.Granted)), N(rows.Select(r => r.DocumentNumber).Distinct().Count()),
        });

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("ADM_Col_BorderPoint"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("PAL_Direction_Entry", true),
                    new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_Refused", true),
                    new ReportColumnTemplate("RP_Col_DistinctDocuments", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_BorderPoints", N(names.Count)),
                new("RP_Sum_ActivePosts", N(names.Count(n => rows.Any(r => r.BorderPointName == n.Name || r.BorderPointName == n.ExternalId)))),
                new("RP_Sum_Unassigned", N(rows.Count(r => r.BorderPointName is null))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 1.3 / 1.4 — Mouvements journaliers et mensuels
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildDailyAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.MovementsDaily)!;

        var tableRows = rows.GroupBy(r => r.Date.Date).OrderBy(g => g.Key)
            .Select(g => new object?[]
            {
                D(g.Key), N(g.Count()), N(g.Count(IsEntry)), N(g.Count(IsExit)),
                N(g.Count(r => !r.Granted)), N(g.Sum(r => r.Children)),
            }).ToList();

        tableRows.Add(new object?[]
        {
            "Total", N(rows.Count), N(rows.Count(IsEntry)), N(rows.Count(IsExit)),
            N(rows.Count(r => !r.Granted)), N(rows.Sum(r => r.Children)),
        });

        var days = rows.Select(r => r.Date.Date).Distinct().Count();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("Admin_Col_Date"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("PAL_Direction_Entry", true),
                    new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_Refused", true),
                    new ReportColumnTemplate("RP_Col_Children", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Days", N(days)),
                new("RP_Sum_DailyAverage", days == 0 ? "—" : N(rows.Count / days)),
                new("RP_Sum_PeakDay", rows.Count == 0 ? "—" : $"{D(rows.GroupBy(r => r.Date.Date).OrderByDescending(g => g.Count()).First().Key)} ({N(rows.GroupBy(r => r.Date.Date).Max(g => g.Count()))})"),
            },
        };
    }

    private async Task<ReportTemplate> BuildMonthlyAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.MovementsMonthly)!;

        var groups = rows.GroupBy(r => MonthKey(r.Date)).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var tableRows = groups.Select(g => new object?[]
        {
            g.Key, N(g.Count()), N(g.Count(IsEntry)), N(g.Count(IsExit)),
            N(g.Count(r => !r.Granted)), N(g.Sum(r => r.Children)),
        }).ToList();

        tableRows.Add(new object?[]
        {
            "Total", N(rows.Count), N(rows.Count(IsEntry)), N(rows.Count(IsExit)),
            N(rows.Count(r => !r.Granted)), N(rows.Sum(r => r.Children)),
        });

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_Month"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("PAL_Direction_Entry", true),
                    new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_Refused", true),
                    new ReportColumnTemplate("RP_Col_Children", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Months", N(groups.Count)),
                new("RP_Sum_MonthlyAverage", groups.Count == 0 ? "—" : N(rows.Count / groups.Count)),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 1.5 — Registre détaillé par condition
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildRegisterAsync(ReportParameters p, CancellationToken ct)
    {
        // Le filtre « enfants » ne s'applique pas au registre : l'option ajoute
        // seulement une colonne au document.
        var rows = await LoadMovementsAsync(p with { IncludeChildren = false }, ct);
        var definition = ReportCatalog.Find(ReportCatalog.MovementRegister)!;

        var columns = new List<ReportColumnTemplate>
        {
            new("RP_Col_TravelDate"), new("WL_Col_Surname"), new("WL_Col_GivenName"),
            new("WL_Col_Document"), new("WL_Col_BirthDate"), new("WL_Col_Citizenship"),
            new("PAL_Col_Direction"), new("RP_Col_TravelReason"), new("BO_Col_Trip"),
            new("RP_Col_Granted"), new("RP_Filter_Inspector"),
        };
        if (p.IncludeChildren) columns.Add(new ReportColumnTemplate("RP_Col_Children", true));

        var tableRows = rows.Select(r =>
        {
            var cells = new List<object?>
            {
                D(r.Date), r.Surname, r.GivenName, r.DocumentNumber, D(r.BirthDate), r.CitizenshipName,
                r.DirectionFr, r.TravelReasonFr, r.Flight, r.Granted ? "Oui" : "Non", r.Inspector,
            };
            if (p.IncludeChildren) cells.Add(N(r.Children));
            return cells.ToArray();
        }).ToList();

        return Template(definition, p) with
        {
            Tables = new[] { Table("RP_Table_Register", columns.ToArray(), tableRows, null) },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Records", N(rows.Count)),
                new("RP_Sum_Refused", N(rows.Count(r => !r.Granted))),
                new("RP_Sum_Children", N(rows.Sum(r => r.Children))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 1.6 — Enfants rattachés
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildChildrenAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p with { IncludeChildren = true }, ct);
        var definition = ReportCatalog.Find(ReportCatalog.MovementChildren)!;

        var tableRows = rows.Select(r => new object?[]
        {
            D(r.Date), PersonName(r.Surname, r.GivenName), r.DocumentNumber, r.CitizenshipName,
            N(r.Children), r.DirectionFr, r.Flight,
        }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_TravelDate"), new ReportColumnTemplate("PAL_Col_Holder"),
                    new ReportColumnTemplate("WL_Col_Document"), new ReportColumnTemplate("WL_Col_Citizenship"),
                    new ReportColumnTemplate("RP_Col_Children", true), new ReportColumnTemplate("PAL_Col_Direction"),
                    new ReportColumnTemplate("BO_Col_Trip"),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_TravellersWithChildren", N(tableRows.Count)),
                new("RP_Sum_Children", N(rows.Sum(r => r.Children))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 1.7 — Statistiques passagers (par vol)
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildPassengersAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.MovementPassengers)!;

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
                    new ReportColumnTemplate("BO_Col_Trip"), new ReportColumnTemplate("RP_Col_Passengers", true),
                    new ReportColumnTemplate("PAL_Direction_Entry", true), new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_Drivers", true), new ReportColumnTemplate("RP_Col_Children", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Flights", N(groups.Count)),
                new("RP_Sum_WithoutFlight", N(rows.Count(r => r.Flight is null))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 2.1–2.3 — Secondes inspections (personnes douteuses)
    // -----------------------------------------------------------------------

    private sealed record DoubtRow(int Id, DateTime Date, string Person, string? DocumentNumber,
        string? Citizenship, string? Reason, short WorkflowStatus, string? Supervisor, string? Note);

    private async Task<List<DoubtRow>> LoadDoubtsAsync(ReportParameters p, CancellationToken ct)
    {
        var query = _db.Set<DoubtablePerson>().AsNoTracking()
            .Include(d => d.Movement!).ThenInclude(m => m.Document)
            .Include(d => d.CheckingReason).ThenInclude(r => r!.Cultures)
                        .AsQueryable();

        if (p.From is { } from) query = query.Where(d => d.CreateDate >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(d => d.CreateDate < end);
        }
        if (p.BorderPointId is { } borderPoint) query = query.Where(d => d.BorderPointId == borderPoint);

        var doubts = await query.OrderBy(d => d.CreateDate).Take(MaxRows).ToListAsync(ct);
        var countries = await _db.Set<Country>().AsNoTracking().Include(c => c.Cultures)
            .ToDictionaryAsync(c => c.Id, ct);

        return doubts.Select(d => new DoubtRow(
            d.Id,
            d.CreateDate,
            PersonName(d.Movement?.Document?.Surname, d.Movement?.Document?.GivenName),
            d.Movement?.Document?.DocumentNumber,
            d.Movement?.Document?.CitizenshipId is { } c && countries.TryGetValue(c, out var country)
                ? Description(country.Cultures, "fr") : null,
            Description(d.CheckingReason?.Cultures, "fr") ?? d.CheckingReason?.Code,
            d.WorkflowStatus,
            d.Supervisor,
            d.SupervisorNote)).ToList();
    }

    private static string DoubtStatusLabel(short status) => status switch
    {
        DoubtfulWorkflowStatus.Pending => "En attente",
        DoubtfulWorkflowStatus.Processed => "Traitée",
        _ => "Annulée",
    };

    private async Task<ReportTemplate> BuildDoubtfulListAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadDoubtsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.DoubtfulList)!;

        var tableRows = rows.Select(r => new object?[]
        {
            D(r.Date), r.Person, r.DocumentNumber, r.Citizenship, r.Reason,
            DoubtStatusLabel(r.WorkflowStatus), r.Supervisor, r.Note,
        }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("Admin_Col_Date"), new ReportColumnTemplate("PAL_Col_Holder"),
                    new ReportColumnTemplate("WL_Col_Document"), new ReportColumnTemplate("WL_Col_Citizenship"),
                    new ReportColumnTemplate("EX_Col_Reason"), new ReportColumnTemplate("EX_Col_ActionStatus"),
                    new ReportColumnTemplate("EX_Col_Supervisor"), new ReportColumnTemplate("EX_Col_Note"),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_SecondaryInspections", N(rows.Count)),
                new("RP_Sum_Pending", N(rows.Count(r => r.WorkflowStatus == DoubtfulWorkflowStatus.Pending))),
                new("RP_Sum_Processed", N(rows.Count(r => r.WorkflowStatus == DoubtfulWorkflowStatus.Processed))),
                new("RP_Sum_Cancelled", N(rows.Count(r => r.WorkflowStatus == DoubtfulWorkflowStatus.Cancelled))),
            },
        };
    }

    private async Task<ReportTemplate> BuildDoubtfulByNationalityAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadDoubtsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.DoubtfulByNationality)!;

        var tableRows = rows.GroupBy(r => r.Citizenship ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[] { g.Key, N(g.Count()), Pct(g.Count(), rows.Count) })
            .ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("WL_Col_Citizenship"),
                    new ReportColumnTemplate("RP_Col_Inspections", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows, new object?[] { "Total", N(rows.Count), Pct(rows.Count, rows.Count) }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Nationalities", N(tableRows.Count)),
            },
        };
    }

    private async Task<ReportTemplate> BuildDoubtfulByReasonAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadDoubtsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.DoubtfulByReason)!;

        var tableRows = rows.GroupBy(r => r.Reason ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[] { g.Key, N(g.Count()), Pct(g.Count(), rows.Count) })
            .ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("EX_Col_Reason"),
                    new ReportColumnTemplate("RP_Col_Inspections", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows, new object?[] { "Total", N(rows.Count), Pct(rows.Count, rows.Count) }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Reasons", N(tableRows.Count)),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 3.1 — Nationalités : synthèse
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildNationalitiesSummaryAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.NationalitiesSummary)!;

        var tableRows = rows.GroupBy(r => r.CitizenshipName ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[]
            {
                g.Key, N(g.Count()), N(g.Count(IsEntry)), N(g.Count(IsExit)),
                N(g.Count(r => !r.Granted)), Pct(g.Count(), rows.Count),
            }).ToList();
        tableRows.Add(new object?[]
        {
            "Total", N(rows.Count), N(rows.Count(IsEntry)), N(rows.Count(IsExit)),
            N(rows.Count(r => !r.Granted)), Pct(rows.Count, rows.Count),
        });

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("WL_Col_Citizenship"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("PAL_Direction_Entry", true),
                    new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_Refused", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Nationalities", N(tableRows.Count - 1)),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 3.2 — Nationalités par sens
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildNationalitiesByDirectionAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.NationalitiesByDirection)!;

        var groups = rows.GroupBy(r => r.CitizenshipName ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();

        var tableRows = groups.Select(g => new object?[]
        {
            g.Key, N(g.Count(IsEntry)), N(g.Count(IsExit)), N(g.Count()),
            N(Math.Abs(g.Count(IsEntry) - g.Count(IsExit))),
        }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("WL_Col_Citizenship"),
                    new ReportColumnTemplate("PAL_Direction_Entry", true),
                    new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("RP_Col_BalanceAbsolute", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_DirectionBalanced", N(groups.Count(g => g.Count(IsEntry) == g.Count(IsExit)))),
                new("RP_Sum_MostlyEntries", N(groups.Count(g => g.Count(IsEntry) > g.Count(IsExit)))),
                new("RP_Sum_MostlyExits", N(groups.Count(g => g.Count(IsExit) > g.Count(IsEntry)))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 3.3 — Nationalités par type de document
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildNationalitiesByDocumentTypeAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.NationalitiesByDocumentType)!;

        var tableRows = rows.GroupBy(r => r.DocumentTypeFr ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g =>
            {
                var top = g.GroupBy(r => r.CitizenshipName ?? "—")
                    .OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First();
                return new object?[]
                {
                    g.Key, N(g.Count()), N(g.Select(r => r.CitizenshipName).Distinct().Count()),
                    $"{top.Key} ({N(top.Count())})", Pct(g.Count(), rows.Count),
                };
            }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_DocumentType"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("RP_Col_DistinctNationalities", true),
                    new ReportColumnTemplate("RP_Col_TopNationality"),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows, new object?[] { "Total", N(rows.Count), null, null, Pct(rows.Count, rows.Count) }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_DocumentTypes", N(tableRows.Count)),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 3.4 — Tendances : mois × principales nationalités
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildNationalitiesTrendsAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.NationalitiesTrends)!;

        var top = rows.GroupBy(r => r.CitizenshipName ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(6).Select(g => g.Key).ToList();
        var months = rows.Select(r => MonthKey(r.Date)).Distinct().OrderBy(m => m, StringComparer.Ordinal).ToList();

        var tableRows = new List<object?[]>();
        foreach (var month in months)
        {
            var subset = rows.Where(r => MonthKey(r.Date) == month).ToList();
            tableRows.Add(new object?[] { month }
                .Concat(top.Select(nat => (object?)N(subset.Count(r => (r.CitizenshipName ?? "—") == nat))))
                .Concat(new object?[] { N(subset.Count) }).ToArray());
        }

        tableRows.Add(new object?[] { "Total" }
            .Concat(top.Select(nat => (object?)N(rows.Count(r => (r.CitizenshipName ?? "—") == nat))))
            .Concat(new object?[] { N(rows.Count) }).ToArray());

        var columns = new List<ReportColumnTemplate> { new("RP_Col_Month") };
        columns.AddRange(top.Select(nat => new ReportColumnTemplate(Literal + nat, true)));
        columns.Add(new ReportColumnTemplate("RP_Total", true));

        return Template(definition, p) with
        {
            Tables = new[] { Table("RP_Table_Trends", columns.ToArray(), tableRows, null) },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_LeadingNationalities", string.Join(" · ", top.Take(3))),
                new("RP_Sum_Months", N(months.Count)),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 3.5 — Nationalités par motif de voyage
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildNationalitiesByTravelReasonAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.NationalitiesByTravelReason)!;

        var reasons = rows.GroupBy(r => r.TravelReasonFr ?? "—")
            .OrderByDescending(g => g.Count()).Take(6).Select(g => g.Key).ToList();
        var nationalities = rows.GroupBy(r => r.CitizenshipName ?? "—")
            .OrderByDescending(g => g.Count()).Take(10).Select(g => g.Key).ToList();

        var tableRows = nationalities.Select(nat =>
        {
            var subset = rows.Where(r => (r.CitizenshipName ?? "—") == nat).ToList();
            return new object?[] { nat }
                .Concat(reasons.Select(reason => (object?)N(subset.Count(r => (r.TravelReasonFr ?? "—") == reason))))
                .Concat(new object?[] { N(subset.Count) }).ToArray();
        }).ToList();

        var columns = new List<ReportColumnTemplate> { new("WL_Col_Citizenship") };
        columns.AddRange(reasons.Select(reason => new ReportColumnTemplate(Literal + reason, true)));
        columns.Add(new ReportColumnTemplate("RP_Total", true));

        return Template(definition, p) with
        {
            Tables = new[] { Table("RP_Table_NationalityReasons", columns.ToArray(), tableRows, null) },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Reasons", N(reasons.Count)),
                new("RP_Sum_NationalitiesShown", N(nationalities.Count)),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 3.6 — Données cartographiques des nationalités
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildNationalitiesMapAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.NationalitiesMapData)!;

        var points = await _db.Set<CountryGeoPoint>().AsNoTracking().ToListAsync(ct);
        var byIso3 = points.Where(pt => !string.IsNullOrEmpty(pt.Iso3))
            .GroupBy(pt => pt.Iso3!).ToDictionary(g => g.Key, g => g.First());

        var tableRows = rows.GroupBy(r => r.CitizenshipName ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g =>
            {
                var sample = rows.First(r => (r.CitizenshipName ?? "—") == g.Key);
                var point = sample.NationalityAlpha3 is { } iso3 && byIso3.TryGetValue(iso3, out var found)
                    ? found : null;
                return new object?[]
                {
                    g.Key, sample.NationalityAlpha2, sample.NationalityAlpha3, N(g.Count()),
                    point?.Latitude.ToString("0.0000", Format) ?? "—",
                    point?.Longitude.ToString("0.0000", Format) ?? "—",
                };
            }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("WL_Col_Citizenship"), new ReportColumnTemplate("RP_Col_Iso2"),
                    new ReportColumnTemplate("RP_Col_Iso3"), new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("RP_Col_Latitude", true), new ReportColumnTemplate("RP_Col_Longitude", true),
                }, tableRows),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_LocatedCountries", N(tableRows.Count(r => (string?)r[4] != "—"))),
                new("RP_Sum_NotLocated", N(tableRows.Count(r => (string?)r[4] == "—"))),
            },
        };
    }

    // -----------------------------------------------------------------------
    // 4.1 / 4.4 / 4.5 — Motifs, types de documents, résidence
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildTravelReasonsAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.TravelReasons)!;

        var tableRows = rows.GroupBy(r => r.TravelReasonFr ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[]
            {
                g.Key, N(g.Count()), N(g.Count(IsEntry)), N(g.Count(IsExit)), Pct(g.Count(), rows.Count),
            }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_TravelReason"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("PAL_Direction_Entry", true),
                    new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows, new object?[] { "Total", N(rows.Count), N(rows.Count(IsEntry)), N(rows.Count(IsExit)), Pct(rows.Count, rows.Count) }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Unspecified", N(rows.Count(r => r.TravelReasonFr is null))),
            },
        };
    }

    private async Task<ReportTemplate> BuildDocumentTypesAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.DocumentTypes)!;

        var tableRows = rows.GroupBy(r => r.DocumentTypeFr ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[]
            {
                g.Key, N(g.Count()), N(g.Count(IsEntry)), N(g.Count(IsExit)), Pct(g.Count(), rows.Count),
            }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_DocumentType"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("PAL_Direction_Entry", true),
                    new ReportColumnTemplate("PAL_Direction_Exit", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows, new object?[] { "Total", N(rows.Count), N(rows.Count(IsEntry)), N(rows.Count(IsExit)), Pct(rows.Count, rows.Count) }),
            },
            Summary = new List<ReportSummaryLine>(),
        };
    }

    private async Task<ReportTemplate> BuildResidenceAsync(ReportParameters p, CancellationToken ct)
    {
        var rows = await LoadMovementsAsync(p, ct);
        var definition = ReportCatalog.Find(ReportCatalog.CountriesOfResidence)!;

        var tableRows = rows.GroupBy(r => r.ResidenceName ?? "—")
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new object?[] { g.Key, N(g.Count()), Pct(g.Count(), rows.Count) })
            .ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                Table(null, new[]
                {
                    new ReportColumnTemplate("RP_Col_Residence"),
                    new ReportColumnTemplate("RP_Col_Movements", true),
                    new ReportColumnTemplate("RP_Col_Share", true),
                }, tableRows, new object?[] { "Total", N(rows.Count), Pct(rows.Count, rows.Count) }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_ResidenceCountries", N(tableRows.Count)),
                new("RP_Sum_WithoutResidence", N(rows.Count(r => r.ResidenceName is null))),
            },
        };
    }
}
