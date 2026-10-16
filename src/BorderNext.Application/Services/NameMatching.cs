namespace BorderNext.Application.Services;

/// <summary>
/// Correspondances nominales du moteur de listes d'alerte, reconstruites à
/// l'identique depuis les fonctions héritées :
///  - <c>dbo.SplitName</c> (TVF) : nom entier puis première partie puis reste ;
///  - <c>dbo.udf_GetLevenshteinDistance</c> (CLR) : distance d'édition ;
///  - seuils « &lt;3 », « &lt;2 », « année ±5 » des critères.
///
/// La base héritée est en collation <c>SQL_Latin1_General_CP1_CI_AS</c> :
/// les égalités sont donc reproduites <b>insensibles à la casse et sensibles
/// aux accents</b> (comparaisons ordinales après repli de casse), et les
/// appels à la distance abaissent la casse des deux termes comme les
/// <c>LOWER()</c> hérités.
/// </summary>
public static class NameMatching
{
    /// <summary>Seuil des critères « nom proche » : distance strictement inférieure à 3.</summary>
    public const int SurnameFuzzyBound = 3;

    /// <summary>Seuil des critères « deux noms proches » : distance strictement inférieure à 2.</summary>
    public const int NamesFuzzyBound = 2;

    /// <summary>Fenêtre du critère « date de naissance ± 5 ans » : |années| strictement inférieure à 5.</summary>
    public const int BirthYearWindow = 5;

    /// <summary>Égalité nominale insensible à la casse (héritage : collation CI_AS).
    /// Une valeur nulle ne correspond à rien (sémantique SQL NULL = x → inconnu).</summary>
    public static bool NamesEqual(string? left, string? right)
        => left is not null && right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>Distance de Levenshtein (nombre minimal d'insertions/suppressions/substitutions).
    /// Un terme nul donne un résultat nul (comme la fonction CLR héritée).</summary>
    public static int? Levenshtein(string? left, string? right)
    {
        if (left is null || right is null) return null;
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    /// <summary>Distance de Levenshtein casse ignorée (héritage : <c>LOWER()</c> appliqué
    /// aux deux termes avant l'appel CLR).</summary>
    public static int? LevenshteinFolded(string? left, string? right)
        => Levenshtein(left?.ToLowerInvariant(), right?.ToLowerInvariant());

    /// <summary>Découpage hérité <c>dbo.SplitName</c> : nom entier, puis première partie
    /// (avant le premier espace), puis reste (après le premier espace). Chaîne vide ou nulle :
    /// aucune partie. Les parties vides du découpage («  A », « A ») sont conservées comme
    /// dans l'héritage.</summary>
    public static IReadOnlyList<string> SplitName(string? name)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(name)) return parts;

        parts.Add(name);
        var space = name.IndexOf(' ');
        if (space >= 0)
        {
            parts.Add(name[..space]);
            parts.Add(name[(space + 1)..]);
        }
        return parts;
    }

    /// <summary>Parties d'un nom abaissées en casse (héritage : <c>SELECT LOWER(item) FROM SplitName(...)</c>).</summary>
    public static IReadOnlyList<string> SplitNameFolded(string? name)
        => SplitName(name).Select(p => p.ToLowerInvariant()).ToList();

    /// <summary>Vrai si |années de naissance| &lt; 5 (critère hérité « DoB ± 5 ans »).</summary>
    public static bool WithinBirthYearWindow(DateTime? entry, DateTime reference)
        => entry is { } d && Math.Abs(d.Year - reference.Year) < BirthYearWindow;

    /// <summary>Distance sous un seuil strict, nulle si une valeur manque.</summary>
    public static bool DistanceBelow(string? left, string? right, int bound)
        => LevenshteinFolded(left, right) is { } distance && distance < bound;

    /// <summary>Distance sous un seuil inclusif (« &lt;= », critères numéro de document),
    /// avec chaîne nulle assimilée à la chaîne vide (héritage : <c>ISNULL(colonne, '')</c>).</summary>
    public static bool DistanceAtMostFolded(string? left, string? right, int bound)
        => LevenshteinFolded(left ?? string.Empty, right ?? string.Empty) is { } distance && distance <= bound;
}
