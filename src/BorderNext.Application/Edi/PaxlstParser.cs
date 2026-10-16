namespace BorderNext.Application.Edi;

/// <summary>
/// Lecture typée des messages PAXLST : vol (TDT/LOC/DTM), partie déclarante
/// (NAD+MS), voyageurs et équipage (NAD+FL / NAD+FM, DTM, ATT, NAT, LOC, DOC, RFF)
/// et totaux de contrôle (CNT).
/// </summary>
public static class PaxlstParser
{
    /// <summary>Analyse un flux EDIFACT et retourne les messages PAXLST typés.</summary>
    public static IReadOnlyList<PaxlstMessage> Parse(string content)
        => EdifactParser.ParsePaxlstMessages(content).Select(ParseMessage).ToList();

    /// <summary>Convertit un message EDIFACT (déjà analysé) en modèle PAXLST.</summary>
    public static PaxlstMessage ParseMessage(EdifactMessage message)
    {
        var result = new PaxlstMessage
        {
            MessageReference = message.Reference,
        };

        PaxlstTraveller? current = null;
        var lastNameElement = false;

        foreach (var segment in message.Segments)
        {
            switch (segment.Tag)
            {
                case "TDT":
                    ParseTransport(segment, result.Flight);
                    break;

                case "LOC":
                    if (segment.Value(1) == PaxlstMapping.LocDeparturePort)
                        result.Flight.DeparturePort = segment.Value(2);
                    else if (segment.Value(1) == PaxlstMapping.LocArrivalPort)
                        result.Flight.ArrivalPort = segment.Value(2);
                    else if (segment.Value(1) == PaxlstMapping.LocResidence && current is not null)
                        current.CountryOfResidence = segment.Value(2);
                    break;

                case "DTM":
                    ApplyDate(segment, result.Flight, current);
                    break;

                case "NAD":
                    ApplyName(segment, result, ref current, ref lastNameElement);
                    break;

                case "ATT":
                    if (segment.Value(1) == PaxlstMapping.AttSex && current is not null)
                        current.Sex = NormalizeSex(ElementOrComponent(segment, 2));
                    break;

                case "NAT":
                    if (current is not null)
                        current.Nationality = ElementOrComponent(segment, 2);
                    break;

                case "DOC":
                    if (current is not null)
                        ApplyDocument(segment, current);
                    break;

                case "RFF":
                    if (current is not null && segment.Component(1, 1) is "ABO" or "AEA" or "TN")
                        current.BookingReference = segment.Component(1, 2);
                    break;

                case "CNT":
                    if (int.TryParse(ElementOrComponent(segment, 2), out var total))
                        result.ControlTotals.Add(new PaxlstControlTotal(segment.Value(1), total));
                    break;

                case "FTX":
                    var text = segment.Value(4);
                    if (!string.IsNullOrWhiteSpace(text)) result.FreeTexts.Add(text);
                    break;

                case "COM":
                    if (lastNameElement && result.ReportingParty is not null)
                        result.ReportingParty.ContactNumber = segment.Value(1);
                    break;
            }
        }

        return result;
    }

    private static void ParseTransport(EdifactSegment segment, PaxlstFlight flight)
    {
        // TDT+<étape de transport>+<n° de vol>+<code transporteur>:<nom du transporteur>
        if (!string.IsNullOrEmpty(segment.Value(1))) flight.TransportMode = segment.Value(1);
        if (!string.IsNullOrEmpty(segment.Value(2))) flight.Id = segment.Value(2);
        if (!string.IsNullOrEmpty(segment.Component(3, 1))) flight.CarrierCode = segment.Component(3, 1);
        if (!string.IsNullOrEmpty(segment.Component(3, 2))) flight.CarrierName = segment.Component(3, 2);
    }

