using BorderNext.Application.Abstractions;
using BorderNext.Application.Reporting;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Persistence.Seeding;
using BorderNext.Infrastructure.Reporting;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Référentiels et dossiers communs aux tests des rapports et de la santé (Lot 8).</summary>
internal static class ReportSeed
{
    public static void SeedReferentials(BorderNextDbContext db)
    {
        BorderOpsSeed.SeedReferentials(db);
        HealthDataSeeder.SeedAsync(db).GetAwaiter().GetResult();
    }
}

/// <summary>Éditions : catalogue complet, agrégations et fiches individuelles.</summary>
public class ReportServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly ReportService _service;
    private readonly MovementService _movements;
    private readonly DocumentService _documents;

    public ReportServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"rp-{Guid.NewGuid()}").Options);
        ReportSeed.SeedReferentials(_db);
        var audit = new AuditWriter(_db, _currentUser, _clock);
        _service = new ReportService(_db, _currentUser, _clock);
        _movements = new MovementService(_db, _currentUser, _clock, audit);
        _documents = new DocumentService(_db, _currentUser, _clock, audit);
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3, BorderPointId = 2 }, "IMMIG_OFF");
        _currentUser.SetPcNumber("A01");
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> AddMovementAsync(string number, string direction, bool granted, DateTime? date = null,
        int? country = null, int? reason = null, string surname = "MBALA", string given = "JEAN")
    {
        var documentId = await _documents.CreateAsync(DossierSeed.CompleteDocument(number)
            with { Surname = surname, GivenName = given, CitizenshipId = country ?? 1 });
        var movementId = await _movements.RegisterAsync(new MovementRequest(
            documentId, direction, date ?? new DateTime(2026, 10, 5, 10, 0, 0),
            TransportTypeId: null, IsDriver: false, TravelReasonId: reason, Granted: granted,
            DeportationReasonId: granted ? null : 8, IsTransit: false, HasVisa: false, VisaId: null,
            ChildrenCount: 0, Note: null, ResidentCountryId: null, DestinationCountryId: null,
            Occupation: null, AccommodationAddress: null, AccommodationTypeId: null,
            DurationOfStay: null, TripCrossingId: null));
        return movementId;
    }

    private static ReportParameters Period(int fromMonth = 10, int fromDay = 1, int toMonth = 10, int toDay = 31)
        => new(From: new DateTime(2026, fromMonth, fromDay), To: new DateTime(2026, toMonth, toDay));

    [Fact]
    public async Task Catalog_EveryReport_Builds()
    {
        // Chaque édition du catalogue doit produire un document avec un titre.
        await AddMovementAsync("OP100001", MovementService.EntryCode, granted: true);
        await AddMovementAsync("OP100002", MovementService.ExitCode, granted: false);

        foreach (var definition in ReportCatalog.All)
        {
            var parameters = Period() with { DocumentNumber = "OP100001" };
            if (definition.Id == ReportCatalog.BlankQuestionnaire) parameters = new ReportParameters();

            // Les impressions individuelles qui exigent une pièce absente lèvent une
            // erreur métier explicite — les autres doivent aboutir.
            try
            {
                var document = await _service.BuildAsync(definition.Id, parameters);
                Assert.False(string.IsNullOrWhiteSpace(document.TitleKey), definition.Id);
            }
            catch (DomainValidationException ex)
            {
                Assert.True(ex.ErrorKey == "RP_Error_RecordNotFound", $"{definition.Id} : {ex.ErrorKey}");
                Assert.StartsWith("Docs_", definition.Id);
            }
        }
    }

    [Fact]
    public async Task MovementSummary_CountsDirectionsAndRefusals()
    {
        await AddMovementAsync("OP200001", MovementService.EntryCode, granted: true);
        await AddMovementAsync("OP200002", MovementService.EntryCode, granted: false);
        await AddMovementAsync("OP200003", MovementService.ExitCode, granted: true);

        var document = await _service.BuildAsync(ReportCatalog.MovementSummary, Period());

        var table = Assert.Single(document.Tables);
        Assert.Equal(3, table.Rows.Count); // Entrées, Sorties, Total
        var entries = table.Rows[0];
        Assert.Equal("Entrées", entries[0]);
        Assert.Equal("2", entries[1]);
        Assert.Equal("1", entries[3]); // refus
        var exits = table.Rows[1];
        Assert.Equal("Sorties", exits[0]);
        Assert.Equal("1", exits[1]);
        Assert.Contains(document.Summary, s => s.LabelKey == "RP_Sum_RefusalRate" && s.Value != "—");
    }

    [Fact]
    public async Task DailyReport_GroupsByDayAndAddsTotals()
    {
        await AddMovementAsync("OP300001", MovementService.EntryCode, true, new DateTime(2026, 10, 5));
        await AddMovementAsync("OP300002", MovementService.EntryCode, true, new DateTime(2026, 10, 5));
        await AddMovementAsync("OP300003", MovementService.ExitCode, true, new DateTime(2026, 10, 6));

        var document = await _service.BuildAsync(ReportCatalog.MovementsDaily, Period());

        var table = Assert.Single(document.Tables);
        Assert.Equal(3, table.Rows.Count); // 2 jours + total
        Assert.Equal("05/10/2026", table.Rows[0][0]);
        Assert.Equal("2", table.Rows[0][1]);
        Assert.Equal("06/10/2026", table.Rows[1][0]);
        Assert.Equal("Total", table.Rows[2][0]);
        Assert.Contains(document.Summary, s => s.LabelKey == "RP_Sum_Days" && s.Value == "2");
    }

    [Fact]
    public async Task Register_AppliesDirectionAndDocumentFilters()
    {
        await AddMovementAsync("OP400001", MovementService.EntryCode, true, surname: "KABEYA");
        await AddMovementAsync("OP400002", MovementService.ExitCode, true, surname: "MUKENDI");

        var entries = await _service.BuildAsync(ReportCatalog.MovementRegister,
            Period() with { DirectionCode = MovementService.EntryCode });
        Assert.Single(Assert.Single(entries.Tables).Rows);

        var byName = await _service.BuildAsync(ReportCatalog.MovementRegister,
            Period() with { Surname = "MUKENDI" });
        var row = Assert.Single(Assert.Single(byName.Tables).Rows);
        Assert.Equal("MUKENDI", row[1]);
    }

    [Fact]
    public async Task NationalitiesSummary_SortsAndShares()
    {
        await AddMovementAsync("OP500001", MovementService.EntryCode, true, country: 1);
        await AddMovementAsync("OP500002", MovementService.EntryCode, true, country: 2);
        await AddMovementAsync("OP500003", MovementService.EntryCode, true, country: 2);

        var document = await _service.BuildAsync(ReportCatalog.NationalitiesSummary, Period());

        var table = Assert.Single(document.Tables);
        Assert.Equal(3, table.Rows.Count); // 2 nationalités + total
        Assert.Equal("3", table.Rows[^1][1]); // total avant la dernière ligne
        Assert.Equal("Total", table.Rows[^1][0]);
        Assert.Contains(document.Summary, s => s.LabelKey == "RP_Sum_Nationalities" && s.Value == "2");
    }

    [Fact]
    public async Task MapData_JoinsGeoPoints()
    {
        await AddMovementAsync("OP600001", MovementService.EntryCode, true, country: 1);

        var document = await _service.BuildAsync(ReportCatalog.NationalitiesMapData, Period());

        var table = Assert.Single(document.Tables);
        Assert.NotEmpty(table.Rows);
        Assert.Equal(6, table.Columns.Count);
        Assert.Contains(document.Summary, s => s.LabelKey == "RP_Sum_LocatedCountries");
    }

    [Fact]
    public async Task Trends_CreatesMonthByNationalityMatrix()
    {
        await AddMovementAsync("OP700001", MovementService.EntryCode, true, new DateTime(2026, 9, 5), country: 1);
        await AddMovementAsync("OP700002", MovementService.EntryCode, true, new DateTime(2026, 10, 5), country: 1);

        var document = await _service.BuildAsync(ReportCatalog.NationalitiesTrends,
            new ReportParameters(From: new DateTime(2026, 9, 1), To: new DateTime(2026, 10, 31)));

        var table = Assert.Single(document.Tables);
        Assert.Equal("RP_Table_Trends", table.TitleKey);
        Assert.Equal(3, table.Rows.Count); // 2 mois + total
        Assert.Equal("09/2026", table.Rows[0][0]);
        Assert.Equal("Total", table.Rows[^1][0]);
    }

    [Fact]
    public async Task ForeignersByAge_UsesPermitDocuments()
    {
        // Permis rattaché à un document existant (date de naissance 1990).
        var documentId = await _documents.CreateAsync(DossierSeed.CompleteDocument("OP800001"));
        var permit = new Permit
        {
            Number = "PERMIT-2026-000777", DocumentId = documentId,
            IssueDate = new DateTime(2026, 10, 10), CreateDate = new DateTime(2026, 10, 10),
        };
        _db.Permits.Add(permit);
        await _db.SaveChangesAsync();

        var document = await _service.BuildAsync(ReportCatalog.ForeignersByAge,
            new ReportParameters(From: new DateTime(2026, 10, 1), To: new DateTime(2026, 10, 31)));

        var table = Assert.Single(document.Tables);
        Assert.Contains(table.Rows, r => (string?)r[0] == "31-45" && (string?)r[1] == "1");
    }

    [Fact]
    public async Task VisaStatistics_GroupsByType()
    {
        var documentId = await _documents.CreateAsync(DossierSeed.CompleteDocument("OP900001"));
        _db.Visas.Add(new Visa
        {
            Number = "VISA-2026-000001", VisaTypeId = 10, DocumentId = documentId,
            IssueDate = new DateTime(2026, 10, 3), ExpireDate = new DateTime(2027, 1, 3), Duration = 90,
        });
        await _db.SaveChangesAsync();

        var document = await _service.BuildAsync(ReportCatalog.VisaStatistics, Period());

        var table = Assert.Single(document.Tables);
        Assert.Equal(2, table.Rows.Count); // un type + total
        Assert.Contains(document.Summary, s => s.LabelKey == "RP_Sum_Visas" && s.Value == "1");
    }

    [Fact]
    public async Task StopListPersons_AppliesNameFilter()
    {
        _db.PersonsStopList.Add(WatchlistSeed.Person(surname: "KABILA", givenName: "JOSEPH"));
        _db.PersonsStopList.Add(WatchlistSeed.Person(surname: "MUKENDI", givenName: "ANTOINE"));
        await _db.SaveChangesAsync();

        var document = await _service.BuildAsync(ReportCatalog.StopListPersons,
            new ReportParameters(Surname: "KABILA"));

        var table = Assert.Single(document.Tables);
        Assert.Single(table.Rows);
        Assert.Equal("KABILA", table.Rows[0][0]);
    }

    [Fact]
    public async Task RegistrationForm_FindsLatestMovement()
    {
        await AddMovementAsync("OP110001", MovementService.EntryCode, true, new DateTime(2026, 10, 5));
        await AddMovementAsync("OP110001", MovementService.ExitCode, true, new DateTime(2026, 10, 9));

        var document = await _service.BuildAsync(ReportCatalog.RegistrationForm,
            new ReportParameters(DocumentNumber: "OP110001"));

        Assert.Equal(ReportCatalog.RegistrationForm, document.ReportId);
        Assert.True(document.Tables.Count >= 3); // identité, document, passage
        Assert.Contains(document.Summary, s => s.LabelKey == "RP_Sum_MovementId");
    }

    [Fact]
    public async Task IndividualForms_WithoutRecord_ReportNotFound()
    {
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.BuildAsync(ReportCatalog.VisaForm, new ReportParameters(DocumentNumber: "ABSENT")));
        Assert.Equal("RP_Error_RecordNotFound", exception.ErrorKey);
    }

    [Fact]
    public async Task BlankQuestionnaire_ListsSeededQuestions()
    {
        var document = await _service.BuildAsync(ReportCatalog.BlankQuestionnaire, new ReportParameters());

        // Le questionnaire sanitaire semé porte 7 questions.
        var questions = document.Tables.Single(t => t.TitleKey == "RP_Section_Questions");
        Assert.Equal(7, questions.Rows.Count);
        Assert.Contains(document.Summary, s => s.LabelKey == "RP_Sum_Questions" && s.Value == "7");
    }

    [Fact]
    public async Task UnknownReport_IsRejected()
    {
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.BuildAsync("Inconnu", new ReportParameters()));
        Assert.Equal("RP_Error_UnknownReport", exception.ErrorKey);
    }
}

