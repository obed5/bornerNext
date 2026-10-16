using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Élément du catalogue des questionnaires.</summary>
public sealed record QuestionnaireListItem(int Id, string Description, int ExpireDays, int QuestionCount, bool Revoke)
{
    public override string ToString() => Description;
}

/// <summary>Réponse proposée à une question.</summary>
public sealed record AnswerItem(int Id, string Answer)
{
    public override string ToString() => Answer;
}

/// <summary>Question telle que posée à l'application d'un questionnaire, avec la
/// réponse donnée le cas échéant.</summary>
public sealed record OpenQuestion(int QuestionId, string Title, string? TypeCode, string? TypeFr, int Order,
    bool IsFollowUp, IReadOnlyList<AnswerItem> Answers, int? SelectedAnswerId, string? OtherAnswer, bool Answered);

/// <summary>État d'une application de questionnaire : questions visibles, réponses,
/// complétude et message de fin éventuel.</summary>
public sealed record QuestionnaireOutcome(int AppliedId, string Questionnaire, DateTime AppliedDate,
    DateTime? ExpiresAt, bool Expired, bool Complete, string? EndingTitle, string? EndingMessage,
    IReadOnlyList<OpenQuestion> Questions);

/// <summary>Résumé d'une application (liste par dossier).</summary>
public sealed record AppliedQuestionnaireSummary(int Id, string Questionnaire, DateTime AppliedDate,
    DateTime? ExpiresAt, bool Expired, string? EndingTitle);

/// <summary>Clé de l'application : document de voyage et/ou enregistrement de passage.</summary>
public sealed record QuestionnaireApplicationRequest(int QuestionnaireId, int? DocumentId, int? MovementId);

