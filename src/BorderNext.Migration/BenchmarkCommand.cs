using System.Diagnostics;
using BorderNext.Application.Reporting;
using BorderNext.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BorderNext.Migration;

/// <summary>
/// Mesure des temps de réponse des écrans critiques sur la volumétrie en place
/// (objectif : ouverture d'un écran de saisie sous la seconde). Les mesures portent
/// sur les services qui alimentent les écrans : c'est là que passe l'essentiel du temps.
/// </summary>
public static class BenchmarkCommand
{
    private const int TargetMilliseconds = 1000;

    public static async Task<int> RunAsync(CommandLine options)
    {
        var iterations = options.GetInt("iterations", 5);
        await using var db = await Program.OpenTargetAsync(options);

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<Microsoft.EntityFrameworkCore.DbContext>(db);
        services.AddSingleton<BorderNext.Application.Abstractions.ICurrentUser, BorderNext.Infrastructure.Services.CurrentUser>();
        services.AddSingleton<BorderNext.Application.Abstractions.IClock, BorderNext.Infrastructure.Services.SystemClock>();
        services.AddSingleton<BorderNext.Application.Abstractions.IAuditWriter, BorderNext.Infrastructure.Services.AuditWriter>();
        services.AddScoped<DocumentService>();
        services.AddScoped<MovementService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ReportService>();
        services.AddScoped<WatchlistService>();
        services.AddScoped<StopListMatcher>();
        var provider = services.BuildServiceProvider();

        var currentUser = provider.GetRequiredService<BorderNext.Application.Abstractions.ICurrentUser>();
        currentUser.Set(new Domain.Entities.User
        {
            Id = "ADM001",
            UserName = "admin",
            UserRoleId = 1,
            BorderPointId = await db.BorderPoints.OrderBy(p => p.Id).Select(p => (short?)p.Id).FirstOrDefaultAsync(),
        }, "ADMIN");

        var documentNumber = await db.TravelDocuments.OrderByDescending(d => d.Id)
            .Select(d => d.DocumentNumber).FirstOrDefaultAsync() ?? "OP000001";

        Console.WriteLine($"Volumétrie : {await db.Persons.CountAsync()} personne(s), "
                          + $"{await db.TravelDocuments.CountAsync()} document(s), "
                          + $"{await db.Movements.CountAsync()} mouvement(s), "
                          + $"{await db.PersonsStopList.CountAsync()} liste(s) de surveillance");
        Console.WriteLine();
        Console.WriteLine($"{"Mesure",-46} {"moyenne",8} {"max",8}   verdict");

        var failures = 0;
        failures += await MeasureAsync("Recherche de dossier (numéro de document)", iterations, async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var documents = scope.ServiceProvider.GetRequiredService<DocumentService>();
            await documents.SearchAsync(documentNumber);
        });

        var latestDocumentId = await db.TravelDocuments.OrderByDescending(d => d.Id)
            .Select(d => d.Id).FirstOrDefaultAsync();
        failures += await MeasureAsync("Historique des passages d'un dossier", iterations, async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var movements = scope.ServiceProvider.GetRequiredService<MovementService>();
            await movements.GetHistoryAsync(latestDocumentId, 50);
        });

        failures += await MeasureAsync("Tableau de bord (synthèse du jour)", iterations, async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var dashboard = scope.ServiceProvider.GetRequiredService<DashboardService>();
            await dashboard.GetSummaryAsync();
        });

        failures += await MeasureAsync("Édition 1.1 — synthèse des mouvements", iterations, async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
            await reports.BuildAsync(BorderNext.Application.Reporting.ReportCatalog.MovementSummary,
                new ReportParameters(From: DateTime.Today.AddDays(-30), To: DateTime.Today));
        });

        failures += await MeasureAsync("Édition — registre détaillé des passages", iterations, async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
            await reports.BuildAsync(BorderNext.Application.Reporting.ReportCatalog.MovementRegister,
                new ReportParameters(From: DateTime.Today.AddDays(-30), To: DateTime.Today));
        });

        failures += await MeasureAsync("Contrôle des listes d'alerte (nom + date de naissance)", iterations, async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var matcher = scope.ServiceProvider.GetRequiredService<StopListMatcher>();
            await matcher.CheckPersonAsync(new BorderNext.Application.Services.StopListCriteria(
                Surname: "MARTIN", GivenName: "CLAIRE", DateOfBirth: DateTime.Today.AddYears(-40)));
        });

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine($"Toutes les mesures sont sous {TargetMilliseconds} ms.");
            return 0;
        }

        Console.WriteLine($"{failures} mesure(s) au-dessus de {TargetMilliseconds} ms — à examiner.");
        return 4;
    }

    private static async Task<int> MeasureAsync(string label, int iterations, Func<Task> action)
    {
        // Passage à blanc : le premier appel d'un écran après le démarrage (plans
        // d'exécution, caches EF) est mesuré à part et ne doit pas fausser la moyenne.
        await action();

        var times = new List<double>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await action();
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var average = times.Average();
        var max = times.Max();
        var verdict = average < TargetMilliseconds ? "OK" : "LENT";
        Console.WriteLine($"{label,-46} {average,7:F0} ms {max,7:F0} ms   {verdict}");
        return average < TargetMilliseconds ? 0 : 1;
    }
}
