using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Signalement d'une personne douteuse (héritage : insertion dans
/// DoubtablePersons au moment du contrôle).</summary>
public sealed record DoubtablePersonRequest(
    int MovementId,
    int? CheckingReasonId = null,
    string? InspectorNote = null,
    int? PersonStopListId = null,
    int? DocumentStopListId = null,
    byte[]? PhotoJustScanned = null,
    short? BorderPointId = null);

/// <summary>Décision du superviseur (seconde inspection) sur un doute.</summary>
public sealed record DoubtableProcessRequest(
    string? SupervisorNote,
    int? PerformedActionId,
    int? CheckingReasonId = null,
    string? RefusalOfficerName = null,
    string? RefusalOfficerRank = null,
    string? RefusalDocNumber = null,
    int? RefusalCountryId = null,
    int? RefusalJustificationId = null,
    string? RefusalJustificationOther = null);

/// <summary>Ligne de la file des personnes douteuses.</summary>
public sealed record DoubtablePersonListItem(
    int Id,
    DateTime CreateDate,
    int MovementId,
    DateTime? TravelDate,
    string? DocumentNumber,
    string? HolderName,
    string? DirectionFr,
    short WorkflowStatus,
    string? CheckingReasonFr,
    int? PersonStopListId,
    int? DocumentStopListId,
    string? InspectorNote,
    string? Supervisor,
    DateTime? SupervisorRegistrationTime,
    string? SupervisorPcNumber,
    string? SupervisorNote,
    int? PerformedActionId,
    string? PerformedActionFr,
    string? StatusActionFr,
    bool HasRefusal);

