using BorderNext.Application.Abstractions;
using BorderNext.Application.Edi;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Xml;

namespace BorderNext.Application.Services;

/// <summary>Options d'import d'un message APIS.</summary>
public sealed record ApisImportOptions(bool StoreFile = true, bool Reconcile = true, bool RegisterDoubtful = true);

/// <summary>Résultat d'un import PAXLST.</summary>
public sealed record ApisImportResult(
    string? ControlReference,
    IReadOnlyList<int> MessageIds,
    int MessageCount,
    int PassengerCount,
    int CrewCount,
    int Matched,
    int Doubtful,
    int Unmatched,
    IReadOnlyList<string> Warnings);

/// <summary>Résultat d'un rapprochement de message.</summary>
public sealed record ApisReconcileResult(int MessageId, int Processed, int Matched, int Doubtful, int Unmatched);

/// <summary>Comptages du module Échanges.</summary>
public sealed record ApisStatistics(
    int MessageCount,
    int PassengerTotal,
    int CrewTotal,
    int ReconciledTotal,
    int DoubtfulTotal,
    int PendingDoubtfulTotal,
    int ExportedTotal);

/// <summary>Résultat d'un export PAXLST (fichier EDIFACT généré).</summary>
public sealed record PaxlstExportResult(string FilePath, int MessageCount, int TravellerCount, string ControlReference);

/// <summary>Résultat d'un export XML des messages APIS.</summary>
public sealed record ApisXmlExportResult(string FilePath, int MessageCount, int PassengerCount);

