using System.Text;

namespace BorderNext.Application.Edi;

/// <summary>
/// Parseur EDIFACT (ISO 9735) : enveloppes UNA/UNB/UNH/UNT/UNZ (groupes
/// UNG/UNE acceptés et traversés), découpage des segments en éléments et
/// composants avec gestion du caractère de libération.
/// </summary>
public static class EdifactParser
{
    /// <summary>Analyse un échange complet et contrôle ses enveloppes.</summary>
    /// <exception cref="EdifactParseException">Enveloppe absente ou compteurs UNT/UNZ incohérents.</exception>
    public static EdifactInterchange Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new EdifactParseException("Edi_Error_Empty", null, "Contenu EDIFACT vide.");

        var syntax = EdifactSyntax.Default;
        var hasUna = false;
        if (content.Length >= 9 && content.StartsWith("UNA", StringComparison.Ordinal))
        {
            syntax = EdifactSyntax.FromUna(content[..9]);
            hasUna = true;
        }

        var rawSegments = SplitSegments(content, syntax);

        if (rawSegments.Count == 0 || !rawSegments[0].StartsWith("UNB", StringComparison.Ordinal))
        {
            var firstTag = rawSegments.FirstOrDefault() is { Length: >= 3 } first ? first[..3] : null;
            throw new EdifactParseException("Edi_Error_UnbMissing", firstTag, "Échange EDIFACT sans enveloppe UNB.");
        }

        var segments = rawSegments.Select(raw => ParseSegment(raw, syntax)).ToList();
        var unb = segments[0];
        if (unb.Elements.Count < 5)
            throw new EdifactParseException("Edi_Error_UnbIncomplete", "UNB", "UNB incomplet (expéditeur, destinataire, date, référence requis).");

        var messages = new List<EdifactMessage>();
        EdifactSegment? unz = null;
        var index = 1;
        while (index < segments.Count)
        {
            var segment = segments[index];
            switch (segment.Tag)
            {
                case "UNG":
                case "UNE":
                    // Groupes fonctionnels : traversés sans traitement (un seul niveau).
                    index++;
                    continue;

                case "UNH":
                {
                    var body = new List<EdifactSegment>();
                    index++;
                    EdifactSegment? unt = null;
                    while (index < segments.Count && segments[index].Tag != "UNT")
                    {
                        if (segments[index].Tag == "UNH")
                            throw new EdifactParseException("Edi_Error_UnhWithoutUnt", "UNH", "Message UNH sans segment UNT de fermeture.");
                        body.Add(segments[index]);
                        index++;
                    }

                    if (index >= segments.Count)
                        throw new EdifactParseException("Edi_Error_UntMissing", "UNH", "Message sans segment UNT de fermeture.");

                    unt = segments[index];
                    index++;

                    // Contrôle du compteur UNT : nombre de segments de UNH à UNT inclus.
                    var declared = ParseInt(unt.Value(1));
                    var actual = body.Count + 2;
                    if (declared is not null && declared.Value != actual)
                        throw new EdifactParseException("Edi_Error_UntCountMismatch", "UNT",
                            $"UNT annonce {declared.Value} segment(s), {actual} compté(s).");

                    var reference = unt.Value(2);
                    if (!string.IsNullOrEmpty(reference) && reference != segment.Value(1))
                        throw new EdifactParseException("Edi_Error_UntReferenceMismatch", "UNT",
                            $"Référence UNT « {reference} » différente de la référence UNH « {segment.Value(1)} ».");

                    messages.Add(new EdifactMessage(segment, unt, body));
                    continue;
                }

                case "UNZ":
                    unz = segment;
                    index++;
                    if (index < segments.Count)
                        throw new EdifactParseException("Edi_Error_DataAfterUnz", segments[index].Tag, "Segments après l'enveloppe UNZ.");
                    continue;

                default:
                    throw new EdifactParseException("Edi_Error_SegmentOutsideMessage", segment.Tag,
                        $"Segment « {segment.Tag} » hors enveloppe de message.");
            }
        }

        if (unz is null)
            throw new EdifactParseException("Edi_Error_UnzMissing", "UNB", "Échange sans enveloppe UNZ de fermeture.");

        if (messages.Count == 0)
            throw new EdifactParseException("Edi_Error_NoMessage", "UNB", "Échange sans message UNH/UNT.");

