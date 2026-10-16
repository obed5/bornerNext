using System.Reflection;
using System.Text.Json;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Infrastructure.Persistence.Seeding;

/// <summary>
/// Semis du Lot 8 : nomenclature des types de questions, fiche sanitaire du
/// voyageur (contenu de référence BorderNext — les tables héritées étaient vides)
/// et coordonnées cartographiques des pays (héritage :
/// rpt.DashboardCountryGPSCoordinates, métadonnées uniquement). Idempotent.
/// </summary>
public static class HealthDataSeeder
{
    private const string GeoPointsResource =
        "BorderNext.Infrastructure.Persistence.Seeding.country-geo-points.json";

    public static async Task SeedAsync(BorderNextDbContext db, CancellationToken ct = default)
    {
        await EnsureQuestionTypesAsync(db, ct);
        await EnsureCountryGeoPointsAsync(db, ct);
        await EnsureHealthQuestionnaireAsync(db, ct);
    }

    /// <summary>Nomenclature QUESTION_TYPE (héritage : QuestionTypes — 5 types).</summary>
    private static async Task EnsureQuestionTypesAsync(BorderNextDbContext db, CancellationToken ct)
    {
        if (await db.LookupTypes.AnyAsync(t => t.Code == "QUESTION_TYPE", ct)) return;

        db.LookupTypes.Add(new LookupType
        {
            Code = "QUESTION_TYPE",
            NameFr = "Types de questions",
            NameEn = "Question types",
            SortOrder = 1000,
            Items =
            {
                Item(QuestionTypeCodes.YesNo, "Oui/Non", "Yes/No"),
                Item(QuestionTypeCodes.MultipleChoice, "Choix multiple", "Multiple choice"),
                Item(QuestionTypeCodes.Text, "Texte", "Text"),
                Item(QuestionTypeCodes.Date, "Date", "Date"),
                Item(QuestionTypeCodes.Numerical, "Numérique", "Numerical"),
            },
        });
        await db.SaveChangesAsync(ct);
    }

    private static Lookup Item(string code, string fr, string en) => new()
    {
        Code = code,
        Cultures =
        {
            new LookupCulture { Culture = "fr", Description = fr },
            new LookupCulture { Culture = "en", Description = en },
        },
    };