/// <summary>
/// Messages APIS (héritage : APIMessage / APIPassenger / APIMessageFiles) :
/// import des listes de passagers EDIFACT PAXLST, conservation des fichiers,
/// rapprochement avec les dossiers locaux (documents de voyage), inscription des
/// passagers douteux (héritage : APIDoubtablePersons) et exports vers les autres
/// sites (PAXLST régénéré, XML héritage ExportAPIMessagesXML).
/// </summary>
public class ApisMessageService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ExchangeJournal _journal;
    private readonly ExchangeFileStore _files;
    private readonly StopListMatcher _stopListMatcher;
    private readonly DoubtablePersonService _doubtfulPersons;

    public ApisMessageService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit,
        ExchangeJournal journal, ExchangeFileStore files, StopListMatcher stopListMatcher,
        DoubtablePersonService doubtfulPersons)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _journal = journal;
        _files = files;
        _stopListMatcher = stopListMatcher;
        _doubtfulPersons = doubtfulPersons;
    }

    // -----------------------------------------------------------------------
    // Import
    // -----------------------------------------------------------------------

    /// <summary>
    /// Importe un échange EDIFACT PAXLST : crée un message APIS par message PAXLST,
    /// ses lignes passagers (passagers et équipage) et le fichier d'origine, puis
    /// rapproche les voyageurs des dossiers locaux.
    /// </summary>
    public async Task<ApisImportResult> ImportPaxlstAsync(string content, string? fileName = null,
        ApisImportOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ApisImportOptions();

        // L'exécution est ouverte avant l'analyse : une enveloppe invalide laisse
        // elle aussi une trace en échec dans le journal des échanges.
        var run = await _journal.BeginAsync(ExchangeChannels.Edifact, ExchangeDirections.Import,
            null, fileName, ct: ct);

        try
        {
            var interchange = EdifactParser.Parse(content); // lève une exception i18n si l'enveloppe est invalide
            run.Reference = interchange.InterchangeControlReference;

            var warnings = new List<string>();
            string? storedPath = null;
            string? hash = null;
            long? size = null;

            if (options.StoreFile)
            {
                var saved = await _files.SaveAsync(fileName, content, "Messages", ct);
                storedPath = saved.Path;
                hash = saved.Hash;
                size = saved.Size;

                if (await _files.FindByHashAsync(hash, ct) is { } duplicate)
                    warnings.Add($"Contenu identique au fichier importé n° {duplicate.Id} ({duplicate.FileName}).");
            }

            short? borderPointId = await StationSettings.GetBorderPointIdAsync(_db, _currentUser, ct);

            var messageIds = new List<int>();
            var passengerTotal = 0;
            var crewTotal = 0;

            foreach (var message in interchange.Messages)
            {
                if (!string.Equals(message.Type, PaxlstMapping.MessageType, StringComparison.OrdinalIgnoreCase))
                {
                    _journal.AddError(run, "Import", message.Reference,
                        $"Type de message « {message.Type} » non pris en charge (PAXLST attendu).");
                    continue;
                }

                PaxlstMessage typed;
                try
                {
                    typed = PaxlstParser.ParseMessage(message);
                }
                catch (Exception exception)
                {
                    _journal.AddError(run, "Analyse", message.Reference, exception.Message);
                    continue;
                }

                var now = _clock.Now;
                var entity = new ApisMessage
                {
                    FlightNumber = typed.Flight.Id,
                    DepartureLocation = typed.Flight.DeparturePort,
                    ArrivalLocation = typed.Flight.ArrivalPort,
                    DepartureDate = typed.Flight.DepartureDate,
                    ArrivalDate = typed.Flight.ArrivalDate,
                    CarrierName = typed.Flight.CarrierName,
                    PassengerCount = typed.Passengers.Count(),
                    Status = ApisMessageStatus.Received,
                    SourceFormat = ApisSourceFormats.Edifact,
                    ControlReference = interchange.InterchangeControlReference,
                    SenderId = interchange.Sender,
                    RecipientId = interchange.Recipient,
                    BorderPointId = borderPointId,
                    CreatedDate = now,
                    CreatedByUserId = _currentUser.UserName,
                };

                foreach (var traveller in typed.Travellers)
                {
                    entity.Passengers.Add(new ApisPassenger
                    {
                        DocumentType = traveller.DocumentType,
                        DocumentNumber = traveller.DocumentNumber,
                        CountryOfIssue = traveller.CountryOfIssue,
                        IssueDate = traveller.IssueDate,
                        ExpireDate = traveller.ExpiryDate,
                        Surname = traveller.Surname,
                        GivenName = traveller.FirstName,
                        DateOfBirth = traveller.DateOfBirth,
                        Citizenship = traveller.Nationality,
                        Gender = traveller.Sex,
                        CountryOfResidence = traveller.CountryOfResidence,
                        Status = ApisPassengerStatus.Received,
                        IsPassenger = traveller.Type != PaxlstTravellerType.Crew,
                        CreatedDate = now,
                    });

                    if (traveller.Type == PaxlstTravellerType.Crew) crewTotal++;
                    else passengerTotal++;
                }

                if (storedPath is not null)
                {
                    entity.Files.Add(new ApisMessageFile
                    {
                        FilePath = storedPath,
                        FileName = fileName,
                        SizeBytes = size,
                        ContentHash = hash,
                        CreatedDate = now,
                    });
                }

                _db.Set<ApisMessage>().Add(entity);
                await _db.SaveChangesAsync(ct);
                messageIds.Add(entity.Id);
            }

            foreach (var warning in warnings)
                _journal.AddError(run, "Avertissement", interchange.InterchangeControlReference, warning);

            await _journal.CompleteAsync(run, ExchangeRunStatus.Success, passengerTotal + crewTotal,
                passengerTotal + crewTotal,
                $"Import PAXLST : {messageIds.Count} message(s), {passengerTotal} passager(s), {crewTotal} équipage.", ct);

            var matched = 0;
            var doubtful = 0;
            if (options.Reconcile)
            {
                foreach (var messageId in messageIds)
                {
                    var result = await ReconcileMessageAsync(messageId, options.RegisterDoubtful, ct);
                    matched += result.Matched;
                    doubtful += result.Doubtful;
                }
            }

            var unmatched = passengerTotal - matched - doubtful;
            return new ApisImportResult(interchange.InterchangeControlReference, messageIds, messageIds.Count,
                passengerTotal, crewTotal, matched, doubtful, Math.Max(unmatched, 0), warnings);
        }
        catch (Exception exception)
        {
            await _journal.FailAsync(run, exception, ct);
            throw;
        }
    }

    /// <summary>Importe un fichier PAXLST depuis le disque.</summary>
    public async Task<ApisImportResult> ImportPaxlstFileAsync(string filePath, ApisImportOptions? options = null,
        CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(filePath, ct);
        return await ImportPaxlstAsync(content, Path.GetFileName(filePath), options, ct);
    }

    // -----------------------------------------------------------------------
    // Rapprochement
    // -----------------------------------------------------------------------

    /// <summary>
    /// Rapproche les passagers d'un message avec les documents de voyage locaux
    /// (numéro de document) et le mouvement le plus récent, puis contrôle les
    /// listes d'alerte (mêmes critères que l'enregistrement passeport).
    /// </summary>
    public async Task<ApisReconcileResult> ReconcileMessageAsync(int messageId, bool registerDoubtful = true,
        CancellationToken ct = default)
    {
        var message = await _db.Set<ApisMessage>().Include(m => m.Passengers)
            .FirstOrDefaultAsync(m => m.Id == messageId, ct)
            ?? throw new DomainValidationException("EX_Error_MessageNotFound");

        var run = await _journal.BeginAsync(ExchangeChannels.Apis, ExchangeDirections.Check,
            message.ControlReference, ct: ct);

        try
        {
            var matched = 0;
            var doubtful = 0;
            var registerAutomatically = registerDoubtful && await _doubtfulPersons.IsAutoCheckEnabledAsync(ct);

            foreach (var passenger in message.Passengers)
            {
                var normalized = passenger.DocumentNumber?.Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(normalized))
                {
                    var document = await _db.Set<TravelDocument>()
                        .Where(d => d.DocumentNumber != null && d.DocumentNumber.ToUpper() == normalized)
                        .OrderByDescending(d => d.UpdateDate ?? d.CreateDate)
                        .FirstOrDefaultAsync(ct);

                    if (document is not null)
                    {
                        passenger.MatchedDocumentId = document.Id;
                        passenger.PassportRegistrationId = document.Id.ToString();

                        var movement = await _db.Set<Movement>()
                            .Where(m => m.DocumentId == document.Id)
                            .OrderByDescending(m => m.TravelDate).ThenByDescending(m => m.Id)
                            .FirstOrDefaultAsync(ct);
                        passenger.MatchedMovementId = movement?.Id;
                    }
                }

                // Contrôle des listes d'alerte : personne puis documents (comme l'enregistrement passeport).
                var criteria = new StopListCriteria(
                    Surname: passenger.Surname,
                    GivenName: passenger.GivenName,
                    DocumentNumber: passenger.DocumentNumber,
                    DateOfBirth: passenger.DateOfBirth,
                    CitizenshipId: await ResolveCitizenshipIdAsync(passenger.Citizenship, ct));

                var personHit = await _stopListMatcher.CheckPersonAsync(criteria, ct: ct);
                var documentHit = personHit is null ? await _stopListMatcher.CheckDocumentAsync(criteria, ct: ct) : null;
                var hit = personHit ?? documentHit;

                var priorAction = await _doubtfulPersons.HasPriorActionAsync(
                    passenger.Surname, passenger.GivenName, passenger.DateOfBirth, passenger.DocumentNumber, ct);

                var reasonCode = hit?.Kind == StopListKind.Person ? "FDP"
                    : hit?.Kind == StopListKind.Document ? "FD"
                    : priorAction ? "SSC" : null;

                if (hit is not null || priorAction)
                {
                    passenger.CheckingReason = reasonCode;

                    if (registerAutomatically ||
                        // Un signalement de liste est toujours inscrit : c'est l'objet du rapprochement.
                        hit is not null)
                    {
                        var reasonId = reasonCode is null ? null
                            : await _doubtfulPersons.GetCheckingReasonIdAsync(reasonCode, ct);
                        var note = hit is null
                            ? "Décision antérieure au-delà de l'admission (contrôle APIS)"
                            : $"{hit.Surname} {hit.GivenName} (critère {hit.Criterion}, {hit.ActionCode})".Trim();

                        _db.Set<ApisDoubtfulPerson>().Add(new ApisDoubtfulPerson
                        {
                            ApisPassengerId = passenger.Id,
                            CheckingReason = reasonCode,
                            PerformActionId = null,
                            SupervisorsNote = note,
                            ActionStatus = 0,
                            CreateDate = _clock.Now,
                        });
                        doubtful++;
                    }

                    passenger.Status = ApisPassengerStatus.Doubtful;
                    message.Status = ApisMessageStatus.Alerted;
                }

                if (passenger.Status != ApisPassengerStatus.Doubtful && passenger.MatchedDocumentId is not null)
                {
                    passenger.Status = ApisPassengerStatus.Reconciled;
                    matched++;
                }

                passenger.UpdateDate = _clock.Now;
                passenger.UpdateUser = _currentUser.UserName;
            }

            var processed = message.Passengers.Count;
            await _db.SaveChangesAsync(ct);
            await _journal.CompleteAsync(run, ExchangeRunStatus.Success, processed, matched + doubtful,
                $"Rapprochement : {matched} rapproché(s), {doubtful} douteux, {processed - matched - doubtful} sans correspondance.", ct);

            return new ApisReconcileResult(message.Id, processed, matched, doubtful, processed - matched - doubtful);
        }
        catch (Exception exception)
        {
            await _journal.FailAsync(run, exception, ct);
            throw;
        }
    }

    /// <summary>Résout un code pays (alpha-2, alpha-3 ou code hérité) vers l'identifiant BorderNext.</summary>
    private async Task<int?> ResolveCitizenshipIdAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Trim().ToUpperInvariant();
        return await _db.Set<Country>()
            .Where(c => !c.Revoke && (c.Alpha2 == normalized || c.Alpha3 == normalized || c.ExternalId == code))
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);
    }

    // -----------------------------------------------------------------------
    // Décision superviseur sur un passager douteux
    // -----------------------------------------------------------------------

    /// <summary>Décision du superviseur sur un signalement APIS (héritage :
    /// UpdateAPIDoubtablePersonsAction) : action, note, agent — un signalement déjà
    /// traité ne peut pas l'être deux fois.</summary>
    public async Task ProcessDoubtfulAsync(int doubtfulId, int? performActionId, string? supervisorsNote,
        CancellationToken ct = default)
    {
        var doubtful = await _db.Set<ApisDoubtfulPerson>().Include(d => d.ApisPassenger)
            .FirstOrDefaultAsync(d => d.Id == doubtfulId, ct)
            ?? throw new DomainValidationException("EX_Error_DoubtNotFound");

        if (doubtful.ActionStatus != 0)
            throw new DomainValidationException("EX_Error_DoubtAlreadyProcessed");

        if (performActionId is { } actionId)
        {
            var valid = await _db.Set<Lookup>().AnyAsync(l => l.Id == actionId && !l.Revoke, ct);
            if (!valid) throw new DomainValidationException("EX_Error_InvalidAction");
        }

        doubtful.PerformActionId = performActionId;
        doubtful.SupervisorsNote = supervisorsNote;
        doubtful.Supervisor = _currentUser.UserName;
        doubtful.SupervisorPcNumber = _currentUser.PcNumber;
        doubtful.ActionStatus = 1;
        doubtful.UpdateDate = _clock.Now;
        doubtful.UpdateUser = _currentUser.UserName;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, "ApisDoubtfulPersons", doubtful.Id.ToString(),
            $"Décision superviseur : action {performActionId?.ToString() ?? "-"}.", ct);
    }

    // -----------------------------------------------------------------------
    // Consultation
    // -----------------------------------------------------------------------

    /// <summary>Recherche des messages APIS (période, vol, statut, poste).</summary>
    public async Task<IReadOnlyList<ApisMessage>> SearchMessagesAsync(DateTime? from = null, DateTime? to = null,
        string? flightNumber = null, int? status = null, CancellationToken ct = default)
    {
        var query = _db.Set<ApisMessage>().AsNoTracking().AsQueryable();
        if (from is { } start) query = query.Where(m => (m.ArrivalDate ?? m.CreatedDate) >= start);
        if (to is { } end) query = query.Where(m => (m.ArrivalDate ?? m.CreatedDate) < end);
        if (!string.IsNullOrWhiteSpace(flightNumber))
        {
            var number = flightNumber.Trim().ToUpper();
            query = query.Where(m => m.FlightNumber != null && m.FlightNumber.ToUpper().Contains(number));
        }
        if (status is { } state) query = query.Where(m => m.Status == state);

        return await query.OrderByDescending(m => m.ArrivalDate ?? m.CreatedDate).ThenByDescending(m => m.Id)
            .Take(500).ToListAsync(ct);
    }

    public async Task<ApisMessage?> GetMessageAsync(int messageId, CancellationToken ct = default)
        => await _db.Set<ApisMessage>().AsNoTracking()
            .Include(m => m.Files)
            .FirstOrDefaultAsync(m => m.Id == messageId, ct);

    /// <summary>Passagers d'un message (passagers puis équipage, par nom).</summary>
    public async Task<IReadOnlyList<ApisPassenger>> GetPassengersAsync(int messageId, CancellationToken ct = default)
        => await _db.Set<ApisPassenger>().AsNoTracking()
            .Where(p => p.ApisMessageId == messageId)
            .OrderByDescending(p => p.IsPassenger).ThenBy(p => p.Surname).ThenBy(p => p.GivenName)
            .ToListAsync(ct);

    /// <summary>Recherche d'un passager par numéro de document ou nom (tous messages).</summary>
    public async Task<IReadOnlyList<ApisPassenger>> SearchPassengersAsync(string term, int take = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term)) return Array.Empty<ApisPassenger>();
        var normalized = term.Trim().ToUpper();
        return await _db.Set<ApisPassenger>().AsNoTracking()
            .Where(p => (p.DocumentNumber != null && p.DocumentNumber.ToUpper().Contains(normalized))
                || (p.Surname != null && p.Surname.ToUpper().Contains(normalized)))
            .OrderBy(p => p.Surname).ThenBy(p => p.GivenName)
            .Take(take).ToListAsync(ct);
    }

    /// <summary>Signalements de passagers douteux (en attente par défaut).</summary>
    public async Task<IReadOnlyList<ApisDoubtfulPerson>> GetDoubtfulAsync(bool pendingOnly = true, int take = 300,
        CancellationToken ct = default)
    {
        var query = _db.Set<ApisDoubtfulPerson>().AsNoTracking()
            .Include(d => d.ApisPassenger).ThenInclude(p => p!.ApisMessage)
            .AsQueryable();
        if (pendingOnly) query = query.Where(d => d.ActionStatus == 0);

        return await query.OrderByDescending(d => d.CreateDate).ThenByDescending(d => d.Id)
            .Take(take).ToListAsync(ct);
    }

    /// <summary>Comptages du module (période optionnelle).</summary>
    public async Task<ApisStatistics> GetStatisticsAsync(DateTime? from = null, DateTime? to = null,
        CancellationToken ct = default)
    {
        var messages = _db.Set<ApisMessage>().AsNoTracking().AsQueryable();
        if (from is { } start) messages = messages.Where(m => m.CreatedDate >= start);
        if (to is { } end) messages = messages.Where(m => m.CreatedDate < end);

        var messageCount = await messages.CountAsync(ct);
        var exported = await messages.CountAsync(m => m.Status == ApisMessageStatus.Exported, ct);

        var passengers = _db.Set<ApisPassenger>().AsNoTracking()
            .Where(p => messages.Select(m => m.Id).Contains(p.ApisMessageId));

        return new ApisStatistics(
            messageCount,
            await passengers.CountAsync(p => p.IsPassenger, ct),
            await passengers.CountAsync(p => !p.IsPassenger, ct),
            await passengers.CountAsync(p => p.Status == ApisPassengerStatus.Reconciled, ct),
            await passengers.CountAsync(p => p.Status == ApisPassengerStatus.Doubtful, ct),
            await _db.Set<ApisDoubtfulPerson>().CountAsync(d => d.ActionStatus == 0, ct),
            exported);
    }

    // -----------------------------------------------------------------------
    // Exports
    // -----------------------------------------------------------------------

    /// <summary>
    /// Exporte des messages APIS vers un autre poste (héritage : Export.ExportAPIMessagesXML) :
    /// XML des messages et de leurs passagers, filtre par période de mise à jour et
    /// par localité frontière, marquage « exporté » et journalisation.
    /// </summary>
    public async Task<ApisXmlExportResult> ExportMessagesXmlAsync(DateTime from, DateTime to,
        string? borderPointLocation = null, CancellationToken ct = default)
    {
        var query = _db.Set<ApisMessage>().AsNoTracking()
            .Include(m => m.Passengers)
            .Where(m => (m.UpdateDate ?? m.CreatedDate) >= from && (m.UpdateDate ?? m.CreatedDate) < to);

        if (!string.IsNullOrWhiteSpace(borderPointLocation))
        {
            var location = borderPointLocation.Trim();
            query = query.Where(m => m.DepartureLocation == location || m.ArrivalLocation == location);
        }

        var messages = await query.OrderBy(m => m.Id).ToListAsync(ct);

        var run = await _journal.BeginAsync(ExchangeChannels.Apis, ExchangeDirections.Export,
            $"{from:yyyyMMdd}-{to:yyyyMMdd}", ct: ct);

        try
        {
            var xml = BuildMessagesXml(messages);
            var (path, _, _) = await _files.SaveAsync(
                $"APIS_{from:yyyyMMdd}_{to:yyyyMMdd}.xml", xml, "Exports", ct);

            // Marquage « exporté » des messages émis.
            var ids = messages.Select(m => m.Id).ToList();
            var tracked = await _db.Set<ApisMessage>().Where(m => ids.Contains(m.Id)).ToListAsync(ct);
            foreach (var message in tracked)
            {
                if (message.Status < ApisMessageStatus.Exported)
                {
                    message.Status = ApisMessageStatus.Exported;
                    message.UpdateDate = _clock.Now;
                    message.UpdateUser = _currentUser.UserName;
                }
            }
            await _db.SaveChangesAsync(ct);

            await _journal.CompleteAsync(run, ExchangeRunStatus.Success, messages.Count, messages.Count,
                $"Export XML : {messages.Count} message(s), {messages.Sum(c => c.Passengers.Count)} passager(s) → {path}.", ct);
            await _audit.WriteAsync(AuditEventTypes.DataExported, "ApisMessages", null, path, ct);

            return new ApisXmlExportResult(path, messages.Count, messages.Sum(c => c.Passengers.Count));
        }
        catch (Exception exception)
        {
            await _journal.FailAsync(run, exception, ct);
            throw;
        }
    }

    /// <summary>Régénère les messages APIS stockés au format EDIFACT PAXLST (aller-retour).</summary>
    public async Task<PaxlstExportResult> ExportMessagesPaxlstAsync(IReadOnlyList<int> messageIds,
        string? directory = null, PaxlstGeneratorOptions? options = null, CancellationToken ct = default)
    {
        var messages = await _db.Set<ApisMessage>().AsNoTracking()
            .Include(m => m.Passengers)
            .Where(m => messageIds.Contains(m.Id))
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        if (messages.Count == 0)
            throw new DomainValidationException("EX_Error_NoMessageSelected");

        var typed = messages.Select(ToPaxlst).ToList();
        var generatorOptions = options ?? new PaxlstGeneratorOptions
        {
            PreparedAt = _clock.Now,
            ControlReference = $"{_clock.Now:yyMMddHHmmss}",
        };
        var content = PaxlstGenerator.Generate(typed, generatorOptions);

        var run = await _journal.BeginAsync(ExchangeChannels.Edifact, ExchangeDirections.Export,
            typed[0].MessageReference, ct: ct);

        try
        {
            var fileName = $"PAXLST_{_clock.Now:yyyyMMdd_HHmmss}.edi";
            var (path, _, _) = await _files.SaveAsync(fileName, content, directory ?? "Exports", ct);
            await _journal.CompleteAsync(run, ExchangeRunStatus.Success, typed.Count,
                typed.Count, $"Export PAXLST : {typed.Count} message(s), {typed.Sum(m => m.Travellers.Count)} voyageur(s).", ct);
            await _audit.WriteAsync(AuditEventTypes.DataExported, "ApisMessages", null, path, ct);

            return new PaxlstExportResult(path, typed.Count, typed.Sum(m => m.Travellers.Count),
                generatorOptions.ControlReference ?? string.Empty);
        }
        catch (Exception exception)
        {
            await _journal.FailAsync(run, exception, ct);
            throw;
        }
    }

    /// <summary>
    /// Génère un message PAXLST à partir des mouvements locaux d'une période
    /// (déclaration des passagers enregistrés au poste), groupés par vol/passage.
    /// </summary>
    public async Task<PaxlstExportResult> GeneratePaxlstFromMovementsAsync(DateTime from, DateTime to,
        string? borderPointCode = null, CancellationToken ct = default)
    {
        var query = _db.Set<Movement>().AsNoTracking()
            .Include(m => m.Document!).ThenInclude(d => d.Citizenship)
            .Include(m => m.TripCrossing!).ThenInclude(c => c.Trip!).ThenInclude(t => t.Carrier!).ThenInclude(c => c.Cultures)
            .Include(m => m.TripCrossing!).ThenInclude(c => c.Trip!).ThenInclude(t => t.FromTerminal)
            .Include(m => m.TripCrossing!).ThenInclude(c => c.Trip!).ThenInclude(t => t.ToTerminal)
            .Where(m => m.TravelDate >= from && m.TravelDate < to);
        if (!string.IsNullOrWhiteSpace(borderPointCode))
            query = query.Where(m => m.BorderPoint != null && m.BorderPoint.ExternalId == borderPointCode);

        var movements = await query.OrderBy(m => m.TravelDate).ThenBy(m => m.Id).Take(5000).ToListAsync(ct);

        var sexCodes = await _db.Set<Lookup>().AsNoTracking()
            .Where(l => l.Type!.Code == "SEX" && l.Code != null)
            .ToDictionaryAsync(l => l.Id, l => l.Code!, ct);

        var run = await _journal.BeginAsync(ExchangeChannels.Edifact, ExchangeDirections.Export,
            $"{from:yyyyMMdd}-{to:yyyyMMdd}", ct: ct);

        try
        {
            var groups = movements
                .GroupBy(m => new
                {
                    Date = m.TripCrossing?.Date.Date ?? m.TravelDate.Date,
                    Flight = m.TripCrossing?.Trip?.ExternalNumber ?? m.TripCrossing?.Trip?.Name,
                    Departure = m.TripCrossing?.Trip?.FromTerminal?.Code,
                    Arrival = m.TripCrossing?.Trip?.ToTerminal?.Code,
                    Carrier = m.TripCrossing?.Trip?.Carrier?.DisplayName,
                })
                .OrderBy(g => g.Key.Date)
                .ToList();

            var typed = new List<PaxlstMessage>();
            foreach (var group in groups)
            {
                var message = new PaxlstMessage
                {
                    Flight = new PaxlstFlight
                    {
                        Id = group.Key.Flight,
                        CarrierName = group.Key.Carrier,
                        DeparturePort = group.Key.Departure,
                        ArrivalPort = group.Key.Arrival,
                        DepartureDate = group.Min(m => m.TravelDate),
                        ArrivalDate = group.Max(m => m.TravelDate),
                    },
                    ReportingParty = new PaxlstReportingParty { Name = _currentUser.UserName ?? "Poste frontière" },
                };

                foreach (var movement in group)
                {
                    var document = movement.Document;
                    message.Travellers.Add(new PaxlstTraveller
                    {
                        Type = PaxlstTravellerType.Passenger,
                        Surname = document?.Surname,
                        FirstName = document?.GivenName,
                        MiddleName = document?.FatherName,
                        DateOfBirth = document?.DateOfBirth,
                        Sex = document?.SexId is { } sexId && sexCodes.TryGetValue(sexId, out var code) ? code : null,
                        Nationality = document?.Citizenship?.Alpha2 ?? document?.Citizenship?.Alpha3,
                        DocumentType = "P",
                        DocumentNumber = document?.DocumentNumber,
                        CountryOfIssue = document?.CountryOfIssue?.Alpha2,
                        IssueDate = document?.IssueDate,
                        ExpiryDate = document?.ExpireDate,
                    });
                }

                typed.Add(message);
            }

            var content = PaxlstGenerator.Generate(typed, new PaxlstGeneratorOptions
            {
                PreparedAt = _clock.Now,
                ControlReference = $"{_clock.Now:yyMMddHHmmss}",
            });

            var fileName = $"PAXLST_MOUVEMENTS_{from:yyyyMMdd}_{to:yyyyMMdd}.edi";
            var (path, _, _) = await _files.SaveAsync(fileName, content, "Exports", ct);

            await _journal.CompleteAsync(run, ExchangeRunStatus.Success, typed.Sum(m => m.Travellers.Count),
                typed.Sum(m => m.Travellers.Count),
                $"Export PAXLST depuis les mouvements : {typed.Count} vol(s), {movements.Count} voyageur(s).", ct);
            await _audit.WriteAsync(AuditEventTypes.DataExported, "Movements", null, path, ct);

            return new PaxlstExportResult(path, typed.Count, typed.Sum(m => m.Travellers.Count), string.Empty);
        }
        catch (Exception exception)
        {
            await _journal.FailAsync(run, exception, ct);
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // Conversions et sérialisation XML
    // -----------------------------------------------------------------------

    /// <summary>Convertit un message APIS stocké en modèle PAXLST (aller-retour EDIFACT).</summary>
    public static PaxlstMessage ToPaxlst(ApisMessage message)
    {
        var typed = new PaxlstMessage
        {
            MessageReference = message.ControlReference is null ? null : $"M{message.Id}",
            Flight = new PaxlstFlight
            {
                Id = message.FlightNumber,
                CarrierName = message.CarrierName,
                DeparturePort = message.DepartureLocation,
                ArrivalPort = message.ArrivalLocation,
                DepartureDate = message.DepartureDate,
                ArrivalDate = message.ArrivalDate,
            },
        };

        foreach (var passenger in message.Passengers)
        {
            typed.Travellers.Add(new PaxlstTraveller
            {
                Type = passenger.IsPassenger ? PaxlstTravellerType.Passenger : PaxlstTravellerType.Crew,
                Surname = passenger.Surname,
                FirstName = passenger.GivenName,
                DateOfBirth = passenger.DateOfBirth,
                Sex = passenger.Gender,
                Nationality = passenger.Citizenship,
                CountryOfResidence = passenger.CountryOfResidence,
                DocumentType = passenger.DocumentType ?? "P",
                DocumentNumber = passenger.DocumentNumber,
                CountryOfIssue = passenger.CountryOfIssue,
                IssueDate = passenger.IssueDate,
                ExpiryDate = passenger.ExpireDate,
            });
        }

        return typed;
    }

    /// <summary>XML des messages et passagers (même forme que le XML d'export hérité).</summary>
    private static string BuildMessagesXml(IReadOnlyList<ApisMessage> messages)
    {
        var builder = new System.Text.StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new System.Text.UTF8Encoding(false),
            OmitXmlDeclaration = true,
        }))
        {
            writer.WriteStartElement("ApisMessages");
            foreach (var message in messages)
            {
                writer.WriteStartElement("ApisMessage");
                WriteAttribute(writer, "ID", message.Id.ToString());
                WriteAttribute(writer, "FlightNumber", message.FlightNumber);
                WriteAttribute(writer, "DepartureLocation", message.DepartureLocation);
                WriteAttribute(writer, "ArrivalLocation", message.ArrivalLocation);
                WriteAttribute(writer, "DepartureDate", message.DepartureDate?.ToString("yyyy-MM-ddTHH:mm:ss"));
                WriteAttribute(writer, "ArrivalDate", message.ArrivalDate?.ToString("yyyy-MM-ddTHH:mm:ss"));
                WriteAttribute(writer, "PassengerCount", message.PassengerCount.ToString());
                WriteAttribute(writer, "Status", message.Status.ToString());
                WriteAttribute(writer, "CreatedDate", message.CreatedDate.ToString("yyyy-MM-ddTHH:mm:ss"));

                foreach (var passenger in message.Passengers)
                {
                    writer.WriteStartElement("ApisPassenger");
                    WriteAttribute(writer, "ID", passenger.Id.ToString());
                    WriteAttribute(writer, "APIMessageID", passenger.ApisMessageId.ToString());
                    WriteAttribute(writer, "PassportRegistrationID", passenger.PassportRegistrationId);
                    WriteAttribute(writer, "DocumentType", passenger.DocumentType);
                    WriteAttribute(writer, "DocumentNumber", passenger.DocumentNumber);
                    WriteAttribute(writer, "CountryOfIssue", passenger.CountryOfIssue);
                    WriteAttribute(writer, "IssueDate", passenger.IssueDate?.ToString("yyyy-MM-ddTHH:mm:ss"));
                    WriteAttribute(writer, "ExpireDate", passenger.ExpireDate?.ToString("yyyy-MM-ddTHH:mm:ss"));
                    WriteAttribute(writer, "Surname", passenger.Surname);
                    WriteAttribute(writer, "GivenName", passenger.GivenName);
                    WriteAttribute(writer, "DateOfBirth", passenger.DateOfBirth?.ToString("yyyy-MM-ddTHH:mm:ss"));
                    WriteAttribute(writer, "Citizenship", passenger.Citizenship);
                    WriteAttribute(writer, "Gender", passenger.Gender);
                    WriteAttribute(writer, "CountryOfResidence", passenger.CountryOfResidence);
                    WriteAttribute(writer, "Status", passenger.Status.ToString());
                    WriteAttribute(writer, "isPassenger", passenger.IsPassenger ? "1" : "0");
                    WriteAttribute(writer, "CreatedDate", passenger.CreatedDate.ToString("yyyy-MM-ddTHH:mm:ss"));
                    WriteAttribute(writer, "UpdateDate", passenger.UpdateDate?.ToString("yyyy-MM-ddTHH:mm:ss"));
                    WriteAttribute(writer, "UpdateUser", passenger.UpdateUser);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        return builder.ToString();
    }

    private static void WriteAttribute(XmlWriter writer, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value)) writer.WriteAttributeString(name, value);
    }
}