        var declaredCount = ParseInt(unz.Value(1));
        if (declaredCount is not null && declaredCount.Value != messages.Count)
            throw new EdifactParseException("Edi_Error_UnzCountMismatch", "UNZ",
                $"UNZ annonce {declaredCount.Value} message(s), {messages.Count} compté(s).");

        var unzControl = unz.Value(2);
        if (!string.IsNullOrEmpty(unzControl) && unzControl != unb.Value(5))
            throw new EdifactParseException("Edi_Error_UnzReferenceMismatch", "UNZ",
                $"Référence de contrôle UNZ « {unzControl} » différente de l'UNB « {unb.Value(5)} ».");

        return new EdifactInterchange(syntax, hasUna, unb, unz, messages);
    }

    /// <summary>Analyse un échange et exige que tous ses messages soient de type PAXLST.</summary>
    public static IReadOnlyList<EdifactMessage> ParsePaxlstMessages(string content)
    {
        var interchange = Parse(content);
        foreach (var message in interchange.Messages)
        {
            if (!string.Equals(message.Type, "PAXLST", StringComparison.OrdinalIgnoreCase))
                throw new EdifactParseException("Edi_Error_NotPaxlst", "UNH",
                    $"Type de message « {message.Type} » non pris en charge (PAXLST attendu).");
        }

        return interchange.Messages;
    }

    /// <summary>
    /// Découpe le flux en segments bruts (le caractère de libération protège les
    /// séparateurs). Les fins de ligne et tabulations de mise en forme sont ignorées.
    /// </summary>
    private static List<string> SplitSegments(string content, EdifactSyntax syntax)
    {
        var segments = new List<string>();
        var current = new StringBuilder();

        // Un segment UNA éventuel occupe les 9 premiers caractères du flux.
        var i = content.Length >= 9 && content.StartsWith("UNA", StringComparison.Ordinal) ? 9 : 0;

        var release = syntax.ReleaseCharacter;
        var terminator = syntax.SegmentTerminator;

        for (; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '\r' || c == '\n' || c == '\t') continue;

            if (c == release && i + 1 < content.Length)
            {
                // Paire de libération conservée telle quelle ; déliée à l'analyse de l'élément.
                current.Append(c);
                current.Append(content[i + 1]);
                i++;
                continue;
            }

            if (c == terminator)
            {
                if (current.Length > 0)
                {
                    segments.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            segments.Add(current.ToString());

        return segments;
    }

    /// <summary>Découpe un segment brut en étiquette + éléments composites (libérations déliées).</summary>
    internal static EdifactSegment ParseSegment(string raw, EdifactSyntax syntax)
    {
        var parts = SplitRespectingRelease(raw, syntax.DataElementSeparator, syntax.ReleaseCharacter);
        var tag = parts[0].Trim();
        if (tag.Length == 0)
            throw new EdifactParseException("Edi_Error_EmptySegmentTag", null, "Segment sans étiquette.");

        var elements = new List<EdifactComposite>();
        for (var i = 1; i < parts.Count; i++)
        {
            var components = SplitRespectingRelease(parts[i], syntax.ComponentDataSeparator, syntax.ReleaseCharacter)
                .Select(c => c).ToList();
            elements.Add(new EdifactComposite(components));
        }

        return new EdifactSegment(tag, elements);
    }

    private static List<string> SplitRespectingRelease(string value, char separator, char release)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == release && i + 1 < value.Length)
            {
                current.Append(value[i + 1]); // ?x → x (les séparateurs libérés ne coupent pas)
                i++;
                continue;
            }

            if (c == separator)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }

    /// <summary>Séquences de service : séparateurs et caractère de libération protégés par « ? ».
    /// Le séparateur de répétition (espace conventionnel) n'est pas échappé : il n'est pas
    /// interprété à la lecture, comme dans les implémentations PAXLST courantes.</summary>
    internal static string Escape(string value, EdifactSyntax syntax)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c == syntax.ReleaseCharacter || c == syntax.ComponentDataSeparator || c == syntax.DataElementSeparator
                || c == syntax.SegmentTerminator)
                builder.Append(syntax.ReleaseCharacter);
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static int? ParseInt(string value)
        => int.TryParse(value, out var parsed) ? parsed : null;
}
