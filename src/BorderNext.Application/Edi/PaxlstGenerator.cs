using System.Text;

namespace BorderNext.Application.Edi;

/// <summary>Options de génération d'un échange PAXLST.</summary>
public sealed class PaxlstGeneratorOptions
{
    public EdifactSyntax Syntax { get; init; } = EdifactSyntax.Default;

    /// <summary>Écrire le segment UNA en tête d'échange (séparateurs explicites).</summary>
    public bool WriteUna { get; init; } = true;

    public string Sender { get; init; } = "BORDERNEXT";
    public string Recipient { get; init; } = "BORDER";
    public string SyntaxIdentifier { get; init; } = "UNOA";
    public string SyntaxVersion { get; init; } = "1";

    /// <summary>Référence de contrôle de l'échange (UNB/UNZ). Générée si absente.</summary>
    public string? ControlReference { get; init; }

    /// <summary>Date de préparation de l'échange (défaut : horloge système).</summary>
    public DateTime? PreparedAt { get; init; }

    /// <summary>Indicateur d'essai (UNB élément 11) : « 1 » pour un échange de test.</summary>
    public string? TestIndicator { get; init; }
}

/// <summary>
/// Génération des messages PAXLST (ONU/EDIFACT, IATA) : enveloppes UNB/UNZ,
/// UNH/UNT avec compteur de segments, segments TransportDetails, ReportingParty,
/// Passenger / Crew, TravelDocument et ControlTotal.
/// </summary>
public static class PaxlstGenerator
{
    /// <summary>Génère un échange contenant un seul message PAXLST.</summary>
    public static string Generate(PaxlstMessage message, PaxlstGeneratorOptions? options = null)
        => Generate(new[] { message }, options);

    /// <summary>Génère un échange contenant plusieurs messages PAXLST (UNZ = nombre de messages).</summary>
    public static string Generate(IReadOnlyList<PaxlstMessage> messages, PaxlstGeneratorOptions? options = null)
    {
        if (messages.Count == 0)
            throw new ArgumentException("Au moins un message PAXLST est requis.", nameof(messages));

        options ??= new PaxlstGeneratorOptions();
        var syntax = options.Syntax;
        var preparedAt = options.PreparedAt ?? DateTime.Now;
        var controlReference = string.IsNullOrWhiteSpace(options.ControlReference)
            ? preparedAt.ToString("yyMMddHHmmss")
            : options.ControlReference!;

        var builder = new StringBuilder();
        if (options.WriteUna) builder.Append(syntax.ToUna());

        // Enveloppe d'échange (UNB) : identifiant de syntaxe et date/heure en éléments composites.
        var (unbDate, unbTime) = EdifactDate.ToUnbParts(preparedAt);
        builder.Append(SegmentRaw(syntax, "UNB",
            Join(new[] { options.SyntaxIdentifier, options.SyntaxVersion }, syntax),
            Push(options.Sender, syntax), Push(options.Recipient, syntax),
            Join(new[] { unbDate, unbTime }, syntax),
            Push(controlReference, syntax),
            null, null, null, null,
            null, Push(options.TestIndicator, syntax)));

        for (var i = 0; i < messages.Count; i++)
            AppendMessage(builder, messages[i], i + 1, controlReference, syntax);

        // Enveloppe de fin (UNZ) : nombre de messages + référence de contrôle.
        builder.Append(Segment(syntax, "UNZ", messages.Count.ToString(), controlReference));
        return builder.ToString();
    }

    private static void AppendMessage(StringBuilder builder, PaxlstMessage message, int index,
        string controlReference, EdifactSyntax syntax)
    {
        var reference = string.IsNullOrWhiteSpace(message.MessageReference)
            ? $"{controlReference}{index}"
            : message.MessageReference!;

        var body = new List<string> { Segment(syntax, "BGM", "745", reference, "9") };
        AppendFlight(body, message.Flight, syntax);
        AppendReportingParty(body, message, syntax);
        foreach (var traveller in message.Travellers)
            AppendTraveller(body, traveller, syntax);

        // Totaux de contrôle : calculés si le message n'en porte pas.
        var totals = message.ControlTotals.Count > 0
            ? message.ControlTotals
            : new List<PaxlstControlTotal>
            {
                new(PaxlstControlTotal.Passengers, message.Passengers.Count()),
                new(PaxlstControlTotal.Crew, message.Crew.Count()),
            };
        foreach (var total in totals)
            body.Add(SegmentRaw(syntax, "CNT", Join(new[] { total.Qualifier, total.Value.ToString() }, syntax)));

        foreach (var text in message.FreeTexts.Where(t => !string.IsNullOrWhiteSpace(t)))
            body.Add(Segment(syntax, "FTX", "AAI", null, null, text));

        builder.Append(SegmentRaw(syntax, "UNH", Push(reference, syntax), PaxlstMapping.SyntaxVersion));
        foreach (var segment in body) builder.Append(segment);

        // Compteur UNT : segments de UNH à UNT inclus.
        builder.Append(Segment(syntax, "UNT", (body.Count + 2).ToString(), reference));
    }

