using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Refus d'entrée (vue des mouvements refusés, héritage : RefusalOfEntryPrintForm).</summary>
public sealed record RefusalListItem(int MovementId, DateTime RegistrationTime, DateTime TravelDate,
    string DocumentNumber, string? HolderName, string DirectionFr, string? DeportationReasonFr,
    string? RefusalJustificationFr, string? Inspector, bool HasRemovalOrder, bool HasPenalty);

/// <summary>Ligne de liste des ordres de reconduite.</summary>
public sealed record RemovalOrderListItem(int Id, DateTime CreateDate, string TypeFr, string? DocNumber,
    string? HolderName, string? Info, string? OfficerName, bool Revoke);

/// <summary>Demande d'ordre de reconduite (héritage : RemovalOrders).</summary>
public sealed record RemovalOrderRequest(int PersonId, string TypeCode, string? Info, int? MovementId);

/// <summary>Ligne de liste des amendes.</summary>
public sealed record PenaltyTicketListItem(int Id, string? Number, DateTime CreateDate, string ReasonFr,
    decimal Amount, string CurrencyCode, string? DocumentNumber, string? HolderName, int? Duration,
    bool Paid, bool Revoke);

/// <summary>Demande d'amende depuis un enregistrement de passage (héritage : PenaltyTicket).</summary>
public sealed record PenaltyTicketRequest(int MovementId, int PenaltyReasonId, decimal Amount,
    int? Duration, string? Number, string? CurrencyCode);

/// <summary>
/// Enforcement (héritage : RefusalOfEntryPrintForm, RemovalOrderPrintForm,
/// PenaltyTicket) : refus d'entrée avec base légale (lRefusalJustifications),
/// ordres de reconduite / avis d'inadmissibilité (lRemovalOrderTypes),
/// amendes avec copie du document et du visa au moment des faits.
/// </summary>
public class EnforcementService
{
    /// <summary>Devise par défaut si le réglage hérité Currency est absent.</summary>
    public const string DefaultCurrency = "USD";

    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public EnforcementService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    // ---- Refus d'entrée ------------------------------------------------------

    public async Task<IReadOnlyList<RefusalListItem>> GetRefusalsAsync(DateTime? from = null, DateTime? to = null,
        int take = 200, CancellationToken ct = default)
    {
        var query = _db.Set<Movement>()
            .Include(m => m.Document)
            .Include(m => m.Direction)
            .Include(m => m.DeportationReason!).ThenInclude(r => r.Cultures)
            .Include(m => m.RefusalJustification!).ThenInclude(r => r.Cultures)
            .Where(m => !m.Granted);
        if (from is not null) query = query.Where(m => m.RegistrationTime >= from);
        if (to is not null) query = query.Where(m => m.RegistrationTime <= to);

        var movements = await query.OrderByDescending(m => m.RegistrationTime).Take(take).ToListAsync(ct);
        var ids = movements.Select(m => m.Id).ToList();
        var withOrders = await _db.Set<RemovalOrder>().Where(o => o.MovementId != null && ids.Contains(o.MovementId.Value) && !o.Revoke)
            .Select(o => o.MovementId!.Value).Distinct().ToListAsync(ct);
        var withPenalties = await _db.Set<PenaltyTicket>().Where(p => p.MovementId != null && ids.Contains(p.MovementId.Value) && !p.Revoke)
            .Select(p => p.MovementId!.Value).Distinct().ToListAsync(ct);

        return movements.Select(m => new RefusalListItem(m.Id, m.RegistrationTime, m.TravelDate,
            m.Document?.DocumentNumber ?? string.Empty,
            m.Document is null ? null : $"{m.Document.Surname} {m.Document.GivenName}".Trim(),
            MovementService.DescriptionOf(m.Direction, "fr"),
            m.DeportationReason is null ? null : MovementService.DescriptionOf(m.DeportationReason, "fr"),
            m.RefusalJustification is null ? null : MovementService.DescriptionOf(m.RefusalJustification, "fr"),
            m.Inspector, withOrders.Contains(m.Id), withPenalties.Contains(m.Id))).ToList();
    }

