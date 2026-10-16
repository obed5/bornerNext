namespace BorderNext.Application.Edi;

/// <summary>
/// Modèle d'un message PAXLST (liste de passagers, IATA/ONU) : détails du vol,
/// partie déclarante, voyageurs (passagers et équipage) et totaux de contrôle.
/// </summary>
public sealed class PaxlstMessage
{
    public PaxlstFlight Flight { get; set; } = new();
    public PaxlstReportingParty? ReportingParty { get; set; }
    public List<PaxlstTraveller> Travellers { get; } = new();
    public List<PaxlstControlTotal> ControlTotals { get; } = new();
    public List<string> FreeTexts { get; } = new();

    /// <summary>Références de message (UNH/UNT) conservées à l'aller-retour.</summary>
    public string? MessageReference { get; set; }

    public IEnumerable<PaxlstTraveller> Passengers => Travellers.Where(t => t.Type != PaxlstTravellerType.Crew);
    public IEnumerable<PaxlstTraveller> Crew => Travellers.Where(t => t.Type == PaxlstTravellerType.Crew);
}

/// <summary>Détails du transport (segment TDT + LOC + DTM).</summary>
public sealed class PaxlstFlight
{
    public string? Id { get; set; }
    public string? CarrierCode { get; set; }
    public string? CarrierName { get; set; }
    public string? DeparturePort { get; set; }
    public string? ArrivalPort { get; set; }
    public DateTime? DepartureDate { get; set; }
    public DateTime? ArrivalDate { get; set; }
    public string? TransportMode { get; set; }

    /// <summary>Port de référence (arrivée si renseignée, sinon départ) — comme la sortie héritée « Port ».</summary>
    public string? Port => ArrivalPort ?? DeparturePort;
}

/// <summary>Partie déclarante (segment NAD+MS).</summary>
public sealed class PaxlstReportingParty
{
    public string? Name { get; set; }
    public string? ContactNumber { get; set; }
}

public enum PaxlstTravellerType
{
    Passenger = 0,
    Crew = 1,
}

/// <summary>Voyageur : passager ou membre d'équipage avec son document de voyage.</summary>
public sealed class PaxlstTraveller
{
    public PaxlstTravellerType Type { get; set; } = PaxlstTravellerType.Passenger;
    public string? Surname { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public DateTime? DateOfBirth { get; set; }

    /// <summary>Sexe : M / F / U (codes EDIFACT 1/2/0 acceptés et normalisés).</summary>
    public string? Sex { get; set; }

    public string? Nationality { get; set; }
    public string? CountryOfResidence { get; set; }
    public string? DocumentType { get; set; } = "P";
    public string? DocumentNumber { get; set; }
    public string? CountryOfIssue { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? BookingReference { get; set; }
}

/// <summary>Total de contrôle (segment CNT) : ex. 41 = passagers, 42 = équipage.</summary>
public sealed record PaxlstControlTotal(string Qualifier, int Value)
{
    public const string Passengers = "41";
    public const string Crew = "42";
}

/// <summary>
/// Correspondance des qualificatifs PAXLST utilisés par le générateur et reconnus
/// par le parseur (héritage : EdifactParser.dll — enveloppes UNB/UNH/UNT/UNZ,
/// segments Passenger/Crew/TravelDocument/ReportingParty/TransportDetails/ControlTotal).
/// </summary>
public static class PaxlstMapping
{
    public const string MessageType = "PAXLST";
    public const string SyntaxVersion = "PAXLST:D:05B:UN:IATA";

    // Qualificatifs NAD.
    public const string NadReportingParty = "MS";
    public const string NadPassenger = "FL";
    public const string NadCrew = "FM";

    // Qualificatifs de lieu (LOC).
    public const string LocDeparturePort = "87";
    public const string LocArrivalPort = "125";
    public const string LocResidence = "174";

    // Qualificatifs de date (DTM).
    public const string DtmBirth = "329";
    public const string DtmBirthLegacy = "174";
    public const string DtmDeparture = "189";
    public const string DtmArrival = "232";
    public const string DtmDocumentExpiry = "36";
    public const string DtmDocumentIssue = "91";
    public const string DtmDocumentIssueLegacy = "182";

    // Qualificatif d'attribut (ATT) : 2 = sexe.
    public const string AttSex = "2";

    // Codes de format de date EDIFACT.
    public const string FormatDateShort = "102"; // AAAAMMJJ
    public const string FormatDateTime = "203";  // AAAAMMJJHHMM

    // Documents : code de type passeport (segment DOC).
    public const string DocumentNameCodePassport = "P";
    public const string DocumentNameCodeOthers = "X";
}
