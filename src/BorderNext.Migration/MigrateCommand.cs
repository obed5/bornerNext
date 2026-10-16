using System.Text.Json;
using BorderNext.Migration.Legacy;

namespace BorderNext.Migration;

/// <summary>Commande « migrate » : reprise de l'historique, avec simulation et rapport.</summary>
public static class MigrateCommand
{
    public static async Task<int> RunAsync(CommandLine options)
    {
        var source = options.Get("source")
            ?? throw new InvalidOperationException("La chaîne de connexion d'origine est obligatoire (--source).");
        var dryRun = options.HasFlag("dry-run");

        var sqlSource = new SqlLegacySource(source);
        Console.WriteLine($"Origine : {await sqlSource.DescribeAsync(default)}");
        Console.WriteLine(dryRun
            ? "Mode simulation : aucune écriture ne sera effectuée."
            : "Mode exécution : écriture dans la base cible.");

        var migrationOptions = new MigrationOptions(
            PageSize: options.GetInt("page", 500),
            DryRun: dryRun,
            WithPhotos: options.HasFlag("with-photos"),
            Verbose: options.HasFlag("verbose"))
        {
            Sections = options.GetList("only"),
        };

        await using var target = await Program.OpenTargetAsync(options);

        // Les référentiels cibles (nomenclatures, pays, postes) sont semés d'abord :
        // la reprise les rapproche ensuite par code, en créant ce qui manque côté cible.
        if (!dryRun)
            await BorderNext.Infrastructure.Persistence.Seeding.ReferenceDataSeeder.SeedAsync(target);

        var migrator = new HistoryMigrator(sqlSource, target, migrationOptions,
            verbose => Console.WriteLine(verbose));
        var counters = await migrator.RunAsync();

        Console.WriteLine();
        Console.WriteLine("Comptages de la reprise :");
        foreach (var table in counters.Tables.OrderBy(t => t.Table, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {table}");
            foreach (var (reason, count) in table.RejectReasons.OrderByDescending(r => r.Value))
                Console.WriteLine($"      rejet : {reason} ({count})");
        }

        Console.WriteLine();
        Console.WriteLine(dryRun
            ? $"Simulation terminée : {counters.TotalWritten} ligne(s) seraient reprises, " +
              $"{counters.TotalSkipped} déjà présentes, {counters.TotalRejected} rejetées."
            : $"Reprise terminée : {counters.TotalWritten} ligne(s) reprises, " +
              $"{counters.TotalSkipped} déjà présentes (ignorées), {counters.TotalRejected} rejetées.");

        // Rapport détaillé : comptages et motifs uniquement (aucune donnée personnelle),
        // les identifiants des rejets servent au diagnostic côté poste.
        if (options.Get("report") is { } reportPath)
        {
            var payload = new
            {
                generatedAt = DateTime.Now,
                dryRun,
                totals = new
                {
                    written = counters.TotalWritten,
                    skipped = counters.TotalSkipped,
                    rejected = counters.TotalRejected,
                },
                tables = counters.Tables
                    .OrderBy(t => t.Table, StringComparer.OrdinalIgnoreCase)
                    .Select(t => new
                    {
                        t.Table,
                        t.Read,
                        t.Written,
                        t.Skipped,
                        t.Rejected,
                        reasons = t.RejectReasons,
                        rejectedIds = t.RejectedIds,
                    }),
            };
            await File.WriteAllTextAsync(reportPath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Rapport écrit : {reportPath}");
        }

        return counters.TotalRejected > 0 && counters.TotalWritten == 0 ? 3 : 0;
    }
}
