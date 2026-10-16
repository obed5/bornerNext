namespace BorderNext.Application.Reporting;

/// <summary>Définition d'une édition du catalogue (identifiant stable, groupe,
/// champs de paramètre exploités, source de données).</summary>
public sealed record ReportDefinition(
    string Id,
    string GroupKey,
    string NameKey,
    ReportFields Fields,
    string? HeritageRef = null);

/// <summary>
/// Catalogue des éditions, reconstruit depuis le formulaire d'édition du poste
/// (état de l'écran : rapport, période, poste frontière, filtres personnes/documents)
/// et ses procédures de données (GetReports*, GetReport*, GetDailyReports*).
/// Groupes : impressions individuelles, mouvements, secondes inspections,
/// nationalités, profils, transport, échanges et santé.
/// </summary>
public static class ReportCatalog
{
    // Groupes.
    public const string GroupDocuments = "RP_Group_Documents";
    public const string GroupMovements = "RP_Group_Movements";
    public const string GroupInspections = "RP_Group_Inspections";
    public const string GroupNationalities = "RP_Group_Nationalities";
    public const string GroupProfiles = "RP_Group_Profiles";
    public const string GroupTransport = "RP_Group_Transport";
    public const string GroupExchange = "RP_Group_Exchange";
    public const string GroupHealth = "RP_Group_Health";

    // Identifiants d'édition.
    public const string RegistrationForm = "Docs_Registration";
    public const string VisaForm = "Docs_Visa";
    public const string PermitForm = "Docs_Permit";
    public const string RefusalForm = "Docs_Refusal";
    public const string PenaltyForm = "Docs_Penalty";
    public const string RemovalOrderForm = "Docs_RemovalOrder";
    public const string MedicalHistoryForm = "Docs_MedicalHistory";
    public const string BlankQuestionnaire = "Docs_HealthBlank";

    public const string MovementSummary = "Mov_Summary";
    public const string MovementsByBorderPoint = "Mov_ByBorderPoint";
    public const string MovementsDaily = "Mov_Daily";
    public const string MovementsMonthly = "Mov_Monthly";
    public const string MovementRegister = "Mov_Register";
    public const string MovementChildren = "Mov_Children";
    public const string MovementPassengers = "Mov_Passengers";

    public const string DoubtfulList = "Doubt_List";
    public const string DoubtfulByNationality = "Doubt_ByNationality";
    public const string DoubtfulByReason = "Doubt_ByReason";

    public const string NationalitiesSummary = "Nat_Summary";
    public const string NationalitiesByDirection = "Nat_ByDirection";
    public const string NationalitiesByDocumentType = "Nat_ByDocumentType";
    public const string NationalitiesTrends = "Nat_Trends";
    public const string NationalitiesByTravelReason = "Nat_ByTravelReason";
    public const string NationalitiesMapData = "Nat_Map";

    public const string TravelReasons = "Prof_TravelReasons";
    public const string DocumentTypes = "Prof_DocumentTypes";
    public const string CountriesOfResidence = "Prof_Residence";
    public const string ForeignersSummary = "Prof_Foreigners";
    public const string ForeignersByAge = "Prof_ForeignersAge";
    public const string ForeignersByMaritalStatus = "Prof_ForeignersMaritalStatus";
    public const string ForeignersByOccupation = "Prof_ForeignersOccupation";
    public const string ForeignersByProvince = "Prof_ForeignersProvince";
    public const string ForeignersByQualification = "Prof_ForeignersQualification";
    public const string ForeignersMedical = "Prof_ForeignersMedical";
    public const string ForeignersSecurity = "Prof_ForeignersSecurity";

    public const string VisaStatistics = "Visa_ByIssueDate";
    public const string StopListPersons = "StopLists_Persons";
    public const string StopListDocuments = "StopLists_Documents";

    public const string StatisticsByTransport = "Mov_ByTransport";
    public const string StatisticsVessels = "Mov_Vessels";

