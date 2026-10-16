using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Demande d'enregistrement d'un mouvement (héritage : PassportRegistrationData).</summary>
public sealed record MovementRequest(int DocumentId, string DirectionCode, DateTime TravelDate,
    int? TransportTypeId, bool IsDriver, int? TravelReasonId, bool Granted, int? DeportationReasonId,
    bool IsTransit, bool HasVisa, int? VisaId, int ChildrenCount, string? Note, int? ResidentCountryId,
    int? DestinationCountryId, string? Occupation, string? AccommodationAddress, int? AccommodationTypeId,
    short? DurationOfStay, int? TripCrossingId = null);

/// <summary>Ligne d'historique des mouvements d'un document.</summary>
public sealed record MovementListItem(int Id, DateTime RegistrationTime, DateTime TravelDate,
    string DirectionFr, bool IsEntry, string? TransportFr, string? ReasonFr, bool Granted,
    string? VisaNumber, string? Inspector, string? PcNumber);

/// <summary>
/// Enregistrement des passages à la frontière (héritage : PassportRegistrationData
/// + Children) : validations d'admission, rattachement visa, historique.
/// </summary>
public class MovementService
{
    /// <summary>Code hérité du sens « entrée » (nomenclature MOVEMENT_DIRECTION).</summary>
    public const string EntryCode = "Entry";