/// <summary>Questionnaires santé : application, questions conditionnelles, évaluation, validité.</summary>
public class QuestionnaireServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly QuestionnaireService _service;
    private int _questionnaireId;
    private int _documentId;

    public QuestionnaireServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"he-{Guid.NewGuid()}").Options);
        ReportSeed.SeedReferentials(_db);
        _service = new QuestionnaireService(_db, _currentUser, _clock,
            new AuditWriter(_db, _currentUser, _clock));
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 3, BorderPointId = 2 }, "IMMIG_OFF");

        var documents = new DocumentService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        _documentId = documents.CreateAsync(DossierSeed.CompleteDocument("OP123456")).GetAwaiter().GetResult();
        _questionnaireId = _db.Questionnaires.Single().Id;
    }

    public void Dispose() => _db.Dispose();

    private Task<int> ApplyAsync() => _service.ApplyAsync(
        new QuestionnaireApplicationRequest(_questionnaireId, _documentId, null));

    private int QuestionId(string tag) => _db.Questions.Single(q => q.QuestionTag == tag).Id;
    private int AnswerId(string tag, string label) => _db.QuestionAnswers
        .Single(a => a.Question!.QuestionTag == tag && a.Answer == label).Id;

    [Fact]
    public async Task Seeder_CreatesCompleteQuestionnaire()
    {
        Assert.Equal(7, _db.Questions.Count());
        Assert.Equal(6, _db.ConditionalQuestions.Count()); // 1 + 4 + 1 déclencheurs
        Assert.Equal(3, _db.QuestionnaireEndingMessages.Count());
        Assert.Equal(5, _db.QuestionAnswerQuestionnaireEndings.Count());
        Assert.Equal(5, _db.Lookups.Count(l => l.Type!.Code == "QUESTION_TYPE"));
        Assert.Equal(249, _db.CountryGeoPoints.Count());

        // Le semis est idempotent.
        await HealthDataSeeder.SeedAsync(_db);
        Assert.Equal(7, _db.Questions.Count());
        Assert.Equal(249, _db.CountryGeoPoints.Count());
    }

    [Fact]
    public async Task Apply_SetsExpiryFromQuestionnaireValidity()
    {
        var appliedId = await ApplyAsync();

        var applied = await _db.AppliedQuestionnaires.SingleAsync(a => a.Id == appliedId);
        Assert.Equal(_documentId, applied.DocumentId);
        Assert.Equal((short)2, applied.BorderPointId);
        Assert.Equal("jdoe", applied.AppliedByUserId);
        Assert.NotNull(applied.ExpiresAt);
        Assert.Equal(30, (applied.ExpiresAt!.Value.Date - applied.AppliedDate.Date).Days);
    }

    [Fact]
    public async Task Apply_Twice_IsRejected()
    {
        await ApplyAsync();
        var exception = await Assert.ThrowsAsync<DomainValidationException>(ApplyAsync);
        Assert.Equal("HE_Error_AlreadyApplied", exception.ErrorKey);
    }

    [Fact]
    public async Task Apply_WithoutDossier_IsRejected()
    {
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.ApplyAsync(new QuestionnaireApplicationRequest(_questionnaireId, null, null)));
        Assert.Equal("HE_Error_DossierRequired", exception.ErrorKey);
    }

    [Fact]
    public async Task OpenQuestions_ShowFollowUpOnlyAfterTriggeringAnswer()
    {
        var appliedId = await ApplyAsync();

        var initial = await _service.GetOutcomeAsync(appliedId);
        Assert.Equal(4, initial.Questions.Count); // Q1, Q2, Q4, Q6 (pas de suivis)
        Assert.DoesNotContain(initial.Questions, q => q.Title.Contains("Date de la vaccination"));

        await _service.AnswerAsync(appliedId, QuestionId(HealthQuestionTags.YellowFeverVaccinated),
            AnswerId(HealthQuestionTags.YellowFeverVaccinated, "Oui"));

        var afterYes = await _service.GetOutcomeAsync(appliedId);
        Assert.Equal(5, afterYes.Questions.Count);
        Assert.Contains(afterYes.Questions, q => q.Title.Contains("Date de la vaccination") && q.IsFollowUp);
    }

    [Fact]
    public async Task Answers_UpsertSingleResultPerQuestion()
    {
        var appliedId = await ApplyAsync();
        var questionId = QuestionId(HealthQuestionTags.Symptoms);
        var fever = AnswerId(HealthQuestionTags.Symptoms, "Fièvre");
        var none = AnswerId(HealthQuestionTags.Symptoms, "Aucun");

        await _service.AnswerAsync(appliedId, questionId, fever);
        await _service.AnswerAsync(appliedId, questionId, none);

        var results = await _db.QuestionnaireResults.Include(r => r.QuestionAnswer).ToListAsync();
        var result = Assert.Single(results);
        Assert.Equal(none, result.QuestionAnswerId);
    }

    [Fact]
    public async Task Answer_OnForeignQuestion_IsRejected()
    {
        var appliedId = await ApplyAsync();
        var otherQuestion = _db.Questions.First(q => q.QuestionTag == HealthQuestionTags.ContactDetail).Id;
        // Question de suivi présente dans le questionnaire mais sans réponse déclenchante :
        // la saisie reste autorisée ; une question inconnue est refusée.
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.AnswerAsync(appliedId, otherQuestion + 999, null, "texte"));
        Assert.Equal("HE_Error_QuestionNotInQuestionnaire", exception.ErrorKey);
    }

    [Fact]
    public async Task Validate_RequiresAllVisibleQuestions()
    {
        var appliedId = await ApplyAsync();

        var incomplete = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.ValidateAsync(appliedId));
        Assert.Equal("HE_Error_Incomplete", incomplete.ErrorKey);

        await _service.AnswerAsync(appliedId, QuestionId(HealthQuestionTags.FromEndemicArea),
            AnswerId(HealthQuestionTags.FromEndemicArea, "Non"));
        await _service.AnswerAsync(appliedId, QuestionId(HealthQuestionTags.YellowFeverVaccinated),
            AnswerId(HealthQuestionTags.YellowFeverVaccinated, "Oui"));
        // Le suivi « date » devient visible : il faut le renseigner (texte libre).
        var dateQuestion = QuestionId(HealthQuestionTags.YellowFeverDate);
        var placeholder = _db.QuestionAnswers.Single(a => a.QuestionId == dateQuestion).Id;
        await _service.AnswerAsync(appliedId, dateQuestion, placeholder, "15/09/2026");
        await _service.AnswerAsync(appliedId, QuestionId(HealthQuestionTags.Symptoms),
            AnswerId(HealthQuestionTags.Symptoms, "Aucun"));
        await _service.AnswerAsync(appliedId, QuestionId(HealthQuestionTags.ContactWithSickPerson),
            AnswerId(HealthQuestionTags.ContactWithSickPerson, "Non"));

        await _service.ValidateAsync(appliedId); // ne lève pas

        var outcome = await _service.GetOutcomeAsync(appliedId);
        Assert.True(outcome.Complete);
        Assert.Equal("Libre pratique", outcome.EndingTitle);
    }

    [Fact]
    public async Task Outcome_FirstMatchedRuleWins()
    {
        var appliedId = await ApplyAsync();

        // Vaccination absente (règle « orientation médicale ») puis contact (surveillance) :
        // la première réponse enregistrée détermine le message de fin.
        await _service.AnswerAsync(appliedId, QuestionId(HealthQuestionTags.YellowFeverVaccinated),
            AnswerId(HealthQuestionTags.YellowFeverVaccinated, "Non"));
        await _service.AnswerAsync(appliedId, QuestionId(HealthQuestionTags.ContactWithSickPerson),
            AnswerId(HealthQuestionTags.ContactWithSickPerson, "Oui"));

        var outcome = await _service.GetOutcomeAsync(appliedId);
        Assert.Equal("Orientation vers le service médical", outcome.EndingTitle);
        Assert.Contains("zone d'endémie", outcome.EndingMessage);
    }

    [Fact]
    public async Task Applications_ListByDocument_MarksExpired()
    {
        var appliedId = await ApplyAsync();
        var summary = Assert.Single(await _service.GetApplicationsAsync(documentId: _documentId));
        Assert.Equal(appliedId, summary.Id);
        Assert.False(summary.Expired);
        Assert.Equal("Fiche sanitaire du voyageur", summary.Questionnaire);

        // Validité dépassée : l'application n'est plus « active ».
        var applied = await _db.AppliedQuestionnaires.SingleAsync();
        applied.ExpiresAt = _clock.Now.AddDays(-1);
        await _db.SaveChangesAsync();

        Assert.Empty(await _service.GetApplicationsAsync(documentId: _documentId, includeExpired: false));
        Assert.True((await _service.GetApplicationsAsync(documentId: _documentId)).Single().Expired);
        Assert.False(await _service.HasActiveApplicationAsync(_questionnaireId, _documentId, null));
    }

    [Fact]
    public async Task Statistics_ThroughReports_CountApplications()
    {
        var appliedId = await ApplyAsync();
        await _service.AnswerAsync(appliedId, QuestionId(HealthQuestionTags.Symptoms),
            AnswerId(HealthQuestionTags.Symptoms, "Fièvre"));
        var detailQuestion = QuestionId(HealthQuestionTags.SymptomsDetail);
        var placeholder = _db.QuestionAnswers.Single(a => a.QuestionId == detailQuestion).Id;
        await _service.AnswerAsync(appliedId, detailQuestion, placeholder, "Fièvre depuis 2 jours");

        var reports = new ReportService(_db, _currentUser, _clock);
        var document = await reports.BuildAsync(ReportCatalog.QuestionnaireStatistics,
            new ReportParameters(From: _clock.Now.Date.AddDays(-1), To: _clock.Now.Date.AddDays(1)));

        Assert.Contains(document.Summary, s => s.LabelKey == "RP_Sum_Questionnaires" && s.Value == "1");
        Assert.Contains(document.Tables, t => t.TitleKey == null && t.Rows.Any(r =>
            (string?)r[5] == "1")); // applications avec message de fin

        var answers = await reports.BuildAsync(ReportCatalog.QuestionnaireAnswers,
            new ReportParameters(From: _clock.Now.Date.AddDays(-1), To: _clock.Now.Date.AddDays(1)));
        Assert.True(answers.Tables.Count >= 2); // au moins deux questions répondues
    }
}