    public const string ApisByStatus = "Exc_ApisByStatus";
    public const string QuestionnaireStatistics = "Health_Statistics";
    public const string QuestionnaireAnswers = "Health_Answers";

    /// <summary>Éditions disponibles, dans l'ordre des écrans.</summary>
    public static IReadOnlyList<ReportDefinition> All { get; } = new ReportDefinition[]
    {
        // ---- Impressions individuelles (fiches du formulaire d'édition) ----
        new(RegistrationForm, GroupDocuments, "RP_Report_Registration",
            ReportFields.Person, "PassportRegistrationData"),
        new(VisaForm, GroupDocuments, "RP_Report_Visa", ReportFields.Person, "Visa"),
        new(PermitForm, GroupDocuments, "RP_Report_Permit", ReportFields.Person, "Permit"),
        new(RefusalForm, GroupDocuments, "RP_Report_Refusal", ReportFields.Person, "Refusal"),
        new(PenaltyForm, GroupDocuments, "RP_Report_Penalty", ReportFields.Person, "Penalty"),
        new(RemovalOrderForm, GroupDocuments, "RP_Report_RemovalOrder", ReportFields.Person, "RemovalOrder"),
        new(MedicalHistoryForm, GroupDocuments, "RP_Report_MedicalHistory", ReportFields.Person, "PermitMedicalHistory"),
        new(BlankQuestionnaire, GroupDocuments, "RP_Report_HealthBlank", ReportFields.None, "Questionnaires"),

        // ---- Mouvements (tableaux 1.x) ----
        new(MovementSummary, GroupMovements, "RP_Report_MovementSummary", ReportFields.Period | ReportFields.BorderPoint | ReportFields.Direction, "1.1"),
        new(MovementsByBorderPoint, GroupMovements, "RP_Report_ByBorderPoint", ReportFields.Period, "1.2"),
        new(MovementsDaily, GroupMovements, "RP_Report_Daily", ReportFields.Period | ReportFields.BorderPoint, "1.3"),
        new(MovementsMonthly, GroupMovements, "RP_Report_Monthly", ReportFields.Period | ReportFields.BorderPoint, "1.4"),
        new(MovementRegister, GroupMovements, "RP_Report_Register",
            ReportFields.Period | ReportFields.Direction | ReportFields.Country | ReportFields.DocumentType
            | ReportFields.Flight | ReportFields.Inspector | ReportFields.Person | ReportFields.Children,
            "GetReportsPassportRegistrationDataByCondition"),
        new(MovementChildren, GroupMovements, "RP_Report_Children", ReportFields.Period | ReportFields.BorderPoint, "GetReportChildren"),
        new(MovementPassengers, GroupMovements, "RP_Report_Passengers", ReportFields.Period | ReportFields.BorderPoint | ReportFields.Flight,
            "GetReportsPassengersStatistics"),

        // ---- Secondes inspections (tableaux 2.x) ----
        new(DoubtfulList, GroupInspections, "RP_Report_DoubtfulList", ReportFields.Period | ReportFields.BorderPoint, "2.1"),
        new(DoubtfulByNationality, GroupInspections, "RP_Report_DoubtfulNationality", ReportFields.Period, "2.2"),
        new(DoubtfulByReason, GroupInspections, "RP_Report_DoubtfulReason", ReportFields.Period, "2.3"),

        // ---- Nationalités (tableaux 3.x) ----
        new(NationalitiesSummary, GroupNationalities, "RP_Report_Nationalities", ReportFields.Period | ReportFields.BorderPoint | ReportFields.Direction, "GetReportsStatisticsbyNationalities"),
        new(NationalitiesByDirection, GroupNationalities, "RP_Report_NationalitiesDirection", ReportFields.Period, "3.2"),
        new(NationalitiesByDocumentType, GroupNationalities, "RP_Report_NationalitiesDocument", ReportFields.Period, "3.3"),
        new(NationalitiesTrends, GroupNationalities, "RP_Report_NationalitiesTrends", ReportFields.Period, "3.4"),
        new(NationalitiesByTravelReason, GroupNationalities, "RP_Report_NationalitiesReason", ReportFields.Period, "3.5"),
        new(NationalitiesMapData, GroupNationalities, "RP_Report_NationalitiesMap", ReportFields.Period | ReportFields.Direction, "3.1"),

        // ---- Profils (tableaux 4.x et enregistrements d'étrangers) ----
        new(TravelReasons, GroupProfiles, "RP_Report_TravelReasons", ReportFields.Period | ReportFields.BorderPoint, "4.1"),
        new(DocumentTypes, GroupProfiles, "RP_Report_DocumentTypes", ReportFields.Period | ReportFields.BorderPoint, "4.4"),
        new(CountriesOfResidence, GroupProfiles, "RP_Report_Residence", ReportFields.Period | ReportFields.BorderPoint, "4.5"),
        new(ForeignersSummary, GroupProfiles, "RP_Report_Foreigners", ReportFields.Period | ReportFields.PermitType, "GetReportsForeignerRegistration*"),
        new(ForeignersByAge, GroupProfiles, "RP_Report_ForeignersAge", ReportFields.Period | ReportFields.PermitType, "…AgeBreakdown"),
        new(ForeignersByMaritalStatus, GroupProfiles, "RP_Report_ForeignersMarital", ReportFields.Period | ReportFields.PermitType, "…MaritalStatusAgeBreakdown"),
        new(ForeignersByOccupation, GroupProfiles, "RP_Report_ForeignersOccupation", ReportFields.Period | ReportFields.PermitType, "…OccupationAgeBreakdown"),
        new(ForeignersByProvince, GroupProfiles, "RP_Report_ForeignersProvince", ReportFields.Period | ReportFields.PermitType, "…ProvinceAgeBreakdown"),
        new(ForeignersByQualification, GroupProfiles, "RP_Report_ForeignersQualification", ReportFields.Period | ReportFields.PermitType, "…QualificationAgeBreakdown"),
        new(ForeignersMedical, GroupProfiles, "RP_Report_ForeignersMedical", ReportFields.Period, "…Medical"),
        new(ForeignersSecurity, GroupProfiles, "RP_Report_ForeignersSecurity", ReportFields.Period, "…Security"),

        // ---- Visas et listes d'alerte ----
        new(VisaStatistics, GroupProfiles, "RP_Report_VisaStatistics", ReportFields.Period | ReportFields.BorderPoint | ReportFields.VisaType,
            "GetVisaReportAdvancedStatisticsbyIssueDate"),
        new(StopListPersons, GroupProfiles, "RP_Report_StopPersons",
            ReportFields.Person | ReportFields.Country, "GetReportPersonsStopListByCondition"),
        new(StopListDocuments, GroupProfiles, "RP_Report_StopDocuments",
            ReportFields.Person | ReportFields.Country | ReportFields.DocumentType, "GetReportDocumentsStopListByCondition"),

        // ---- Transport (passagers, véhicules) ----
        new(StatisticsByTransport, GroupTransport, "RP_Report_ByTransport", ReportFields.Period | ReportFields.BorderPoint, "GetReportsStatisticsByTransport"),
        new(StatisticsVessels, GroupTransport, "RP_Report_Vessels", ReportFields.Period, "GetReportsVesselsStatistics"),

        // ---- Échanges et santé ----
        new(ApisByStatus, GroupExchange, "RP_Report_Apis", ReportFields.Period | ReportFields.BorderPoint, "APIMessage"),
        new(QuestionnaireStatistics, GroupHealth, "RP_Report_Questionnaires", ReportFields.Period, "GetReportsQuestionnaireStatistics"),
        new(QuestionnaireAnswers, GroupHealth, "RP_Report_QuestionnaireAnswers", ReportFields.Period, "GetReportsQuestionnaireStatistics"),
    };

    public static ReportDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    /// <summary>Groupes dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<string> Groups { get; } = new[]
    {
        GroupDocuments, GroupMovements, GroupInspections, GroupNationalities,
        GroupProfiles, GroupTransport, GroupExchange, GroupHealth,
    };
}