    private static void ApplyDate(EdifactSegment segment, PaxlstFlight flight, PaxlstTraveller? current)
    {
        var qualifier = segment.Value(1);
        // Forme composite standard (DTM+qualificatif:valeur:format) ou éléments séparés.
        var value = ElementOrComponent(segment, 2);
        var parsed = EdifactDate.ParseCompact(value);
        if (parsed is null) return;

        switch (qualifier)
        {
            case PaxlstMapping.DtmDeparture:
                flight.DepartureDate = parsed;
                break;
            case PaxlstMapping.DtmArrival:
                flight.ArrivalDate = parsed;
                break;
            case PaxlstMapping.DtmBirth:
            case PaxlstMapping.DtmBirthLegacy:
                if (current is not null) current.DateOfBirth = parsed;
                break;
            case PaxlstMapping.DtmDocumentExpiry:
                if (current is not null) current.ExpiryDate = parsed;
                break;
            case PaxlstMapping.DtmDocumentIssue:
            case PaxlstMapping.DtmDocumentIssueLegacy:
                if (current is not null) current.IssueDate = parsed;
                break;
        }
    }

    private static void ApplyName(EdifactSegment segment, PaxlstMessage result,
        ref PaxlstTraveller? current, ref bool lastNameElement)
    {
        var qualifier = segment.Value(1);
        switch (qualifier)
        {
            case PaxlstMapping.NadReportingParty:
                current = null;
                lastNameElement = true;
                result.ReportingParty = new PaxlstReportingParty { Name = BuildName(segment, 4) };
                break;

            case PaxlstMapping.NadPassenger:
            case PaxlstMapping.NadCrew:
                lastNameElement = false;
                current = new PaxlstTraveller
                {
                    Type = qualifier == PaxlstMapping.NadCrew ? PaxlstTravellerType.Crew : PaxlstTravellerType.Passenger,
                    Surname = segment.Component(4, 1),
                    FirstName = segment.Component(4, 2),
                    MiddleName = segment.Component(4, 3),
                };
                result.Travellers.Add(current);
                break;

            default:
                lastNameElement = false;
                break;
        }
    }

    private static string? BuildName(EdifactSegment segment, int elementIndex)
    {
        var surname = segment.Component(elementIndex, 1);
        var given = segment.Component(elementIndex, 2);
        if (string.IsNullOrEmpty(surname)) return given;
        if (string.IsNullOrEmpty(given)) return surname;
        return $"{surname} {given}";
    }

    /// <summary>Valeur d'un élément : élément distinct, sinon composant du premier élément
    /// (les deux écritures des segments composites PAXLST sont courantes chez les émetteurs).</summary>
    private static string ElementOrComponent(EdifactSegment segment, int elementIndex)
    {
        var direct = segment.Value(elementIndex);
        return !string.IsNullOrEmpty(direct) ? direct : segment.Component(1, elementIndex);
    }

    private static void ApplyDocument(EdifactSegment segment, PaxlstTraveller traveller)
    {
        // DOC+<code document>+<n°>+<pays de délivrance>:<date de délivrance>:<date d'expiration>
        var code = segment.Value(1);
        traveller.DocumentType = code switch
        {
            PaxlstMapping.DocumentNameCodePassport => "P",
            "" => traveller.DocumentType,
            _ => code,
        };
        if (!string.IsNullOrEmpty(segment.Value(2))) traveller.DocumentNumber = segment.Value(2);
        if (!string.IsNullOrEmpty(segment.Component(3, 1))) traveller.CountryOfIssue = segment.Component(3, 1);

        if (EdifactDate.ParseCompact(segment.Component(3, 2)) is { } issue) traveller.IssueDate = issue;
        if (EdifactDate.ParseCompact(segment.Component(3, 3)) is { } expiry) traveller.ExpiryDate = expiry;
    }

    /// <summary>Codes de sexe : 1/M → M, 2/F → F, sinon U.</summary>
    public static string NormalizeSex(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "M" or "1" or "MALE" => "M",
        "F" or "2" or "FEMALE" => "F",
        _ => "U",
    };
}
