namespace BorderNext.Migration;

/// <summary>Analyse d'arguments simple (--clé valeur, --drapeau), sans dépendance externe.</summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public string? Command { get; private init; }

    public static CommandLine Parse(string[] args)
    {
        var result = new CommandLine { Command = args.FirstOrDefault(a => !a.StartsWith('-')) };

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith('-')) continue;

            var key = token.TrimStart('-');
            string? value = null;
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                value = args[i + 1];
                i++;
            }

            result._values[key] = value ?? string.Empty;
        }

        return result;
    }

    public string? Get(string key)
        => _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public bool HasFlag(params string[] keys)
        => keys.Any(key => _values.ContainsKey(key));

    public int GetInt(string key, int fallback)
        => int.TryParse(Get(key), out var value) && value > 0 ? value : fallback;

    public IReadOnlyList<string> GetList(string key)
        => Get(key) is { } raw
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();
}
