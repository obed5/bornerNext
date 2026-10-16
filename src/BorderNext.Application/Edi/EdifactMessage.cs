namespace BorderNext.Application.Edi;

/// <summary>
/// Jeu de séparateurs EDIFACT (ISO 9735). Valeurs par défaut « : + . ? * ' »
/// lorsque l'échange ne commence pas par un segment UNA.
/// </summary>
public sealed class EdifactSyntax
{
    public char ComponentDataSeparator { get; init; } = ':';
    public char DataElementSeparator { get; init; } = '+';
    public char DecimalNotation { get; init; } = '.';
    public char ReleaseCharacter { get; init; } = '?';
    public char RepetitionSeparator { get; init; } = ' ';
    public char SegmentTerminator { get; init; } = '\'';

    public static EdifactSyntax Default => new();

    /// <summary>Lit un segment UNA : « UNA » suivi de 6 caractères de service.</summary>
    public static EdifactSyntax FromUna(string segment)
    {
        if (segment.Length < 9 || !segment.StartsWith("UNA", StringComparison.Ordinal))
            throw new EdifactParseException("Edi_Error_UnaInvalid", "UNA", "Segment UNA attendu (9 caractères).");

        return new EdifactSyntax
        {
            ComponentDataSeparator = segment[3],
            DataElementSeparator = segment[4],
            DecimalNotation = segment[5],
            ReleaseCharacter = segment[6],
            RepetitionSeparator = segment[7],
            SegmentTerminator = segment[8],
        };
    }

    /// <summary>Sérialise le segment UNA correspondant à ce jeu de séparateurs.</summary>
    public string ToUna() =>
        new(new[] { 'U', 'N', 'A', ComponentDataSeparator, DataElementSeparator, DecimalNotation, ReleaseCharacter, RepetitionSeparator, SegmentTerminator });
}

/// <summary>Segment EDIFACT : étiquette (3 caractères) + éléments de données composites.</summary>
public sealed class EdifactSegment
{
    public string Tag { get; }
    public IReadOnlyList<EdifactComposite> Elements { get; }

    public EdifactSegment(string tag, IReadOnlyList<EdifactComposite> elements)
    {
        Tag = tag;
        Elements = elements;
    }

    /// <summary>Premier composant d'un élément (1 = premier élément après l'étiquette).</summary>
    public string Value(int elementIndex) => Component(elementIndex, 1);

    /// <summary>Composant précis d'un élément ; chaîne vide si absent.</summary>
    public string Component(int elementIndex, int componentIndex)
    {
        if (elementIndex < 1 || elementIndex > Elements.Count) return string.Empty;
        var composite = Elements[elementIndex - 1];
        if (componentIndex < 1 || componentIndex > composite.Components.Count) return string.Empty;
        return composite.Components[componentIndex - 1];
    }

    public bool IsEmpty(int elementIndex) => string.IsNullOrEmpty(Value(elementIndex));
}

/// <summary>Élément de données composite : ses composants séparés par « : ».</summary>
public sealed class EdifactComposite
{
    public IReadOnlyList<string> Components { get; }

    public EdifactComposite(IReadOnlyList<string> components) => Components = components;

    public string Value => Components.Count > 0 ? Components[0] : string.Empty;
}

/// <summary>Message EDIFACT (UNH … UNT) d'un échange.</summary>
public sealed class EdifactMessage
{
    public EdifactSegment Unh { get; }
    public EdifactSegment Unt { get; }

    /// <summary>Segments de corps entre UNH et UNT (exclus).</summary>
    public IReadOnlyList<EdifactSegment> Segments { get; }

    public EdifactMessage(EdifactSegment unh, EdifactSegment unt, IReadOnlyList<EdifactSegment> segments)
    {
        Unh = unh;
        Unt = unt;
        Segments = segments;
    }

    /// <summary>Référence du message (UNH élément 1).</summary>
    public string Reference => Unh.Value(1);

    /// <summary>Type de message (UNH élément 2, composant 1) — ex. PAXLST.</summary>
    public string Type => Unh.Component(2, 1);
    /// <summary>Version (composant 2), édition (3), agence de contrôle (4), code d'association (5).</summary>
    public string Version => Unh.Component(2, 2);
    public string Release => Unh.Component(2, 3);
    public string ControllingAgency => Unh.Component(2, 4);
    public string AssociationCode => Unh.Component(2, 5);
}