    /// <summary>Code hérité du sens « sortie ».</summary>
    public const string ExitCode = "Exit";

    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public MovementService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>
    /// Enregistre un mouvement : valide le document (champs obligatoires hérités
    /// PAL_*), la validité du document à la date de voyage (réglage
    /// MinimumDocumentValidityMonth), le motif en cas de refus, et rattache le
    /// visa valide lorsque la déclaration l'indique.
    /// </summary>
    public async Task<int> RegisterAsync(MovementRequest request, CancellationToken ct = default)
    {
        var document = await _db.Set<TravelDocument>().SingleOrDefaultAsync(d => d.Id == request.DocumentId, ct)
            ?? throw new DomainValidationException("PAL_Error_DocumentNotFound", request.DocumentId.ToString());

        var missing = MissingMandatoryFields(document);
        if (missing.Count > 0)
            throw new DomainValidationException("PAL_Error_DocumentIncomplete", string.Join(", ", missing));

        if (request.TravelDate == default)
            throw new DomainValidationException("PAL_Error_TravelDateRequired");
        if (request.TravelDate.Date > _clock.Now.Date)
            throw new DomainValidationException("PAL_Error_TravelDateFuture");
        if (request.ChildrenCount < 0 || request.DurationOfStay is < 0)
            throw new DomainValidationException("PAL_Error_NegativeValue");

        var direction = await RequireLookupAsync("MOVEMENT_DIRECTION", request.DirectionCode, "PAL_Error_DirectionRequired", ct);

        await EnsureDocumentValidForTravelAsync(document, request.TravelDate, ct);

        var deportationReasonId = request.DeportationReasonId;
        if (!request.Granted)
        {
            if (deportationReasonId is null)
                throw new DomainValidationException("PAL_Error_DeportationReasonRequired");
            await RequireLookupAsync("DEPORTATION_REASON", deportationReasonId.Value, "PAL_Error_DeportationReasonRequired", ct);
        }
        else
        {
            deportationReasonId = null; // un passage accordé ne porte pas de motif de refus
        }

        var visaId = await ResolveVisaAsync(document.Id, request, ct);

        if (request.TransportTypeId is { } transportId)
            await RequireLookupAsync("TRANSPORT_TYPE", transportId, "PAL_Error_TransportTypeUnknown", ct);
        if (request.TravelReasonId is { } reasonId)
            await RequireLookupAsync("TRAVEL_REASON", reasonId, "PAL_Error_TravelReasonUnknown", ct);
        if (request.AccommodationTypeId is { } accommodationId)
            await RequireLookupAsync("ACCOMODATION_TYPE", accommodationId, "PAL_Error_AccommodationUnknown", ct);

        var movement = new Movement
        {
            PersonId = document.PersonId,
            DocumentId = document.Id,
            TravelDate = request.TravelDate.Date,
            DirectionLookupId = direction.Id,
            TransportTypeId = request.TransportTypeId,
            IsDriver = request.IsDriver,
            TravelReasonId = request.TravelReasonId,
            Granted = request.Granted,
            DeportationReasonId = deportationReasonId,
            IsTransit = request.IsTransit,
            HasVisa = request.HasVisa,
            VisaId = visaId,
            ChildrenCount = request.ChildrenCount,
            Note = Clean(request.Note),
            ResidentCountryId = request.ResidentCountryId,
            DestinationCountryId = request.DestinationCountryId,
            Occupation = Clean(request.Occupation),
            AccommodationAddress = Clean(request.AccommodationAddress),
            AccommodationTypeId = request.AccommodationTypeId,
            DurationOfStay = request.DurationOfStay,
            TripCrossingId = request.TripCrossingId,
            BorderPointId = await StationSettings.GetBorderPointIdAsync(_db, _currentUser, ct),
            Inspector = _currentUser.UserName ?? _currentUser.UserId,
            PcNumber = _currentUser.PcNumber,
            RegistrationTime = _clock.Now
        };

        _db.Set<Movement>().Add(movement);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(Movement), movement.Id.ToString(),
            $"{direction.Code} {document.DocumentNumber} — {(request.Granted ? "accordé" : "refusé")}", ct);
        return movement.Id;
    }

    /// <summary>Derniers mouvements d'un document (le plus récent d'abord).</summary>
    public async Task<IReadOnlyList<MovementListItem>> GetHistoryAsync(int documentId, int take = 20, CancellationToken ct = default)
    {
        var movements = await _db.Set<Movement>()
            .Include(m => m.Direction).Include(m => m.TransportType).Include(m => m.TravelReason)
            .Include(m => m.Visa)
            .Where(m => m.DocumentId == documentId)
            .OrderByDescending(m => m.RegistrationTime)
            .Take(take)
            .ToListAsync(ct);

        return movements.Select(m => new MovementListItem(m.Id, m.RegistrationTime, m.TravelDate,
            DescriptionOf(m.Direction, "fr"), DocumentService.IsEntryCode(m.Direction?.Code),
            m.TransportType is null ? null : DescriptionOf(m.TransportType, "fr"),
            m.TravelReason is null ? null : DescriptionOf(m.TravelReason, "fr"),
            m.Granted, m.Visa?.Number, m.Inspector, m.PcNumber)).ToList();
    }

    /// <summary>Détermine le dernier sens de passage connu d'un document (entrée/sortie).</summary>
    public async Task<bool?> GetLastDirectionIsEntryAsync(int documentId, CancellationToken ct = default)
    {
        var last = await _db.Set<Movement>().Include(m => m.Direction)
            .Where(m => m.DocumentId == documentId)
            .OrderByDescending(m => m.RegistrationTime)
            .FirstOrDefaultAsync(ct);
        return last is null ? null : DocumentService.IsEntryCode(last.Direction?.Code);
    }

    // ---- Validations ---------------------------------------------------------

    /// <summary>Champs obligatoires hérités (contrôles PAL_* de l'ancien système).</summary>
    private static List<string> MissingMandatoryFields(TravelDocument d)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(d.DocumentNumber)) missing.Add("DocumentNumber");
        if (string.IsNullOrWhiteSpace(d.Surname)) missing.Add("Surname");
        if (string.IsNullOrWhiteSpace(d.GivenName)) missing.Add("GivenName");
        if (d.DateOfBirth is null) missing.Add("DateOfBirth");
        if (d.SexId is null) missing.Add("SexId");
        if (d.CitizenshipId is null) missing.Add("CitizenshipId");
        return missing;
    }

    /// <summary>
    /// Validité du document à la date de voyage, avec la marge héritée
    /// MinimumDocumentValidityMonth (réglage entier, 0 par défaut).
    /// </summary>
    private async Task EnsureDocumentValidForTravelAsync(TravelDocument document, DateTime travelDate, CancellationToken ct)
    {
        if (document.ExpireDate is not { } expire) return; // document sans date d'expiration : pas de contrôle

        var minimumValidity = await _db.Set<Setting>()
            .Where(s => s.Type == "MinimumDocumentValidityMonth")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct) ?? 0;

        if (expire.Date < travelDate.Date.AddMonths(minimumValidity))
            throw new DomainValidationException("PAL_Error_DocumentExpired",
                $"{document.DocumentNumber} (expire le {expire:dd/MM/yyyy})");
    }

    /// <summary>
    /// Visa du mouvement : si un visa est fourni il doit appartenir au document ;
    /// si « a un visa » est déclaré sans visa fourni, recherche d'un visa valide à
    /// la date de voyage (contrôle hérité « visa de sortie »).
    /// </summary>
    private async Task<int?> ResolveVisaAsync(int documentId, MovementRequest request, CancellationToken ct)
    {
        if (request.VisaId is { } visaId)
        {
            var visa = await _db.Set<Visa>().SingleOrDefaultAsync(v => v.Id == visaId, ct);
            if (visa is null || visa.DocumentId != documentId)
                throw new DomainValidationException("PAL_Error_VisaMismatch", visaId.ToString());
            return visa.Id;
        }

        if (!request.HasVisa) return null;

        var valid = await _db.Set<Visa>()
            .Where(v => v.DocumentId == documentId && !v.Revoke
                        && (v.ExpireDate == null || v.ExpireDate >= request.TravelDate)
                        && (v.IssueDate == null || v.IssueDate <= request.TravelDate))
            .OrderByDescending(v => v.ExpireDate)
            .FirstOrDefaultAsync(ct);
        if (valid is null)
            throw new DomainValidationException("PAL_Error_VisaNotFound");
        return valid.Id;
    }

    private async Task<Lookup> RequireLookupAsync(string typeCode, string code, string errorKey, CancellationToken ct)
    {
        var lookup = await _db.Set<Lookup>().SingleOrDefaultAsync(l =>
            l.Type!.Code == typeCode && l.Code == code && !l.Revoke, ct);
        return lookup ?? throw new DomainValidationException(errorKey, code);
    }

    private async Task<Lookup> RequireLookupAsync(string typeCode, int lookupId, string errorKey, CancellationToken ct)
    {
        var lookup = await _db.Set<Lookup>().SingleOrDefaultAsync(l =>
            l.Id == lookupId && l.Type!.Code == typeCode && !l.Revoke, ct);
        return lookup ?? throw new DomainValidationException(errorKey, lookupId.ToString());
    }

    internal static string DescriptionOf(Lookup? lookup, string culture)
        => lookup?.Cultures.FirstOrDefault(c => c.Culture == culture)?.Description
           ?? lookup?.Cultures.FirstOrDefault()?.Description ?? lookup?.Code ?? string.Empty;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
