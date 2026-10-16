namespace BorderNext.Migration;

/// <summary>
/// Comptage par table du rapport de migration : lignes lues, écrites, ignorées
/// (déjà présentes) et rejetées avec le motif. Aucune donnée personnelle n'y figure :
/// seuls des identifiants techniques sont conservés, et seulement pour les rejets.
/// </summary>
public sealed class MigrationCounters
{
    private readonly Dictionary<string, TableCounters> _tables = new(StringComparer.OrdinalIgnoreCase);

    public TableCounters For(string table)
    {
        if (!_tables.TryGetValue(table, out var counters))
        {
            counters = new TableCounters(table);
            _tables[table] = counters;
        }
        return counters;
    }

    public IReadOnlyCollection<TableCounters> Tables => _tables.Values;

    public long TotalWritten => _tables.Values.Sum(t => t.Written);
    public long TotalRejected => _tables.Values.Sum(t => t.Rejected);
    public long TotalSkipped => _tables.Values.Sum(t => t.Skipped);
}

/// <summary>Compteurs d'une table (motifs de rejet conservés pour le diagnostic).</summary>
public sealed class TableCounters(string table)
{
    private readonly Dictionary<string, int> _reasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _rejectedIds = new();

    public string Table { get; } = table;
    public long Read { get; private set; }
    public long Written { get; private set; }
    public long Skipped { get; private set; }
    public long Rejected { get; private set; }

    public IReadOnlyDictionary<string, int> RejectReasons => _reasons;

    /// <summary>Identifiants hérités des premiers rejets (diagnostic, plafonné).</summary>
    public IReadOnlyList<string> RejectedIds => _rejectedIds;

    public void AddRead(long count = 1) => Read += count;
    public void AddWritten(long count = 1) => Written += count;
    public void AddSkipped(long count = 1) => Skipped += count;

    public void AddRejected(string legacyId, string reason)
    {
        Rejected++;
        _reasons[reason] = _reasons.TryGetValue(reason, out var current) ? current + 1 : 1;
        if (_rejectedIds.Count < 50) _rejectedIds.Add(legacyId);
    }

    public override string ToString()
        => $"{Table}: lues {Read}, écrites {Written}, ignorées {Skipped}, rejetées {Rejected}";
}

/// <summary>Options d'exécution de la migration.</summary>
public sealed record MigrationOptions(
    int PageSize = 500,
    bool DryRun = false,
    bool WithPhotos = false,
    bool Verbose = false)
{
    /// <summary>Sections à traiter (toutes par défaut).</summary>
    public IReadOnlyCollection<string> Sections { get; init; } = Array.Empty<string>();

    public bool Includes(string section)
        => Sections.Count == 0 || Sections.Contains(section, StringComparer.OrdinalIgnoreCase);
}