    /// <summary>Coordonnées des pays pour la carte des nationalités.</summary>
    private static async Task EnsureCountryGeoPointsAsync(BorderNextDbContext db, CancellationToken ct)
    {
        if (await db.CountryGeoPoints.AnyAsync(ct)) return;

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GeoPointsResource)
            ?? throw new InvalidOperationException($"Ressource embarquée introuvable : {GeoPointsResource}");
        var file = await JsonSerializer.DeserializeAsync<GeoPointFile>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct)
            ?? throw new InvalidOperationException("Coordonnées cartographiques illisibles.");

        foreach (var point in file.Points)
        {
            // Identifiant laissé à la base (colonne d'identité).
            db.CountryGeoPoints.Add(new CountryGeoPoint
            {
                Iso2 = point.Iso2,
                Iso3 = point.Iso3,
                Name = point.Name,
                Latitude = point.Lat,
                Longitude = point.Lon,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Fiche sanitaire du voyageur : questions principales, questions de suivi
    /// conditionnelles (précisions) et messages de fin (libre pratique, orientation
    /// médicale, surveillance sanitaire). Le message de fin appliqué est la
    /// première règle satisfaite dans l'ordre des questions.
    /// </summary>
    private static async Task EnsureHealthQuestionnaireAsync(BorderNextDbContext db, CancellationToken ct)
    {
        if (await db.Questionnaires.AnyAsync(ct)) return;

        var types = await db.Lookups
            .Where(l => l.Type!.Code == "QUESTION_TYPE" && l.Code != null)
            .ToDictionaryAsync(l => l.Code!, l => l.Id, ct);

        var questionnaire = new Questionnaire
        {
            Description = "Fiche sanitaire du voyageur",
            ExpireDays = 30,
            UpdateDate = DateTime.Now,
            UpdateUser = "seed",
        };
        db.Questionnaires.Add(questionnaire);

        var order = 0;
        var yesNo = new[] { ("Oui", true), ("Non", false) };

        // Q1 — provenance d'une zone d'endémie (principale).
        AddQuestion(db, questionnaire, ++order, types[QuestionTypeCodes.YesNo],
            "Voyageur en provenance d'une zone d'endémie (fièvre jaune, choléra, rougeole…)",
            HealthQuestionTags.FromEndemicArea, isInitial: true, yesNo);

        // Q2 — vaccination fièvre jaune (principale) ; Q3 date conditionnée par « Oui ».
        var vaccinated = AddQuestion(db, questionnaire, ++order, types[QuestionTypeCodes.YesNo],
            "Vaccination contre la fièvre jaune", HealthQuestionTags.YellowFeverVaccinated,
            isInitial: true, yesNo);
        var vaccineDate = AddQuestion(db, questionnaire, ++order, types[QuestionTypeCodes.Date],
            "Date de la vaccination (fièvre jaune)", HealthQuestionTags.YellowFeverDate,
            isInitial: false, placeholder: true);
        db.ConditionalQuestions.Add(new ConditionalQuestion
        {
            QuestionAnswer = vaccinated.Yes,
            Question = vaccineDate.Question,
        });

        // Q4 — symptômes (principale, choix multiple) ; Q5 précisions conditionnée.
        var symptoms = AddQuestion(db, questionnaire, ++order, types[QuestionTypeCodes.MultipleChoice],
            "Symptômes actuels", HealthQuestionTags.Symptoms, isInitial: true,
            items: new[] { "Aucun", "Fièvre", "Toux", "Éruption cutanée", "Autres" });
        var symptomDetail = AddQuestion(db, questionnaire, ++order, types[QuestionTypeCodes.Text],
            "Précisions sur les symptômes", HealthQuestionTags.SymptomsDetail,
            isInitial: false, placeholder: true);
        foreach (var trigger in new[] { "Fièvre", "Toux", "Éruption cutanée", "Autres" })
        {
            db.ConditionalQuestions.Add(new ConditionalQuestion
            {
                QuestionAnswer = symptoms.Answers[trigger],
                Question = symptomDetail.Question,
            });
        }

        // Q6 — contact avec une personne malade ; Q7 précisions conditionnées par « Oui ».
        var contact = AddQuestion(db, questionnaire, ++order, types[QuestionTypeCodes.YesNo],
            "Contact avec une personne malade dans les 21 derniers jours",
            HealthQuestionTags.ContactWithSickPerson, isInitial: true, yesNo);
        var contactDetail = AddQuestion(db, questionnaire, ++order, types[QuestionTypeCodes.Text],
            "Précisions sur le contact", HealthQuestionTags.ContactDetail,
            isInitial: false, placeholder: true);
        db.ConditionalQuestions.Add(new ConditionalQuestion
        {
            QuestionAnswer = contact.Yes,
            Question = contactDetail.Question,
        });

        // Messages de fin et règles : première règle satisfaite dans l'ordre des questions.
        var free = AddEnding(db, "LIBRE_PRATIQUE", "Libre pratique",
            "Aucune mesure particulière : le voyageur poursuit son voyage.");
        var medical = AddEnding(db, "ORIENTATION_MEDICALE", "Orientation vers le service médical",
            "Symptômes ou vaccination absente en provenance d'une zone d'endémie : présenter la fiche au service médical du poste.");
        var watch = AddEnding(db, "SURVEILLANCE_SANITAIRE", "Surveillance sanitaire",
            "Contact avec une personne malade : signaler au service médical du poste (suivi 21 jours).");

        LinkEnding(db, vaccinated.No, medical);
        LinkEnding(db, symptoms.Answers["Fièvre"], medical);
        LinkEnding(db, symptoms.Answers["Éruption cutanée"], medical);
        LinkEnding(db, contact.Yes, watch);
        LinkEnding(db, symptoms.Answers["Aucun"], free);

        await db.SaveChangesAsync(ct);
    }

    private sealed record QuestionRef(Question Question, QuestionAnswer Yes, QuestionAnswer No,
        Dictionary<string, QuestionAnswer> Answers);

    /// <summary>Crée une question rattachée au questionnaire (ordre, type, réponses).
    /// Les questions à saisie libre portent une réponse technique unique (le détail
    /// est conservé dans la précision libre du résultat).</summary>
    private static QuestionRef AddQuestion(BorderNextDbContext db, Questionnaire questionnaire, int order,
        int typeId, string title, string tag, bool isInitial, (string Label, bool IsYes)[]? yesNo = null,
        string[]? items = null, bool placeholder = false)
    {
        var question = new Question
        {
            Title = title,
            QuestionTypeId = typeId,
            IsInitialQuestion = isInitial,
            IsFollowUpQuestion = !isInitial,
            QuestionTag = tag,
            UpdateDate = DateTime.Now,
        };
        questionnaire.Questions.Add(new QuestionnaireQuestion
        {
            Question = question,
            QuestionOrder = order,
        });

        var answers = new Dictionary<string, QuestionAnswer>();
        QuestionAnswer? yes = null;
        QuestionAnswer? no = null;

        void AddAnswer(string label, bool? isYes = null)
        {
            var answer = new QuestionAnswer { Answer = label };
            question.Answers.Add(answer);
            answers[label] = answer;
            if (isYes == true) yes = answer;
            if (isYes == false) no = answer;
        }

        if (yesNo is not null)
        {
            foreach (var (label, isYes) in yesNo) AddAnswer(label, isYes);
        }
        else if (items is not null)
        {
            foreach (var item in items) AddAnswer(item);
        }
        else if (placeholder)
        {
            AddAnswer("Saisie libre");
        }

        return new QuestionRef(question, yes!, no!, answers);
    }

    private static void LinkEnding(BorderNextDbContext db, QuestionAnswer answer, QuestionnaireEndingMessage ending)
        => db.QuestionAnswerQuestionnaireEndings.Add(new QuestionAnswerQuestionnaireEnding
        {
            QuestionAnswer = answer,
            EndingMessage = ending,
        });

    private static QuestionnaireEndingMessage AddEnding(BorderNextDbContext db, string name, string title,
        string body)
    {
        var ending = new QuestionnaireEndingMessage { Name = name, Title = title, MessageBody = body };
        db.QuestionnaireEndingMessages.Add(ending);
        return ending;
    }

    private sealed class GeoPointFile
    {
        public List<GeoPoint> Points { get; set; } = new();
    }

    private sealed class GeoPoint
    {
        public string? Iso2 { get; set; }
        public string? Iso3 { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Lat { get; set; }
        public decimal Lon { get; set; }
    }
}