    private static void AppendFlight(List<string> body, PaxlstFlight flight, EdifactSyntax syntax)
    {
        if (string.IsNullOrWhiteSpace(flight.Id) && string.IsNullOrWhiteSpace(flight.CarrierName)) return;

        var stage = string.IsNullOrWhiteSpace(flight.TransportMode) ? "20" : flight.TransportMode;
        var carrier = Join(new[] { flight.CarrierCode, flight.CarrierName }, syntax);
        body.Add(SegmentRaw(syntax, "TDT", Push(stage, syntax), Push(flight.Id, syntax), carrier));

        if (!string.IsNullOrWhiteSpace(flight.DeparturePort))
            body.Add(Segment(syntax, "LOC", PaxlstMapping.LocDeparturePort, flight.DeparturePort));
        if (!string.IsNullOrWhiteSpace(flight.ArrivalPort))
            body.Add(Segment(syntax, "LOC", PaxlstMapping.LocArrivalPort, flight.ArrivalPort));
        if (flight.DepartureDate is { } departure)
            body.Add(DateSegment(syntax, PaxlstMapping.DtmDeparture, EdifactDate.ToFormat203(departure), PaxlstMapping.FormatDateTime));
        if (flight.ArrivalDate is { } arrival)
            body.Add(DateSegment(syntax, PaxlstMapping.DtmArrival, EdifactDate.ToFormat203(arrival), PaxlstMapping.FormatDateTime));
    }

    /// <summary>DTM sous forme composite standard (qualificatif:valeur:format).</summary>
    private static string DateSegment(EdifactSyntax syntax, string qualifier, string value, string format)
        => SegmentRaw(syntax, "DTM", Join(new[] { qualifier, value, format }, syntax));

    private static void AppendReportingParty(List<string> body, PaxlstMessage message, EdifactSyntax syntax)
    {
        if (message.ReportingParty is not { } party || string.IsNullOrWhiteSpace(party.Name)) return;

        var parts = SplitName(party.Name);
        body.Add(SegmentRaw(syntax, "NAD", PaxlstMapping.NadReportingParty, null, null, Join(parts, syntax)));
        if (!string.IsNullOrWhiteSpace(party.ContactNumber))
            body.Add(Segment(syntax, "COM", party.ContactNumber, "TE"));
    }

    private static void AppendTraveller(List<string> body, PaxlstTraveller traveller, EdifactSyntax syntax)
    {
        var qualifier = traveller.Type == PaxlstTravellerType.Crew
            ? PaxlstMapping.NadCrew
            : PaxlstMapping.NadPassenger;
        body.Add(SegmentRaw(syntax, "NAD", qualifier, null, null,
            Join(new[] { traveller.Surname, traveller.FirstName, traveller.MiddleName }, syntax)));

        if (traveller.DateOfBirth is { } birth)
            body.Add(DateSegment(syntax, PaxlstMapping.DtmBirth, EdifactDate.ToFormat102(birth), PaxlstMapping.FormatDateShort));
        if (!string.IsNullOrWhiteSpace(traveller.Sex))
            body.Add(Segment(syntax, "ATT", PaxlstMapping.AttSex, PaxlstParser.NormalizeSex(traveller.Sex)));
        if (!string.IsNullOrWhiteSpace(traveller.Nationality))
            body.Add(SegmentRaw(syntax, "NAT", Join(new[] { "2", traveller.Nationality }, syntax)));
        if (!string.IsNullOrWhiteSpace(traveller.CountryOfResidence))
            body.Add(Segment(syntax, "LOC", PaxlstMapping.LocResidence, traveller.CountryOfResidence));

        if (!string.IsNullOrWhiteSpace(traveller.DocumentNumber))
        {
            var documentCode = string.IsNullOrWhiteSpace(traveller.DocumentType)
                ? PaxlstMapping.DocumentNameCodePassport
                : traveller.DocumentType!;
            var composite = Join(new[]
            {
                traveller.CountryOfIssue,
                traveller.IssueDate is { } issue ? EdifactDate.ToFormat102(issue) : null,
                traveller.ExpiryDate is { } expiry ? EdifactDate.ToFormat102(expiry) : null,
            }, syntax);
            body.Add(SegmentRaw(syntax, "DOC", Push(documentCode, syntax), Push(traveller.DocumentNumber, syntax), composite));
        }

        if (!string.IsNullOrWhiteSpace(traveller.BookingReference))
            body.Add(SegmentRaw(syntax, "RFF", Join(new[] { "ABO", traveller.BookingReference }, syntax)));
    }

    private static string[] SplitName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => new[] { name },
            1 => new[] { parts[0] },
            _ => new[] { parts[0], string.Join(' ', parts.Skip(1)) },
        };
    }

    /// <summary>Compose un élément composite : composants échappés puis joints par « : ».</summary>
    private static string Join(IEnumerable<string?> components, EdifactSyntax syntax) =>
        string.Join(syntax.ComponentDataSeparator,
            components.Select(c => c is null ? string.Empty : EdifactParser.Escape(c, syntax)));

    /// <summary>Échappe une valeur simple destinée à un segment à éléments déjà composés.</summary>
    private static string? Push(string? value, EdifactSyntax syntax)
        => value is null ? null : EdifactParser.Escape(value, syntax);

    /// <summary>Assemble un segment dont chaque élément est une valeur déjà échappée ou un
    /// composite déjà composé. Les éléments vides de fin de segment sont omis.</summary>
    private static string SegmentRaw(EdifactSyntax syntax, string tag, params string?[] elements)
    {
        var last = elements.Length - 1;
        while (last >= 0 && elements[last] is null) last--;

        var builder = new StringBuilder(tag);
        for (var i = 0; i <= last; i++)
        {
            builder.Append(syntax.DataElementSeparator);
            if (elements[i] is { } element) builder.Append(element);
        }

        builder.Append(syntax.SegmentTerminator);
        return builder.ToString();
    }

    /// <summary>Assemble un segment de valeurs simples (échappées une à une).</summary>
    private static string Segment(EdifactSyntax syntax, string tag, params string?[] elements)
        => SegmentRaw(syntax, tag, elements.Select(e => Push(e, syntax)).ToArray());
}