/// <summary>Antécédents médicaux des permis (héritage : PermitMedicalHistory).</summary>
public class PermitMedicalHistoryTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly SystemClock _clock = new();
    private readonly PermitService _service;
    private int _permitId;

    public PermitMedicalHistoryTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"pm-{Guid.NewGuid()}").Options);
        ReportSeed.SeedReferentials(_db);
        _service = new PermitService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        _currentUser.Set(new User { Id = "U001", UserName = "jdoe", UserRoleId = 5, BorderPointId = 2 }, "VISA_OFF");

        var documents = new DocumentService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        var documentId = documents.CreateAsync(DossierSeed.CompleteDocument("OP210001")).GetAwaiter().GetResult();
        _permitId = _service.CreateAsync(new PermitSaveRequest("PERMIT-TEST-1", documentId, null, null,
            new DateTime(2026, 10, 1), new DateTime(2027, 10, 1), null, null, null, null, null, null, null,
            null, null)).GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Save_CreatesThenUpdatesSingleRecord()
    {
        await _service.SaveMedicalHistoryAsync(_permitId, new PermitMedicalRequest(
            CurrentIllness: "Paludisme", MalariaSuffered: true, YellowFeverVaccinated: true,
            HealthCentreVisited: true));

        var first = await _service.GetMedicalHistoryAsync(_permitId);
        Assert.NotNull(first);
        Assert.Equal("Paludisme", first!.CurrentIllness);
        Assert.True(first.MalariaSuffered);
        Assert.True(first.YellowFeverVaccinated);

        await _service.SaveMedicalHistoryAsync(_permitId, new PermitMedicalRequest(
            CurrentIllness: "Grippe", MalariaSuffered: true, YellowFeverVaccinated: false,
            ChronicDisease: true));

        var updated = await _service.GetMedicalHistoryAsync(_permitId);
        Assert.NotNull(updated);
        Assert.Equal(first.Id, updated!.Id); // même dossier mis à jour
        Assert.Equal("Grippe", updated.CurrentIllness);
        Assert.False(updated.YellowFeverVaccinated);
        Assert.True(updated.ChronicDisease);
        Assert.Equal("jdoe", updated.UpdateUser);
        Assert.Single(_db.PermitMedicalHistories);
    }

    [Fact]
    public async Task Save_UnknownPermit_IsRejected()
    {
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.SaveMedicalHistoryAsync(999999, new PermitMedicalRequest()));
        Assert.Equal("PV_Error_PermitNotFound", exception.ErrorKey);
    }

    [Fact]
    public async Task Save_WithForeignDependant_IsRejected()
    {
        var documents = new DocumentService(_db, _currentUser, _clock, new AuditWriter(_db, _currentUser, _clock));
        var otherDocument = await documents.CreateAsync(DossierSeed.CompleteDocument("OP210002"));
        var otherPermit = await _service.CreateAsync(new PermitSaveRequest("PERMIT-TEST-2", otherDocument, null,
            null, new DateTime(2026, 10, 1), null, null, null, null, null, null, null, null, null, null));
        var dependantId = await _service.AddDependantAsync(otherPermit, new DependantRequest(
            "ENFANT", "AINE", null, new DateTime(2015, 1, 1), null, null, null, null, null, null, null));

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.SaveMedicalHistoryAsync(_permitId, new PermitMedicalRequest(PermitDependantId: dependantId)));
        Assert.Equal("PV_Error_DependantNotFound", exception.ErrorKey);
    }

    [Fact]
    public async Task Save_ForDependantOfPermit_IsAccepted_AndRemovedWithDependant()
    {
        var dependantId = await _service.AddDependantAsync(_permitId, new DependantRequest(
            "ENFANT", "CADET", null, new DateTime(2018, 1, 1), null, null, null, null, null, null, null));

        await _service.SaveMedicalHistoryAsync(_permitId, new PermitMedicalRequest(
            PermitDependantId: dependantId, PolioVaccinated: true));

        var dependant = await _service.GetMedicalHistoryAsync(_permitId, dependantId);
        Assert.NotNull(dependant);
        Assert.True(dependant!.PolioVaccinated);

        await _service.RemoveDependantAsync(dependantId);
        Assert.Null(await _service.GetMedicalHistoryAsync(_permitId, dependantId));
    }
}