/// <summary>
/// Questionnaires du poste (héritage : Questionnaires et tables liées — fiche
/// sanitaire du voyageur) : définition (questions, réponses, questions de suivi
/// conditionnelles, messages de fin), application au dossier d'un voyageur avec
/// durée de validité, saisie des réponses et évaluation du message de fin.
///
/// Sémantique reconstruite d'après la structure héritée (les tables du
/// déploiement étaient vides) : une question de suivi n'est posée que si une
/// réponse déclenchante est sélectionnée (ConditionalQuestions) ; le message de
/// fin est la première règle satisfaite dans l'ordre des réponses enregistrées
/// (QuestionAnswerQuestionnaireEndings).
/// </summary>
public class QuestionnaireService
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public QuestionnaireService(DbContext db, ICurrentUser currentUser, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>Catalogue des questionnaires actifs (avec nombre de rubriques).</summary>
    public async Task<IReadOnlyList<QuestionnaireListItem>> GetQuestionnairesAsync(bool includeRevoked = false,
        CancellationToken ct = default)
    {
        var query = _db.Set<Questionnaire>().AsNoTracking()
            .Include(q => q.Questions).AsQueryable();
        if (!includeRevoked) query = query.Where(q => !q.Revoke);

        return await query.OrderBy(q => q.Description)
            .Select(q => new QuestionnaireListItem(q.Id, q.Description, q.ExpireDays,
                q.Questions.Count(x => !x.Revoke), q.Revoke))
            .ToListAsync(ct);
    }

    /// <summary>Applications d'un dossier (ou toutes), les plus récentes en premier.</summary>
    public async Task<IReadOnlyList<AppliedQuestionnaireSummary>> GetApplicationsAsync(int? documentId = null,
        int? movementId = null, bool includeExpired = true, CancellationToken ct = default)
    {
        var query = _db.Set<AppliedQuestionnaire>().AsNoTracking()
            .Include(a => a.Questionnaire)
            .Include(a => a.Results).ThenInclude(r => r.QuestionAnswer)
            .AsQueryable();

        if (documentId is { } document) query = query.Where(a => a.DocumentId == document);
        if (movementId is { } movement) query = query.Where(a => a.MovementId == movement);
        if (!includeExpired)
        {
            var now = _clock.Now;
            query = query.Where(a => a.ExpiresAt == null || a.ExpiresAt > now);
        }

        var applications = await query.OrderByDescending(a => a.AppliedDate).ThenByDescending(a => a.Id)
            .Take(500).ToListAsync(ct);
        var endings = await _db.Set<QuestionAnswerQuestionnaireEnding>().AsNoTracking()
            .Include(x => x.EndingMessage).ToListAsync(ct);
        var reference = _clock.Now;

        return applications.Select(a => new AppliedQuestionnaireSummary(a.Id,
            a.Questionnaire?.Description ?? "—", a.AppliedDate, a.ExpiresAt, a.IsExpired(reference),
            EndingTitle(a, endings))).ToList();
    }

    /// <summary>Applique un questionnaire à un dossier : validité calculée depuis
    /// ExpireDays ; une seule application active par questionnaire et par dossier.</summary>
    public async Task<int> ApplyAsync(QuestionnaireApplicationRequest request, CancellationToken ct = default)
    {
        var questionnaire = await _db.Set<Questionnaire>().FirstOrDefaultAsync(q => q.Id == request.QuestionnaireId, ct)
            ?? throw new DomainValidationException("HE_Error_QuestionnaireNotFound");
        if (questionnaire.Revoke) throw new DomainValidationException("HE_Error_QuestionnaireRevoked");
        if (request.DocumentId is null && request.MovementId is null)
            throw new DomainValidationException("HE_Error_DossierRequired");

        var now = _clock.Now;
        var existing = await _db.Set<AppliedQuestionnaire>().AnyAsync(a =>
            a.QuestionnaireId == questionnaire.Id
            && (request.DocumentId != null
                ? a.DocumentId == request.DocumentId
                : a.DocumentId == null && a.MovementId == request.MovementId)
            && (a.ExpiresAt == null || a.ExpiresAt > now), ct);
        if (existing) throw new DomainValidationException("HE_Error_AlreadyApplied");

        var application = new AppliedQuestionnaire
        {
            QuestionnaireId = questionnaire.Id,
            DocumentId = request.DocumentId,
            MovementId = request.MovementId,
            AppliedDate = now,
            ExpiresAt = questionnaire.ExpireDays > 0 ? now.AddDays(questionnaire.ExpireDays) : null,
            BorderPointId = _currentUser.BorderPointId
                ?? await StationSettings.GetBorderPointIdAsync(_db, _currentUser, ct),
            AppliedByUserId = _currentUser.UserName,
        };

        _db.Set<AppliedQuestionnaire>().Add(application);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowAdded, nameof(AppliedQuestionnaire),
            application.Id.ToString(), questionnaire.Description, ct);
        return application.Id;
    }

    /// <summary>Vrai si une application active (non expirée) existe déjà pour ce questionnaire
    /// et ce document — utilisé par l'écran passeport pour éviter les doublons.</summary>
    public async Task<bool> HasActiveApplicationAsync(int questionnaireId, int? documentId, int? movementId,
        CancellationToken ct = default)
    {
        var now = _clock.Now;
        return await _db.Set<AppliedQuestionnaire>().AnyAsync(a =>
            a.QuestionnaireId == questionnaireId
            && a.DocumentId == documentId
            && (documentId != null || a.MovementId == movementId)
            && (a.ExpiresAt == null || a.ExpiresAt > now), ct);
    }

    /// <summary>Saisit (ou remplace) la réponse à une question de l'application.</summary>
    public async Task AnswerAsync(int appliedId, int questionId, int? answerId, string? otherAnswer = null,
        CancellationToken ct = default)
    {
        var application = await _db.Set<AppliedQuestionnaire>().FirstOrDefaultAsync(a => a.Id == appliedId, ct)
            ?? throw new DomainValidationException("HE_Error_ApplicationNotFound");

        var belongs = await _db.Set<QuestionnaireQuestion>().AnyAsync(x =>
            x.QuestionnaireId == application.QuestionnaireId && x.QuestionId == questionId && !x.Revoke, ct);
        if (!belongs) throw new DomainValidationException("HE_Error_QuestionNotInQuestionnaire");

        if (answerId is { } id)
        {
            var valid = await _db.Set<QuestionAnswer>().AnyAsync(a => a.Id == id && a.QuestionId == questionId
                && !a.Revoke, ct);
            if (!valid) throw new DomainValidationException("HE_Error_AnswerNotInQuestion");
        }

        var existing = await _db.Set<QuestionnaireResult>()
            .Where(r => r.AppliedQuestionnaireId == appliedId
                && r.QuestionAnswer!.QuestionId == questionId)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            foreach (var result in existing.Skip(1))
                _db.Set<QuestionnaireResult>().Remove(result);

            var current = existing[0];
            if (answerId is null && string.IsNullOrWhiteSpace(otherAnswer))
            {
                _db.Set<QuestionnaireResult>().Remove(current);
            }
            else
            {
                current.QuestionAnswerId = answerId ?? current.QuestionAnswerId;
                current.OtherAnswer = otherAnswer;
                current.AnsweredAt = _clock.Now;
            }
        }
        else if (answerId is not null || !string.IsNullOrWhiteSpace(otherAnswer))
        {
            var fallback = await _db.Set<QuestionAnswer>()
                .Where(a => a.QuestionId == questionId && !a.Revoke)
                .OrderBy(a => a.Id).FirstOrDefaultAsync(ct)
                ?? throw new DomainValidationException("HE_Error_AnswerNotInQuestion");

            _db.Set<QuestionnaireResult>().Add(new QuestionnaireResult
            {
                AppliedQuestionnaireId = appliedId,
                QuestionAnswerId = answerId ?? fallback.Id,
                OtherAnswer = otherAnswer,
                AnsweredAt = _clock.Now,
            });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditEventTypes.RowUpdated, nameof(QuestionnaireResult), appliedId.ToString(),
            $"Question {questionId} renseignée.", ct);
    }

    /// <summary>Contrôle de complétude : toutes les questions visibles (principales et
    /// suivis déclenchés) doivent être répondues.</summary>
    public async Task ValidateAsync(int appliedId, CancellationToken ct = default)
    {
        var outcome = await GetOutcomeAsync(appliedId, ct);
        if (outcome.Questions.Any(q => !q.Answered))
            throw new DomainValidationException("HE_Error_Incomplete");
    }

    /// <summary>État complet d'une application : questions visibles, réponses, message de fin.</summary>
    public async Task<QuestionnaireOutcome> GetOutcomeAsync(int appliedId, CancellationToken ct = default)
    {
        var application = await _db.Set<AppliedQuestionnaire>().AsNoTracking()
            .Include(a => a.Questionnaire)
            .Include(a => a.Results).ThenInclude(r => r.QuestionAnswer)
            .FirstOrDefaultAsync(a => a.Id == appliedId, ct)
            ?? throw new DomainValidationException("HE_Error_ApplicationNotFound");

        return await BuildOutcomeAsync(application, ct);
    }

    private async Task<QuestionnaireOutcome> BuildOutcomeAsync(AppliedQuestionnaire application,
        CancellationToken ct)
    {
        var links = await _db.Set<QuestionnaireQuestion>().AsNoTracking()
            .Include(x => x.Question).ThenInclude(q => q!.QuestionType).ThenInclude(t => t!.Cultures)
            .Include(x => x.Question).ThenInclude(q => q!.Answers)
            .Where(x => x.QuestionnaireId == application.QuestionnaireId && !x.Revoke)
            .OrderBy(x => x.QuestionOrder)
            .ToListAsync(ct);
        var conditionals = await _db.Set<ConditionalQuestion>().AsNoTracking()
            .Where(c => !c.Revoke)
            .ToListAsync(ct);
        var endings = await _db.Set<QuestionAnswerQuestionnaireEnding>().AsNoTracking()
            .Include(x => x.EndingMessage).ToListAsync(ct);

        var answered = application.Results.ToDictionary(r => r.QuestionAnswer!.QuestionId, r => r);
        var selectedAnswers = answered.Values.Select(r => r.QuestionAnswerId).ToHashSet();

        var questions = new List<OpenQuestion>();
        foreach (var link in links)
        {
            var question = link.Question!;
            if (question.Revoke) continue;

            var isFollowUp = question.IsFollowUpQuestion && !question.IsInitialQuestion;
            if (isFollowUp)
            {
                // Posée uniquement si une réponse déclenchante est sélectionnée.
                var triggered = conditionals.Any(c => c.QuestionId == question.Id && selectedAnswers.Contains(c.QuestionAnswerId));
                if (!triggered) continue;
            }
            else if (!question.IsInitialQuestion && !conditionals.Any(c => c.QuestionId == question.Id))
            {
                // Question ni principale ni suivi : traitée comme principale.
            }

            answered.TryGetValue(question.Id, out var result);
            questions.Add(new OpenQuestion(
                question.Id,
                question.Title,
                question.QuestionType?.Code,
                question.QuestionType?.Cultures.FirstOrDefault(c => c.Culture == "fr")?.Description,
                link.QuestionOrder,
                isFollowUp,
                question.Answers.Where(a => !a.Revoke).OrderBy(a => a.Id)
                    .Select(a => new AnswerItem(a.Id, a.Answer)).ToList(),
                result?.QuestionAnswerId,
                result?.OtherAnswer,
                result is not null));
        }

        var now = _clock.Now;
        return new QuestionnaireOutcome(
            application.Id,
            application.Questionnaire?.Description ?? "—",
            application.AppliedDate,
            application.ExpiresAt,
            application.IsExpired(now),
            questions.Count > 0 && questions.All(q => q.Answered),
            EndingFor(application, endings)?.EndingMessage?.Title,
            EndingFor(application, endings)?.EndingMessage?.MessageBody,
            questions);
    }

    private static QuestionAnswerQuestionnaireEnding? EndingFor(AppliedQuestionnaire application,
        IReadOnlyList<QuestionAnswerQuestionnaireEnding> endings)
    {
        foreach (var result in application.Results.OrderBy(r => r.Id))
        {
            var ending = endings.FirstOrDefault(x => x.QuestionAnswerId == result.QuestionAnswerId);
            if (ending is not null) return ending;
        }

        return null;
    }

    private static string? EndingTitle(AppliedQuestionnaire application,
        IReadOnlyList<QuestionAnswerQuestionnaireEnding> endings)
        => EndingFor(application, endings)?.EndingMessage?.Title;
}