/// <summary>
/// Personnes douteuses (héritage : DoubtablePersons, DoubtablePersonManagement) :
/// file d'attente de la seconde inspection, décision du superviseur (action menée,
/// note, refus éventuel), compteur d'alerte et contrôle « personne déjà signalée ».
///
/// Workflow conservé (statuts 1/2/3 du client précédent ; les dossiers repris
/// arrivent déjà traités) :
///   1 = en attente du superviseur, 2 = traité (action consignée), 3 = annulé.
/// La colonne héritée <c>Status</c> référence l'action (lActions : 1 = Permit,
/// 2 = Refuse, 3 = Detain, 4 = SendToMe, 11 = RAPATRIER) ; elle est tenue
/// synchronisée avec <c>PerformedActionId</c> comme dans les données observées.
/// </summary>
public class DoubtablePersonService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public DoubtablePersonService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>Identifiant d'un motif de contrôle par code (héritage : rpt.lCheckingReason.ExternalID :
    /// A_FP, D_NOT, EP, FD, FDP, FIND, MIND, FPR, S_VIS, SSC).</summary>
    public async Task<int?> GetCheckingReasonIdAsync(string code, CancellationToken ct = default)
        => await _db.Set<Lookup>()
            .Where(l => l.Type!.Code == "CHECKING_REASON" && l.Code == code && !l.Revoke)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>Réglage hérité « CheckInDoubtablePersons » : contrôle automatique des
    /// personnes douteuses à l'enregistrement (0 = désactivé, valeur du déploiement DGM).</summary>
    public async Task<bool> IsAutoCheckEnabledAsync(CancellationToken ct = default)
        => await _db.Set<Setting>().Where(s => s.Type == "CheckInDoubtablePersons")
               .Select(s => s.Value)
               .FirstOrDefaultAsync(ct) == 1;

    public async Task SetAutoCheckAsync(bool enabled, CancellationToken ct = default)
    {
        var setting = await _db.Set<Setting>().FirstOrDefaultAsync(s => s.Type == "CheckInDoubtablePersons", ct);
        if (setting == null)
        {
            _db.Set<Setting>().Add(new Setting { Type = "CheckInDoubtablePersons", Value = enabled ? 1 : 0 });
        }
        else
        {
            setting.Value = enabled ? 1 : 0;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, "Setting", "CheckInDoubtablePersons",
            enabled ? "contrôle des douteux activé" : "contrôle des douteux désactivé", ct);
    }

    /// <summary>Enregistre un doute pour un mouvement (une ligne par mouvement,
    /// comme la clé héritée PassportRegistrationID).</summary>
    public async Task<int> RegisterAsync(DoubtablePersonRequest request, CancellationToken ct = default)
    {
        var movement = await _db.Set<Movement>().FirstOrDefaultAsync(m => m.Id == request.MovementId, ct)
            ?? throw new DomainValidationException("PAL_Error_MovementNotFound", request.MovementId.ToString());

        if (await _db.Set<DoubtablePerson>().AnyAsync(d => d.MovementId == request.MovementId, ct))
            throw new DomainValidationException("WL_Error_DoubtAlreadyExists", request.MovementId.ToString());

        var entry = new DoubtablePerson
        {
            MovementId = request.MovementId,
            CheckingReasonId = request.CheckingReasonId,
            InspectorNote = request.InspectorNote,
            PersonStopListId = request.PersonStopListId,
            DocumentStopListId = request.DocumentStopListId,
            PhotoJustScanned = request.PhotoJustScanned,
            BorderPointId = request.BorderPointId ?? _currentUser.BorderPointId,
            WorkflowStatus = DoubtfulWorkflowStatus.Pending,
            CreateDate = _clock.Now,
            CreatedByUserId = _currentUser.UserId
        };

        _db.Set<DoubtablePerson>().Add(entry);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(DoubtablePerson), entry.Id.ToString(),
            $"mouvement {request.MovementId}", ct);
        return entry.Id;
    }

    /// <summary>File « en attente du superviseur » (statut 1), la plus ancienne d'abord
    /// (héritage : note de superviseur sur toutes les lignes traitées du déploiement).</summary>
    public Task<IReadOnlyList<DoubtablePersonListItem>> GetPendingAsync(CancellationToken ct = default)
        => LoadAsync(d => d.WorkflowStatus == DoubtfulWorkflowStatus.Pending, ct);

    /// <summary>Historique / recherche (héritage : GetDoubtablePersons et variantes).</summary>
    public Task<IReadOnlyList<DoubtablePersonListItem>> SearchAsync(short? workflowStatus = null,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
        => LoadAsync(d =>
            (workflowStatus == null || d.WorkflowStatus == workflowStatus)
            && (from == null || d.CreateDate >= from)
            && (to == null || d.CreateDate <= to), ct,
            pendingFirst: false);

    public async Task<DoubtablePersonListItem?> GetForMovementAsync(int movementId, CancellationToken ct = default)
    {
        var items = await LoadAsync(d => d.MovementId == movementId, ct, pendingFirst: false);
        return items.FirstOrDefault();
    }

    /// <summary>Compteur d'alerte (héritage : AlarmIndicator / dashboard
    /// NumberOfDoubtablePersons).</summary>
    public Task<int> GetOpenCountAsync(CancellationToken ct = default)
        => _db.Set<DoubtablePerson>().CountAsync(d => d.WorkflowStatus == DoubtfulWorkflowStatus.Pending, ct);

    /// <summary>Décision du superviseur : note, action menée, refus éventuel
    /// (héritage : colonnes Supervisor*, PerformedActionID/Status, Refusal*).</summary>
    public async Task ProcessAsync(int id, DoubtableProcessRequest request, CancellationToken ct = default)
    {
        var entry = await _db.Set<DoubtablePerson>().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DomainValidationException("WL_Error_EntryNotFound");
        if (entry.WorkflowStatus != DoubtfulWorkflowStatus.Pending)
            throw new DomainValidationException("WL_Error_NotPending");

        if (request.PerformedActionId is { } actionId
            && !await _db.Set<Lookup>().AnyAsync(l => l.Id == actionId && l.Type!.Code == "ACTION" && !l.Revoke, ct))
            throw new DomainValidationException("WL_Error_ActionUnknown", actionId.ToString());

        var now = _clock.Now;
        entry.Supervisor = _currentUser.UserName ?? _currentUser.UserId;
        entry.SupervisorUserId = _currentUser.UserId;
        entry.SupervisorRegistrationTime = now;
        entry.SupervisorPcNumber = _currentUser.PcNumber;
        entry.SupervisorNote = request.SupervisorNote?.Trim();
        entry.PerformedActionId = request.PerformedActionId;
        entry.Status = request.PerformedActionId; // colonne héritée, jointe à lActions dans les rapports
        entry.CheckingReasonId = request.CheckingReasonId ?? entry.CheckingReasonId;
        entry.WorkflowStatus = DoubtfulWorkflowStatus.Processed;

        if (request.RefusalJustificationId != null || request.RefusalOfficerName != null)
        {
            entry.RefusalOfficerName = request.RefusalOfficerName?.Trim();
            entry.RefusalOfficerRank = request.RefusalOfficerRank?.Trim();
            entry.RefusalDocNumber = request.RefusalDocNumber?.Trim();
            entry.RefusalCountryId = request.RefusalCountryId;
            entry.RefusalJustificationId = request.RefusalJustificationId;
            entry.RefusalJustificationOther = request.RefusalJustificationOther?.Trim();
            entry.RefusalCreateDate = now;
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(DoubtablePerson), id.ToString(),
            $"traitée (action {request.PerformedActionId?.ToString() ?? "—"})", ct);
    }

    /// <summary>Annule le doute sans action (statut 3 du système précédent : annulation
    /// d'alarme par le superviseur).</summary>
    public async Task CancelAsync(int id, string? supervisorNote, CancellationToken ct = default)
    {
        var entry = await _db.Set<DoubtablePerson>().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DomainValidationException("WL_Error_EntryNotFound");
        if (entry.WorkflowStatus != DoubtfulWorkflowStatus.Pending)
            throw new DomainValidationException("WL_Error_NotPending");

        entry.Supervisor = _currentUser.UserName ?? _currentUser.UserId;
        entry.SupervisorUserId = _currentUser.UserId;
        entry.SupervisorRegistrationTime = _clock.Now;
        entry.SupervisorPcNumber = _currentUser.PcNumber;
        entry.SupervisorNote = supervisorNote?.Trim();
        entry.WorkflowStatus = DoubtfulWorkflowStatus.Cancelled;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(DoubtablePerson), id.ToString(), "doute annulé", ct);
    }

    /// <summary>Contrôle hérité <c>fn_CheckPersonInDoubtablePersons</c> : la dernière
    /// inscription de cette personne (nom + prénom + date + document exacts) a-t-elle
    /// reçu une action au-delà de « Permit » (Status &gt; 1) ?</summary>
    public async Task<bool> HasPriorActionAsync(string? surname, string? givenName, DateTime? dateOfBirth,
        string? documentNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(surname) || string.IsNullOrWhiteSpace(givenName) || dateOfBirth == null)
            return false;

        // Héritage : fn_CheckPersonInDoubtablePersons filtre « D.Status > 1 », les
        // identifiants 1/2/3/4/11 de lActions codant Permit/Refuse/Detain/SendToMe/
        // RAPATRIER. Reconstruit par code pour rester indépendant des identifiants.
        var adverseActionIds = await _db.Set<Lookup>()
            .Where(l => l.Type!.Code == "ACTION" && l.Code != "Permit")
            .Select(l => l.Id)
            .ToListAsync(ct);

        var doubts = await _db.Set<DoubtablePerson>()
            .AsNoTracking()
            .Include(d => d.Movement!).ThenInclude(m => m.Document)
            .Where(d => d.Status != null && adverseActionIds.Contains(d.Status.Value))
            .ToListAsync(ct);

        return doubts.Any(d =>
            NameMatching.NamesEqual(d.Movement?.Document?.Surname, surname)
            && NameMatching.NamesEqual(d.Movement?.Document?.GivenName, givenName)
            && d.Movement?.Document?.DateOfBirth == dateOfBirth
            && NameMatching.NamesEqual(d.Movement?.Document?.DocumentNumber, documentNumber));
    }

    // ---- Interne -------------------------------------------------------------

    private async Task<IReadOnlyList<DoubtablePersonListItem>> LoadAsync(
        System.Linq.Expressions.Expression<Func<DoubtablePerson, bool>> predicate, CancellationToken ct,
        bool pendingFirst = true)
    {
        var query = _db.Set<DoubtablePerson>()
            .Include(d => d.Movement!).ThenInclude(m => m.Document)
            .Include(d => d.Movement!).ThenInclude(m => m.Direction)
            .Include(d => d.CheckingReason!).ThenInclude(r => r.Cultures)
            .AsNoTracking()
            .Where(predicate);

        var doubts = await query.ToListAsync(ct);

        var actionIds = doubts.Where(d => d.PerformedActionId != null).Select(d => (int)d.PerformedActionId!.Value)
            .Concat(doubts.Where(d => d.Status != null).Select(d => (int)d.Status!.Value))
            .Distinct().ToList();
        var actions = await _db.Set<Lookup>().Include(l => l.Cultures)
            .Where(l => actionIds.Contains(l.Id))
            .ToListAsync(ct);
        string? ActionName(int? id) => id == null ? null : MovementService.DescriptionOf(actions.FirstOrDefault(a => a.Id == id), "fr");

        IEnumerable<DoubtablePerson> ordered = pendingFirst
            ? doubts.OrderBy(d => d.WorkflowStatus == DoubtfulWorkflowStatus.Pending ? 0 : 1).ThenBy(d => d.CreateDate)
            : doubts.OrderByDescending(d => d.CreateDate);

        return ordered.Select(d => new DoubtablePersonListItem(
            d.Id,
            d.CreateDate,
            d.MovementId,
            d.Movement?.TravelDate,
            d.Movement?.Document?.DocumentNumber,
            d.Movement?.Document is null ? null : $"{d.Movement.Document.Surname} {d.Movement.Document.GivenName}".Trim(),
            d.Movement?.Direction is null ? null : MovementService.DescriptionOf(d.Movement.Direction, "fr"),
            d.WorkflowStatus,
            d.CheckingReason is null ? null : MovementService.DescriptionOf(d.CheckingReason, "fr"),
            d.PersonStopListId,
            d.DocumentStopListId,
            d.InspectorNote,
            d.Supervisor,
            d.SupervisorRegistrationTime,
            d.SupervisorPcNumber,
            d.SupervisorNote,
            d.PerformedActionId,
            ActionName(d.PerformedActionId),
            ActionName(d.Status),
            d.RefusalJustificationId != null || d.RefusalOfficerName != null)).ToList();
    }
}
