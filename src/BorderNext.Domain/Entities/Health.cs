namespace BorderNext.Domain.Entities;

/// <summary>
/// Questionnaire (héritage : Questionnaires) — ex. fiche sanitaire du voyageur.
/// Chaque questionnaire regroupe des questions ordonnées ; une application au
/// dossier d'un voyageur a une durée de validité (ExpireDays) et des résultats.
/// </summary>
public class Questionnaire
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Durée de validité de l'application, en jours (héritage : ExpireDays).</summary>
    public int ExpireDays { get; set; }

    public bool Revoke { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    public ICollection<QuestionnaireQuestion> Questions { get; set; } = new List<QuestionnaireQuestion>();
    public ICollection<AppliedQuestionnaire> Applications { get; set; } = new List<AppliedQuestionnaire>();
}

/// <summary>Question d'un questionnaire (héritage : Questions). Le type
/// (oui/non, choix multiple, texte, date, numérique) est la nomenclature
/// QUESTION_TYPE ; les questions de suivi sont déclenchées par des réponses
/// (héritage : ConditionalQuestions).</summary>
public class Question
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Type de question (nomenclature QUESTION_TYPE).</summary>
    public int QuestionTypeId { get; set; }
    public Lookup? QuestionType { get; set; }

    /// <summary>Question principale, posée d'office (héritage : isInitialQuestion).</summary>
    public bool IsInitialQuestion { get; set; }

    /// <summary>Question de suivi, posée selon les réponses (héritage : isFollowUpQuestion).</summary>
    public bool IsFollowUpQuestion { get; set; }

    /// <summary>Étiquette technique (héritage : QuestionTag) — ex. YELLOW_FEVER.</summary>
    public string? QuestionTag { get; set; }

    public bool Revoke { get; set; }
    public DateTime? UpdateDate { get; set; }

    public ICollection<QuestionAnswer> Answers { get; set; } = new List<QuestionAnswer>();
}

/// <summary>Réponse possible à une question (héritage : QuestionAnswers).</summary>
public class QuestionAnswer
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public string Answer { get; set; } = string.Empty;

    public bool Revoke { get; set; }
}

/// <summary>Rattachement ordonné d'une question à un questionnaire
/// (héritage : QuestionnaireQuestions).</summary>
public class QuestionnaireQuestion
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public int QuestionnaireId { get; set; }
    public Questionnaire? Questionnaire { get; set; }

    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public int QuestionOrder { get; set; }
    public bool Revoke { get; set; }
}

/// <summary>Question de suivi déclenchée par une réponse donnée
/// (héritage : ConditionalQuestions).</summary>
public class ConditionalQuestion
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    /// <summary>Réponse déclenchante.</summary>
    public int QuestionAnswerId { get; set; }
    public QuestionAnswer? QuestionAnswer { get; set; }

    /// <summary>Question de suivi posée lorsque cette réponse est choisie.</summary>
    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public bool Revoke { get; set; }
}

/// <summary>
/// Application d'un questionnaire au dossier d'un voyageur (héritage :
/// AppliedQuestionnaires — document de voyage et/ou enregistrement de passage).
/// </summary>
public class AppliedQuestionnaire
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public int QuestionnaireId { get; set; }
    public Questionnaire? Questionnaire { get; set; }

    public int? DocumentId { get; set; }
    public TravelDocument? Document { get; set; }

    public int? MovementId { get; set; }
    public Movement? Movement { get; set; }

    public DateTime AppliedDate { get; set; }

    /// <summary>Fin de validité, calculée à l'application (AppliedDate + ExpireDays).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Poste frontière d'application (ajout du schéma cible).</summary>
    public short? BorderPointId { get; set; }

    public string? AppliedByUserId { get; set; }

    public ICollection<QuestionnaireResult> Results { get; set; } = new List<QuestionnaireResult>();

    /// <summary>Vrai si la validité est dépassée à la date donnée.</summary>
    public bool IsExpired(DateTime now) => ExpiresAt is { } expiry && expiry < now;
}

/// <summary>Réponse donnée à une question lors d'une application
/// (héritage : QuestionnaireResults — réponse de la liste ou précision libre).</summary>
public class QuestionnaireResult
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public int AppliedQuestionnaireId { get; set; }
    public AppliedQuestionnaire? AppliedQuestionnaire { get; set; }

    public int QuestionAnswerId { get; set; }
    public QuestionAnswer? QuestionAnswer { get; set; }

    /// <summary>Précision libre « autre » (héritage : OtherAnswer).</summary>
    public string? OtherAnswer { get; set; }

    /// <summary>Horodatage de la réponse (ajout du schéma cible, utile aux statistiques).</summary>
    public DateTime? AnsweredAt { get; set; }
}

/// <summary>Message de fin d'un questionnaire (héritage :
/// QuestionnaireEndingMessages — ex. libre pratique, orientation médicale).</summary>
public class QuestionnaireEndingMessage
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    /// <summary>Nom technique (héritage : Name).</summary>
    public string? Name { get; set; }

    public string? Title { get; set; }
    public string? MessageBody { get; set; }

    public bool Revoke { get; set; }
}

/// <summary>Règle de fin : une réponse donnée déclenche un message de fin
/// (héritage : QuestionAnswerQuestionnaireEndings).</summary>
public class QuestionAnswerQuestionnaireEnding
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public int QuestionAnswerId { get; set; }
    public QuestionAnswer? QuestionAnswer { get; set; }

    public int QuestionnaireEndingMessageId { get; set; }
    public QuestionnaireEndingMessage? EndingMessage { get; set; }
}

/// <summary>Codes de la nomenclature QUESTION_TYPE (héritage : QuestionTypes).</summary>
public static class QuestionTypeCodes
{
    public const string YesNo = "YesNo";
    public const string MultipleChoice = "MultipleChoice";
    public const string Text = "Text";
    public const string Date = "Date";
    public const string Numerical = "Numerical";
}

/// <summary>Codes des questions de la fiche sanitaire du voyageur (contenu de
/// référence BorderNext — les tables héritées étaient vides).</summary>
public static class HealthQuestionTags
{
    public const string FromEndemicArea = "ENDEMIC_AREA";
    public const string YellowFeverVaccinated = "YELLOW_FEVER_VACCINATED";
    public const string YellowFeverDate = "YELLOW_FEVER_DATE";
    public const string Symptoms = "SYMPTOMS";
    public const string SymptomsDetail = "SYMPTOMS_DETAIL";
    public const string ContactWithSickPerson = "CONTACT_SICK";
    public const string ContactDetail = "CONTACT_DETAIL";
}