    /// <summary>Complète la base légale du refus (nomenclature REFUSAL_JUSTIFICATION).</summary>
    public async Task SetRefusalJustificationAsync(int movementId, int? justificationId, CancellationToken ct = default)
    {
        var movement = await _db.Set<Movement>().SingleOrDefaultAsync(m => m.Id == movementId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", movementId.ToString());
        if (movement.Granted)
            throw new DomainValidationException("ENF_Error_MovementNotRefused", movementId.ToString());

        if (justificationId is { } id
            && !await _db.Set<Lookup>().AnyAsync(l => l.Id == id && l.Type!.Code == "REFUSAL_JUSTIFICATION" && !l.Revoke, ct))
            throw new DomainValidationException("ENF_Error_JustificationUnknown", id.ToString());

        movement.RefusalJustificationId = justificationId;
        movement.UpdateDate = _clock.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(Movement), movementId.ToString(),
            "Base légale du refus mise à jour", ct);
    }

    // ---- Ordres de reconduite ------------------------------------------------

    /// <summary>
    /// Crée un ordre de reconduite ou un avis d'inadmissibilité
    /// (nomenclature REMOVAL_ORDER_TYPE). Le rattachement à la liste d'alerte
    /// (PersonStopListID) sera posé au Lot 5.
    /// </summary>
    public async Task<int> CreateRemovalOrderAsync(RemovalOrderRequest request, CancellationToken ct = default)
    {
        if (!await _db.Set<Person>().AnyAsync(p => p.Id == request.PersonId, ct))
            throw new DomainValidationException("Admin_Error_ItemNotFound", request.PersonId.ToString());

        var type = await _db.Set<Lookup>().SingleOrDefaultAsync(l =>
            l.Type!.Code == "REMOVAL_ORDER_TYPE" && l.Code == request.TypeCode && !l.Revoke, ct)
            ?? throw new DomainValidationException("ENF_Error_RemovalTypeRequired", request.TypeCode);

        string? docNumber = null;
        if (request.MovementId is { } movementId)
        {
            var movement = await _db.Set<Movement>().Include(m => m.Document)
                .SingleOrDefaultAsync(m => m.Id == movementId, ct)
                ?? throw new DomainValidationException("Admin_Error_ItemNotFound", movementId.ToString());
            docNumber = movement.Document?.DocumentNumber;
        }

        var order = new RemovalOrder
        {
            PersonId = request.PersonId,
            MovementId = request.MovementId,
            DocNumber = docNumber,
            TypeId = type.Id,
            Info = Clean(request.Info),
            OfficerName = _currentUser.UserName ?? _currentUser.UserId,
            CreateDate = _clock.Now,
            BorderPointId = _currentUser.BorderPointId
        };
        _db.Set<RemovalOrder>().Add(order);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(RemovalOrder), order.Id.ToString(),
            $"{type.Code} — personne {request.PersonId}", ct);
        return order.Id;
    }

    /// <summary>
    /// Création d'un ordre de reconduite depuis un refus d'entrée : la personne
    /// est résolue depuis le mouvement, lui-même obligatoirement refusé.
    /// </summary>
    public async Task<int> CreateRemovalOrderFromRefusalAsync(int movementId, string typeCode, string? info,
        CancellationToken ct = default)
    {
        var movement = await _db.Set<Movement>().SingleOrDefaultAsync(m => m.Id == movementId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", movementId.ToString());
        if (movement.Granted)
            throw new DomainValidationException("ENF_Error_MovementNotRefused", movementId.ToString());

        return await CreateRemovalOrderAsync(new RemovalOrderRequest(movement.PersonId, typeCode, info, movementId), ct);
    }

    public async Task<IReadOnlyList<RemovalOrderListItem>> GetRemovalOrdersAsync(bool includeRevoked = false,
        int take = 200, CancellationToken ct = default)
    {
        var orders = await _db.Set<RemovalOrder>()
            .Include(o => o.Type!).ThenInclude(t => t.Cultures)
            .Include(o => o.Person!).ThenInclude(p => p.Documents)
            .Where(o => includeRevoked || !o.Revoke)
            .OrderByDescending(o => o.CreateDate)
            .Take(take)
            .ToListAsync(ct);

        return orders.Select(o =>
        {
            var document = o.Person?.Documents.OrderByDescending(d => d.Id).FirstOrDefault();
            return new RemovalOrderListItem(o.Id, o.CreateDate, MovementService.DescriptionOf(o.Type, "fr"),
                o.DocNumber, document is null ? null : $"{document.Surname} {document.GivenName}".Trim(),
                o.Info, o.OfficerName, o.Revoke);
        }).ToList();
    }

    public async Task SetRemovalOrderRevokedAsync(int orderId, bool revoke, CancellationToken ct = default)
    {
        var order = await _db.Set<RemovalOrder>().SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", orderId.ToString());

        order.Revoke = revoke;
        order.RevokeDate = revoke ? _clock.Now : null;
        order.UpdateDate = _clock.Now;
        order.UpdateUser = _currentUser.UserId;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(RemovalOrder), orderId.ToString(),
            revoke ? "Ordre révoqué" : "Ordre réactivé", ct);
    }

    // ---- Amendes -------------------------------------------------------------

    /// <summary>
    /// Crée une amende depuis un enregistrement de passage : le document, le
    /// titulaire et le visa sont copiés au moment des faits (héritage). Refus
    /// si une amende active du même motif existe déjà pour ce passage.
    /// </summary>
    public async Task<int> CreatePenaltyTicketAsync(PenaltyTicketRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
            throw new DomainValidationException("ENF_Error_AmountInvalid");
        if (request.Duration is < 0)
            throw new DomainValidationException("ENF_Error_DurationInvalid");

        var reason = await _db.Set<Lookup>().SingleOrDefaultAsync(l =>
            l.Id == request.PenaltyReasonId && l.Type!.Code == "PENALTY_TICKET_REASON" && !l.Revoke, ct)
            ?? throw new DomainValidationException("ENF_Error_ReasonRequired", request.PenaltyReasonId.ToString());

        var movement = await _db.Set<Movement>()
            .Include(m => m.Document)
            .Include(m => m.Visa)
            .SingleOrDefaultAsync(m => m.Id == request.MovementId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", request.MovementId.ToString());

        var duplicate = await _db.Set<PenaltyTicket>().AnyAsync(t =>
            t.MovementId == request.MovementId && t.PenaltyReasonId == request.PenaltyReasonId && !t.Revoke, ct);
        if (duplicate)
            throw new DomainValidationException("ENF_Error_DuplicatePenalty", request.MovementId.ToString());

        var document = movement.Document;
        var ticket = new PenaltyTicket
        {
            MovementId = movement.Id,
            PenaltyReasonId = reason.Id,
            Amount = request.Amount,
            Duration = request.Duration,
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? await GetDefaultCurrencyAsync(ct)
                : request.CurrencyCode!.Trim().ToUpperInvariant(),
            DocumentTypeId = document?.DocumentTypeId,
            DocumentNumber = document?.DocumentNumber,
            DocumentExpireDate = document?.ExpireDate,
            DocumentIssueDate = document?.IssueDate,
            Surname = document?.Surname,
            GivenName = document?.GivenName,
            SexId = document?.SexId,
            DocumentCountryOfIssueId = document?.CountryOfIssueId,
            VisaNumber = movement.Visa?.Number,
            VisaIssueDate = movement.Visa?.IssueDate,
            VisaExpireDate = movement.Visa?.ExpireDate,
            CreateDate = _clock.Now,
            UserId = _currentUser.UserId,
            BorderPointId = _currentUser.BorderPointId
        };
        ticket.Number = string.IsNullOrWhiteSpace(request.Number)
            ? await GenerateNumberAsync(ct)
            : request.Number!.Trim();

        _db.Set<PenaltyTicket>().Add(ticket);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(PenaltyTicket), ticket.Id.ToString(),
            $"Amende {ticket.Number} — {ticket.Amount:0.00} {ticket.CurrencyCode}", ct);
        return ticket.Id;
    }

    public async Task<IReadOnlyList<PenaltyTicketListItem>> GetPenaltyTicketsAsync(bool includeRevoked = false,
        string? search = null, int take = 200, CancellationToken ct = default)
    {
        var tickets = await _db.Set<PenaltyTicket>()
            .Include(t => t.PenaltyReason!).ThenInclude(r => r.Cultures)
            .Where(t => includeRevoked || !t.Revoke)
            .ToListAsync(ct);

        return tickets
            .Where(t => string.IsNullOrWhiteSpace(search)
                        || (t.Number?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (t.DocumentNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (t.Surname?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(t => t.CreateDate)
            .Take(take)
            .Select(t => new PenaltyTicketListItem(t.Id, t.Number, t.CreateDate,
                MovementService.DescriptionOf(t.PenaltyReason, "fr"), t.Amount, t.CurrencyCode ?? DefaultCurrency,
                t.DocumentNumber, $"{t.Surname} {t.GivenName}".Trim(), t.Duration, t.Paid, t.Revoke))
            .ToList();
    }

    /// <summary>Encaissement : marque l'amende payée avec le numéro de reçu (traçable).</summary>
    public async Task SetPaidAsync(int ticketId, bool paid, string? receiptNumber = null, CancellationToken ct = default)
    {
        var ticket = await _db.Set<PenaltyTicket>().SingleOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", ticketId.ToString());

        if (paid && string.IsNullOrWhiteSpace(receiptNumber) && string.IsNullOrWhiteSpace(ticket.ReceiptNumber))
            throw new DomainValidationException("ENF_Error_ReceiptRequired");

        ticket.Paid = paid;
        ticket.PaidDate = paid ? _clock.Now : null;
        if (!string.IsNullOrWhiteSpace(receiptNumber)) ticket.ReceiptNumber = receiptNumber!.Trim();
        ticket.UpdateDate = _clock.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(PenaltyTicket), ticketId.ToString(),
            paid ? $"Amende encaissée (reçu {ticket.ReceiptNumber})" : "Amende marquée impayée", ct);
    }

    public async Task SetPenaltyRevokedAsync(int ticketId, bool revoke, CancellationToken ct = default)
    {
        var ticket = await _db.Set<PenaltyTicket>().SingleOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new DomainValidationException("Admin_Error_ItemNotFound", ticketId.ToString());

        ticket.Revoke = revoke;
        ticket.RevokeDate = revoke ? _clock.Now : null;
        ticket.UpdateDate = _clock.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowRevoked, nameof(PenaltyTicket), ticketId.ToString(),
            revoke ? $"Amende {ticket.Number} annulée" : $"Amende {ticket.Number} réactivée", ct);
    }

    /// <summary>Numérotation : TICKET-AAAA-NNNNNN (héritage : aucun format imposé).</summary>
    private async Task<string> GenerateNumberAsync(CancellationToken ct)
    {
        var prefix = $"TICKET-{_clock.Now.Year}-";
        var count = await _db.Set<PenaltyTicket>().CountAsync(t => t.Number != null && t.Number.StartsWith(prefix), ct);
        for (var i = 1; i <= 1000; i++)
        {
            var candidate = $"{prefix}{count + i:D6}";
            if (!await _db.Set<PenaltyTicket>().AnyAsync(t => t.Number == candidate, ct)) return candidate;
        }
        throw new DomainValidationException("ENF_Error_NumberGeneration");
    }

    /// <summary>Devise par défaut : réglage hérité Currency (description courte, ex. « USD »).</summary>
    private async Task<string> GetDefaultCurrencyAsync(CancellationToken ct)
    {
        var setting = await _db.Set<Setting>()
            .Where(s => s.Type == "Currency")
            .OrderByDescending(s => s.ShortDescription != null)
            .FirstOrDefaultAsync(ct);
        var currency = setting?.ShortDescription ?? setting?.Description;
        return string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency.Trim().ToUpperInvariant();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