/// <summary>Moteur d'édition PDF (QuestPDF) et localisation des documents.</summary>
public class ReportWriterTests
{
    private static ReportTemplate SampleTemplate() => new(
        "Test", "RP_Title", null,
        new[] { new ReportFilterLine("RP_Filter_Period", "01/10/2026 → 31/10/2026") },
        new[]
        {
            new ReportTableTemplate("RP_Table", new[]
            {
                new ReportColumnTemplate("RP_Col_Movements", true),
                new ReportColumnTemplate("@Congo"),
            }, new List<object?[]>
            {
                new object?[] { "12", "7" },
                new object?[] { "0", "0" },
            }, new object?[] { "12", "7" }),
        },
        new[] { new ReportSummaryLine("RP_Sum_RefusalRate", "3,2 %") },
        new DateTime(2026, 10, 16, 9, 30, 0), "jdoe");

    [Fact]
    public void Localize_TranslatesKeysAndKeepsLiterals()
    {
        var report = ReportLocalizer.Localize(SampleTemplate(), key => $"[{key}]", "Organisation", "Pied");

        Assert.Equal("[RP_Title]", report.Title);
        Assert.Equal("[RP_Filter_Period] : 01/10/2026 → 31/10/2026", report.FilterLines[0]);
        Assert.Equal("[RP_Col_Movements]", report.Tables[0].Columns[0].Header);
        Assert.Equal("Congo", report.Tables[0].Columns[1].Header); // littéral « @ »
        Assert.Contains("[RP_Sum_RefusalRate]", report.SummaryLines[0]);
        Assert.Contains("3,2 %", report.SummaryLines[0]);
        Assert.Equal("Organisation", report.Organisation);
    }

    [Fact]
    public void RenderPdf_ProducesValidDocument()
    {
        var report = ReportLocalizer.Localize(SampleTemplate(), key => key);
        var bytes = new QuestPdfReportWriter().RenderPdf(report);

        Assert.True(bytes.Length > 1000, $"PDF de {bytes.Length} octets");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void RenderPdf_EmptyTables_StillProducesDocument()
    {
        var empty = SampleTemplate() with
        {
            Tables = new[] { new ReportTableTemplate(null,
                new[] { new ReportColumnTemplate("RP_Col_Movements") }, Array.Empty<object?[]>()) },
            Summary = Array.Empty<ReportSummaryLine>(),
        };
        var report = ReportLocalizer.Localize(empty, key => key);

        var bytes = new QuestPdfReportWriter().RenderPdf(report);
        Assert.True(bytes.Length > 1000);
    }
}
