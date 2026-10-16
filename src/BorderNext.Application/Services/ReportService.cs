using System.Globalization;
using BorderNext.Application.Abstractions;
using BorderNext.Application.Reporting;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>
/// Éditions du poste (héritage : formulaire d'édition et procédures GetReports* /
/// GetReport* / GetDailyReports*) : le service construit le document (critères,
/// tableaux, synthèse) pour chaque édition du catalogue, à partir des mouvements,
/// dossiers, permis, visas, listes d'alerte, messages et questionnaires.
/// L'agrégation se fait en mémoire sur la période demandée (bornée), ce qui garde
/// les mêmes résultats d'un fournisseur de base à l'autre.
/// </summary>
public partial class ReportService
{
    /// <summary>Culture de mise en forme des éditions (langue principale du produit).</summary>
    private static readonly CultureInfo Format = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Plafond d'éléments chargés par édition (les synthèses restent exactes
    /// sur les volumes courants d'un poste ; au-delà, réduire la période).</summary>
    public const int MaxRows = 20000;

    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public ReportService(DbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public IReadOnlyList<ReportDefinition> Catalog => ReportCatalog.All;

    /// <summary>Construit l'édition demandée.</summary>
    public async Task<ReportTemplate> BuildAsync(string reportId, ReportParameters parameters,
        CancellationToken ct = default)
    {
        var definition = ReportCatalog.Find(reportId)
            ?? throw new DomainValidationException("RP_Error_UnknownReport");

        var document = reportId switch
        {
            ReportCatalog.RegistrationForm => await BuildRegistrationFormAsync(parameters, ct),
            ReportCatalog.VisaForm => await BuildVisaFormAsync(parameters, ct),
            ReportCatalog.PermitForm => await BuildPermitFormAsync(parameters, ct),
            ReportCatalog.RefusalForm => await BuildRefusalFormAsync(parameters, ct),
            ReportCatalog.PenaltyForm => await BuildPenaltyFormAsync(parameters, ct),
            ReportCatalog.RemovalOrderForm => await BuildRemovalOrderFormAsync(parameters, ct),
            ReportCatalog.MedicalHistoryForm => await BuildMedicalHistoryFormAsync(parameters, ct),
            ReportCatalog.BlankQuestionnaire => await BuildBlankQuestionnaireAsync(parameters, ct),

            ReportCatalog.MovementSummary => await BuildMovementSummaryAsync(parameters, ct),
            ReportCatalog.MovementsByBorderPoint => await BuildByBorderPointAsync(parameters, ct),
            ReportCatalog.MovementsDaily => await BuildDailyAsync(parameters, ct),
            ReportCatalog.MovementsMonthly => await BuildMonthlyAsync(parameters, ct),
            ReportCatalog.MovementRegister => await BuildRegisterAsync(parameters, ct),
            ReportCatalog.MovementChildren => await BuildChildrenAsync(parameters, ct),
            ReportCatalog.MovementPassengers => await BuildPassengersAsync(parameters, ct),

            ReportCatalog.DoubtfulList => await BuildDoubtfulListAsync(parameters, ct),
            ReportCatalog.DoubtfulByNationality => await BuildDoubtfulByNationalityAsync(parameters, ct),
            ReportCatalog.DoubtfulByReason => await BuildDoubtfulByReasonAsync(parameters, ct),

            ReportCatalog.NationalitiesSummary => await BuildNationalitiesSummaryAsync(parameters, ct),
            ReportCatalog.NationalitiesByDirection => await BuildNationalitiesByDirectionAsync(parameters, ct),
            ReportCatalog.NationalitiesByDocumentType => await BuildNationalitiesByDocumentTypeAsync(parameters, ct),
            ReportCatalog.NationalitiesTrends => await BuildNationalitiesTrendsAsync(parameters, ct),
            ReportCatalog.NationalitiesByTravelReason => await BuildNationalitiesByTravelReasonAsync(parameters, ct),
            ReportCatalog.NationalitiesMapData => await BuildNationalitiesMapAsync(parameters, ct),

            ReportCatalog.TravelReasons => await BuildTravelReasonsAsync(parameters, ct),
            ReportCatalog.DocumentTypes => await BuildDocumentTypesAsync(parameters, ct),
            ReportCatalog.CountriesOfResidence => await BuildResidenceAsync(parameters, ct),
            ReportCatalog.ForeignersSummary => await BuildForeignersSummaryAsync(parameters, ct),
            ReportCatalog.ForeignersByAge => await BuildForeignersByCriterionAsync(parameters, "age", ct),
            ReportCatalog.ForeignersByMaritalStatus => await BuildForeignersByCriterionAsync(parameters, "marital", ct),
            ReportCatalog.ForeignersByOccupation => await BuildForeignersByCriterionAsync(parameters, "occupation", ct),
            ReportCatalog.ForeignersByProvince => await BuildForeignersByCriterionAsync(parameters, "province", ct),
            ReportCatalog.ForeignersByQualification => await BuildForeignersByCriterionAsync(parameters, "qualification", ct),
            ReportCatalog.ForeignersMedical => await BuildForeignersMedicalAsync(parameters, ct),
            ReportCatalog.ForeignersSecurity => await BuildForeignersSecurityAsync(parameters, ct),

            ReportCatalog.VisaStatistics => await BuildVisaStatisticsAsync(parameters, ct),
            ReportCatalog.StopListPersons => await BuildStopListPersonsAsync(parameters, ct),
            ReportCatalog.StopListDocuments => await BuildStopListDocumentsAsync(parameters, ct),

            ReportCatalog.StatisticsByTransport => await BuildByTransportAsync(parameters, ct),
            ReportCatalog.StatisticsVessels => await BuildVesselsAsync(parameters, ct),

            ReportCatalog.ApisByStatus => await BuildApisAsync(parameters, ct),
            ReportCatalog.QuestionnaireStatistics => await BuildQuestionnaireStatisticsAsync(parameters, ct),
            ReportCatalog.QuestionnaireAnswers => await BuildQuestionnaireAnswersAsync(parameters, ct),

            _ => throw new DomainValidationException("RP_Error_UnknownReport"),
        };

        return document with
        {
            TitleKey = document.TitleKey.Length > 0 ? document.TitleKey : definition.NameKey,
        };
    }

    // -----------------------------------------------------------------------
    // Chargement des mouvements (socle commun des analyses)
    // -----------------------------------------------------------------------

    /// <summary>Ligne de mouvement allégée, résolue en mémoire (référentiels bornés).</summary>
    private sealed record MovRow(
        int Id,
        DateTime Date,
        DateTime RegistrationTime,
        string? DirectionCode,
        string? DirectionFr,
        string? NationalityAlpha2,
        string? NationalityAlpha3,
        string? CitizenshipName,
        string? DocumentTypeFr,
        string? TravelReasonFr,
        string? ResidenceName,
        string? BorderPointName,
        string? Flight,
        bool Granted,
        bool IsTransit,
        bool IsDriver,
        int Children,
        string? Inspector,
        string? Surname,
        string? GivenName,
        string? DocumentNumber,
        DateTime? BirthDate,
        string? TransportTypeFr,
        string? RefusalFr,
        bool HasVisa);

    private async Task<List<MovRow>> LoadMovementsAsync(ReportParameters p, CancellationToken ct)
    {
        var query = _db.Set<Movement>().AsNoTracking()
            .Where(m => true);
        query = ApplyFilters(query, p);

        var raw = await query
            .OrderBy(m => m.TravelDate).ThenBy(m => m.Id)
            .Take(MaxRows)
            .Select(m => new
            {
                m.Id,
                m.TravelDate,
                m.RegistrationTime,
                m.DirectionLookupId,
                m.TransportTypeId,
                m.TravelReasonId,
                m.ResidentCountryId,
                m.BorderPointId,
                m.Granted,
                m.IsTransit,
                m.IsDriver,
                m.ChildrenCount,
                m.Inspector,
                m.HasVisa,
                m.RefusalJustificationId,
                DocumentNumber = m.Document!.DocumentNumber,
                Surname = m.Document!.Surname,
                GivenName = m.Document!.GivenName,
                BirthDate = m.Document!.DateOfBirth,
                DocumentTypeId = m.Document!.DocumentTypeId,
                CitizenshipId = m.Document!.CitizenshipId,
                Flight = m.TripCrossing!.Trip!.ExternalNumber ?? m.TripCrossing!.Trip!.Name,
            })
            .ToListAsync(ct);

        var lookups = await _db.Set<Lookup>().AsNoTracking()
            .Include(l => l.Cultures)
            .ToDictionaryAsync(l => l.Id, ct);
        var countries = await _db.Set<Country>().AsNoTracking()
            .Include(c => c.Cultures)
            .ToDictionaryAsync(c => c.Id, ct);
        var borderPoints = await _db.Set<BorderPoint>().AsNoTracking()
            .Include(b => b.Cultures)
            .ToListAsync(ct);

        string? LookupFr(int? id) => id is { } key && lookups.TryGetValue(key, out var lookup)
            ? Description(lookup.Cultures, "fr") : null;

        string? CountryName(int? id) => id is { } key && countries.TryGetValue(key, out var country)
            ? Description(country.Cultures, "fr") : null;

        string? BorderPointName(short? id) => id is { } key
            ? Description(borderPoints.FirstOrDefault(b => b.Id == key)?.Cultures, "fr") ?? key.ToString()
            : null;

        return raw.Select(m => new MovRow(
            m.Id, m.TravelDate, m.RegistrationTime,
            LookupCode(lookups, m.DirectionLookupId), LookupFr(m.DirectionLookupId),
            countries.TryGetValue(m.CitizenshipId ?? 0, out var nat) ? nat.Alpha2 : null,
            countries.TryGetValue(m.CitizenshipId ?? 0, out var nat3) ? nat3.Alpha3 : null,
            CountryName(m.CitizenshipId),
            LookupFr(m.DocumentTypeId), LookupFr(m.TravelReasonId), CountryName(m.ResidentCountryId),
            BorderPointName(m.BorderPointId),
            m.Flight, m.Granted, m.IsTransit, m.IsDriver, m.ChildrenCount, m.Inspector,
            m.Surname, m.GivenName, m.DocumentNumber, m.BirthDate,
            LookupFr(m.TransportTypeId), LookupFr(m.RefusalJustificationId), m.HasVisa)).ToList();
    }

    private static IQueryable<Movement> ApplyFilters(IQueryable<Movement> query, ReportParameters p)
    {
        if (p.From is { } from) query = query.Where(m => m.TravelDate >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(m => m.TravelDate < end);
        }
        if (p.BorderPointId is { } borderPoint) query = query.Where(m => m.BorderPointId == borderPoint);
        if (!string.IsNullOrWhiteSpace(p.DirectionCode)) query = query.Where(m => m.Direction!.Code == p.DirectionCode);
        if (p.CountryId is { } country) query = query.Where(m => m.Document!.CitizenshipId == country);
        if (p.DocumentTypeId is { } documentType) query = query.Where(m => m.Document!.DocumentTypeId == documentType);
        if (!string.IsNullOrWhiteSpace(p.FlightNumber))
        {
            var flight = p.FlightNumber.Trim().ToUpper();
            query = query.Where(m => m.TripCrossing != null && m.TripCrossing.Trip != null
                && m.TripCrossing.Trip.ExternalNumber != null
                && m.TripCrossing.Trip.ExternalNumber.ToUpper().Contains(flight));
        }
        if (!string.IsNullOrWhiteSpace(p.Inspector))
        {
            var inspector = p.Inspector.Trim().ToUpper();
            query = query.Where(m => m.Inspector != null && m.Inspector.ToUpper().Contains(inspector));
        }
        if (!string.IsNullOrWhiteSpace(p.DocumentNumber))
        {
            var number = p.DocumentNumber.Trim().ToUpper();
            query = query.Where(m => m.Document!.DocumentNumber != null && m.Document.DocumentNumber.ToUpper() == number);
        }
        if (!string.IsNullOrWhiteSpace(p.Surname))
        {
            var surname = p.Surname.Trim().ToUpper();
            query = query.Where(m => m.Document!.Surname != null && m.Document.Surname.ToUpper().Contains(surname));
        }
        if (!string.IsNullOrWhiteSpace(p.GivenName))
        {
            var given = p.GivenName.Trim().ToUpper();
            query = query.Where(m => m.Document!.GivenName != null && m.Document.GivenName.ToUpper().Contains(given));
        }
        if (p.IncludeChildren) query = query.Where(m => m.ChildrenCount > 0);

        return query;
    }

    private static string? LookupCode(Dictionary<int, Lookup> lookups, int? id)
        => id is { } key && lookups.TryGetValue(key, out var lookup) ? lookup.Code : null;

    private static string? Description(IEnumerable<LookupCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Description
           ?? cultures?.FirstOrDefault()?.Description;

    private static string? Description(IEnumerable<CountryCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Name
           ?? cultures?.FirstOrDefault()?.Name;

    private static string? Description(IEnumerable<BorderPointCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Name
           ?? cultures?.FirstOrDefault()?.Name;

    private static string? Description(IEnumerable<ProvinceCulture>? cultures, string culture)
        => cultures?.FirstOrDefault(c => c.Culture == culture)?.Name
           ?? cultures?.FirstOrDefault()?.Name;

    // -----------------------------------------------------------------------
    // Mise en forme et agrégation
    // -----------------------------------------------------------------------

    private static string N(long value) => value.ToString("N0", Format);

    private static string Pct(long part, long total)
        => total == 0 ? "—" : ((decimal)part * 100 / total).ToString("N1", Format) + " %";

    private static string D(DateTime? date) => date?.ToString("dd/MM/yyyy", Format) ?? "—";

    private static string T(DateTime date) => date.ToString("dd/MM/yyyy HH:mm", Format);

    private static string MonthKey(DateTime date) => date.ToString("MM/yyyy", Format);

    private static bool IsEntry(MovRow row) => row.DirectionCode == MovementService.EntryCode;

    private static bool IsExit(MovRow row) => row.DirectionCode == MovementService.ExitCode;

    /// <summary>Décennies d'âge : tranches du poste (mineur, 18-30, 31-45, 46-60, 60+).</summary>
    private static string AgeBand(DateTime? birth, DateTime reference)
    {
        if (birth is not { } date) return "n.d.";
        var age = reference.Year - date.Year - (reference.Date < date.Date.AddYears(reference.Year - date.Year) ? 1 : 0);
        return age switch
        {
            < 18 => "< 18",
            <= 30 => "18-30",
            <= 45 => "31-45",
            <= 60 => "46-60",
            _ => "60+",
        };
    }

    private static readonly string[] AgeBands = { "< 18", "18-30", "31-45", "46-60", "60+", "n.d." };

    /// <summary>Nom d'affichage d'une personne (nom prénom).</summary>
    private static string PersonName(string? surname, string? givenName)
        => string.Join(' ', new[] { surname, givenName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    // -----------------------------------------------------------------------
    // En-têtes communs
    // -----------------------------------------------------------------------

    private ReportTemplate Template(ReportDefinition definition, ReportParameters p,
        IReadOnlyList<ReportFilterLine>? extra = null)
    {
        var filters = Filters(p).ToList();
        if (extra is not null) filters.AddRange(extra);

        return new ReportTemplate(definition.Id, definition.NameKey, null, filters,
            new List<ReportTableTemplate>(), new List<ReportSummaryLine>(), _clock.Now, _currentUser.UserName);
    }

    private static IEnumerable<ReportFilterLine> Filters(ReportParameters p)
    {
        if (p.From is { } from || p.To is { } to)
            yield return new ReportFilterLine("RP_Filter_Period", $"{D(p.From)} → {D(p.To)}");
        if (p.BorderPointId is { } borderPoint)
            yield return new ReportFilterLine("RP_Filter_BorderPoint",
                string.IsNullOrWhiteSpace(p.BorderPointName) ? $"#{borderPoint}" : p.BorderPointName!);
        if (!string.IsNullOrWhiteSpace(p.DirectionCode))
            yield return new ReportFilterLine("PAL_Col_Direction", p.DirectionCode!);
        if (!string.IsNullOrWhiteSpace(p.DocumentNumber))
            yield return new ReportFilterLine("WL_Col_Document", p.DocumentNumber!);
        if (!string.IsNullOrWhiteSpace(p.Surname) || !string.IsNullOrWhiteSpace(p.GivenName))
            yield return new ReportFilterLine("RP_Filter_Person",
                PersonName(p.Surname, p.GivenName));
        if (!string.IsNullOrWhiteSpace(p.FlightNumber))
            yield return new ReportFilterLine("BO_Col_Trip", p.FlightNumber!);
        if (!string.IsNullOrWhiteSpace(p.Inspector))
            yield return new ReportFilterLine("RP_Filter_Inspector", p.Inspector!);
    }

    private static ReportTableTemplate Table(string? titleKey, ReportColumnTemplate[] columns,
        IReadOnlyList<object?[]> rows, object?[]? totals = null)
        => new(titleKey, columns, rows, totals);
}