/// <summary>Échange EDIFACT complet : enveloppe UNB, messages, enveloppe UNZ.</summary>
public sealed class EdifactInterchange
{
    public EdifactSyntax Syntax { get; }
    public bool HasUna { get; }
    public EdifactSegment Unb { get; }
    public EdifactSegment Unz { get; }
    public IReadOnlyList<EdifactMessage> Messages { get; }

    public EdifactInterchange(EdifactSyntax syntax, bool hasUna, EdifactSegment unb, EdifactSegment unz,
        IReadOnlyList<EdifactMessage> messages)
    {
        Syntax = syntax;
        HasUna = hasUna;
        Unb = unb;
        Unz = unz;
        Messages = messages;
    }

    public string Sender => Unb.Component(2, 1);
    public string Recipient => Unb.Component(3, 1);
    public string InterchangeControlReference => Unb.Value(5);
    public string SyntaxIdentifier => Unb.Component(1, 1);
    public string SyntaxVersion => Unb.Component(1, 2);
    public string TestIndicator => Unb.Component(11, 1);

    /// <summary>Date de l'échange (UNB élément 4 : AAMMJJ:HHMM), si lisible.</summary>
    public DateTime? Date => EdifactDate.Parse(Unb.Component(4, 1), Unb.Component(4, 2));
}

/// <summary>Erreur de syntaxe EDIFACT. <see cref="ErrorKey"/> est une clé i18n.</summary>
public sealed class EdifactParseException : Exception
{
    public string ErrorKey { get; }
    public string? SegmentTag { get; }

    public EdifactParseException(string errorKey, string? segmentTag, string detail)
        : base(detail)
    {
        ErrorKey = errorKey;
        SegmentTag = segmentTag;
    }
}

/// <summary>Lecture des formats de dates EDIFACT (AAMMJJ / AAAAMMJJ + HHMM).</summary>
public static class EdifactDate
{
    /// <summary>Date + heure (format 203 : AAAAMMJJHHMM ou AAMMJJHHMM).</summary>
    public static DateTime? Parse(string? value, string? time = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (time is { Length: > 0 } t)
            text += t.Trim();

        return ParseCompact(text);
    }

    /// <summary>Formats : AAAAMMJJHHMM, AAAAMMJJ, AAMMJJHHMM, AAMMJJ.</summary>
    public static DateTime? ParseCompact(string text)
    {
        if (!text.All(char.IsAsciiDigit)) return null;

        int year, month, day, hour = 0, minute = 0;
        switch (text.Length)
        {
            case 12:
                year = int.Parse(text[..4]); month = int.Parse(text.Substring(4, 2)); day = int.Parse(text.Substring(6, 2));
                hour = int.Parse(text.Substring(8, 2)); minute = int.Parse(text.Substring(10, 2));
                break;
            case 8:
                year = int.Parse(text[..4]); month = int.Parse(text.Substring(4, 2)); day = int.Parse(text.Substring(6, 2));
                break;
            case 10:
                year = 2000 + int.Parse(text[..2]); month = int.Parse(text.Substring(2, 2)); day = int.Parse(text.Substring(4, 2));
                hour = int.Parse(text.Substring(6, 2)); minute = int.Parse(text.Substring(8, 2));
                break;
            case 6:
                year = 2000 + int.Parse(text[..2]); month = int.Parse(text.Substring(2, 2)); day = int.Parse(text.Substring(4, 2));
                break;
            default:
                return null;
        }

        // Fenêtre glissante : une année sur deux chiffres au-delà de 60 appartient au siècle précédent.
        if (text.Length is 6 or 10 && year >= 2060) year -= 100;

        try
        {
            return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Format 102 (AAAAMMJJ) pour les dates de naissance / documents.</summary>
    public static string ToFormat102(DateTime date) => date.ToString("yyyyMMdd");

    /// <summary>Format 203 (AAAAMMJJHHMM) pour les horaires de transport.</summary>
    public static string ToFormat203(DateTime date) => date.ToString("yyyyMMddHHmm");

    /// <summary>Format UNB (AAMMJJ) et heure (HHMM).</summary>
    public static (string Date, string Time) ToUnbParts(DateTime date) =>
        (date.ToString("yyMMdd"), date.ToString("HHmm"));
}
