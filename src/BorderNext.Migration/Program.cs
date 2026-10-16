using BorderNext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Migration;

/// <summary>
/// Outil d'exploitation (aucune interface graphique) : reprise de l'historique du
/// système précédent, jeu de démonstration synthétique, mesure des temps de réponse,
/// purge des fichiers selon la durée de conservation, réinitialisation d'un mot de passe.
/// Les commandes qui écrivent exigent une base cible ; aucune n'exige la base d'origine.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            var options = CommandLine.Parse(args);
            if (options.Command is null || options.HasFlag("help", "h", "?"))
            {
                PrintHelp();
                return options.Command is null ? 1 : 0;
            }

            return options.Command switch
            {
                "migrate" => await MigrateCommand.RunAsync(options),
                "seed-demo" => await DemoDataCommand.RunAsync(options),
                "benchmark" => await BenchmarkCommand.RunAsync(options),
                "purge-files" => await PurgeCommand.RunAsync(options),
                "reset-password" => await ResetPasswordCommand.RunAsync(options),
                "inspect" => await InspectCommand.RunAsync(options),
                _ => Unknown(options.Command),
            };
        }
        catch (Exception ex)
        {
            // Chaîne complète des exceptions : sans cela, une erreur EF reste illisible.
            Console.Error.WriteLine("Échec :");
            for (var current = ex; current is not null; current = current.InnerException)
                Console.Error.WriteLine($"  {current.GetType().Name} : {current.Message}");
            return 2;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Commande inconnue : {command}");
        PrintHelp();
        return 1;
    }

    /// <summary>Ouvre la base cible (migrations appliquées) et retourne le contexte.</summary>
    internal static async Task<BorderNextDbContext> OpenTargetAsync(CommandLine options)
    {
        var connectionString = options.Get("target")
            ?? Environment.GetEnvironmentVariable("BORDERNEXT_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Indiquez la base cible : --target \"Server=…;Database=BorderNext;…\" " +
                "ou la variable d'environnement BORDERNEXT_CONNECTION_STRING.");

        var builder = new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseSqlServer(connectionString, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

        // Diagnostic d'exploitation : journaliser les requêtes et leur durée (--log-sql).
        if (options.HasFlag("log-sql"))
        {
            builder.LogTo(line => Console.WriteLine($"  [sql] {line}"),
                new[] { Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted },
                Microsoft.Extensions.Logging.LogLevel.Information);
        }

        // Délai de commande paramétrable : les reprises et éditions d'historique peuvent
        // dépasser les 30 s par défaut sur une base volumineuse.
        var timeout = options.GetInt("command-timeout", 0);
        var db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                if (timeout > 0) sql.CommandTimeout(timeout);
            })
            .Options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            BorderNext — outil d'exploitation

              migrate          Reprend l'historique du système précédent vers la base cible.
                               --source "…"   chaîne de connexion d'origine (lecture seule, obligatoire)
                               --target "…"   chaîne de connexion cible (ou BORDERNEXT_CONNECTION_STRING)
                               --dry-run      analyse et comptage sans écriture
                               --page 500     taille des lots de lecture
                               --only a,b     sections : lookups, countries, border-points, persons,
                                              movements, visas, permits, watchlists, doubtful
                               --with-photos  reprend aussi les photos et documents scannés
                               --report f.json  rapport détaillé (comptages et rejets)
                               --verbose      journaux détaillés

              seed-demo        Jeu de démonstration synthétique (aucune donnée réelle).
                               --target "…"   base cible
                               --persons 500  --movements 2000  (volumétrie)
                               --seed 2026    graine du générateur (déterminisme)
                               --with-photos  images synthétiques

              benchmark        Mesure les temps de réponse des écrans critiques (< 1 s attendu).
                               --target "…"   base cible
                               --iterations 5

              purge-files      Supprime les fichiers plus anciens que la durée de conservation.
                               --target "…"   base cible (réglage Exchange_MessageRetentionDays)
                               --days 365     force la durée (0 = illimitée)
                               --dry-run      liste sans supprimer

              reset-password   Réinitialise le mot de passe d'un compte (ops).
                               --target "…"   base cible
                               --user admin   compte concerné
                               --password …   mot de passe (sinon aléatoire, affiché une fois)

              inspect          Affiche la base cible (version, comptages) — diagnostic.
                               --target "…"
            """);
    }
}
