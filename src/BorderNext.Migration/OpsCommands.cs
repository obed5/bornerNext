using BorderNext.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Migration;

/// <summary>
/// Commande « purge-files » : applique la durée de conservation (réglage
/// Exchange_MessageRetentionDays) aux fichiers d'échange et aux journaux du poste.
/// La purge est journalisée par l'application ; ce traitement est destiné à une tâche
/// planifiée du serveur.
/// </summary>
public static class PurgeCommand
{
    public static async Task<int> RunAsync(CommandLine options)
    {
        var dryRun = options.HasFlag("dry-run");
        await using var db = await Program.OpenTargetAsync(options);

        var store = new ExchangeFileStore(db, new BorderNext.Infrastructure.Services.SystemClock());
        var retention = new FileRetentionService(db, store, new BorderNext.Infrastructure.Services.SystemClock());

        var days = options.Get("days") is { } raw && int.TryParse(raw, out var forced)
            ? forced
            : await retention.GetRetentionDaysAsync();

        if (days <= 0)
        {
            Console.WriteLine("Aucune durée de conservation configurée (Exchange_MessageRetentionDays = 0) : "
                              + "aucune suppression. Utilisez --days pour forcer une durée.");
            return 0;
        }

        Console.WriteLine($"Conservation : {days} jour(s)" + (dryRun ? " — simulation, aucune suppression." : "."));
        var root = await store.GetRootAsync();
        Console.WriteLine($"Répertoire des échanges : {root}");

        var reports = await retention.PurgeAllAsync(days, dryRun);
        var totalFiles = 0;
        var totalBytes = 0L;

        foreach (var (directory, report) in reports)
        {
            Console.WriteLine($"  {directory}: {report.FileCount} fichier(s), {report.BytesFreed / 1024} Ko");
            totalFiles += report.FileCount;
            totalBytes += report.BytesFreed;
            if (!options.HasFlag("verbose")) continue;
            foreach (var file in report.DeletedFiles.Take(50)) Console.WriteLine($"      {file}");
        }

        Console.WriteLine(dryRun
            ? $"Simulation : {totalFiles} fichier(s) seraient supprimés ({totalBytes / 1024} Ko)."
            : $"Purge terminée : {totalFiles} fichier(s) supprimé(s) ({totalBytes / 1024} Ko).");
        return 0;
    }
}

/// <summary>
/// Commande « reset-password » : réinitialise le mot de passe d'un compte et impose
/// son changement à la prochaine connexion. Remplace l'ancien mot de passe maître :
/// l'accès de secours passe par une opération tracée sur le serveur.
/// </summary>
public static class ResetPasswordCommand
{
    public static async Task<int> RunAsync(CommandLine options)
    {
        var userName = options.Get("user") ?? "admin";
        await using var db = await Program.OpenTargetAsync(options);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName);
        if (user is null)
        {
            Console.Error.WriteLine($"Compte introuvable : {userName}");
            return 1;
        }

        var password = options.Get("password");
        var generated = false;
        if (string.IsNullOrWhiteSpace(password))
        {
            password = BorderNext.Infrastructure.Security.BootstrapSecretStore.GeneratePassword();
            generated = true;
        }

        var hasher = new BorderNext.Infrastructure.Security.Pbkdf2PasswordHasher();
        user.PasswordHash = hasher.Hash(password);
        user.MustChangePassword = true;
        user.FailedAttemptCount = 0;
        user.LockoutUntil = null;
        user.UpdateDate = DateTime.Now;
        user.UpdateUser = "reset-password";
        await db.SaveChangesAsync();

        // Tracé dans le journal d'audit de l'application (sans le mot de passe).
        db.AuditLogs.Add(new Domain.Entities.AuditLog
        {
            EventType = Domain.Entities.AuditEventTypes.RowUpdated,
            UserId = "reset-password",
            Entity = nameof(Domain.Entities.User),
            EntityId = user.Id,
            Details = $"Mot de passe réinitialisé par l'exploitation (compte {user.UserName})",
            EventTime = DateTime.Now,
        });
        await db.SaveChangesAsync();

        Console.WriteLine($"Mot de passe réinitialisé pour « {user.UserName} » "
                          + "(changement imposé à la prochaine connexion).");
        if (generated) Console.WriteLine($"Mot de passe provisoire : {password}");
        return 0;
    }
}

/// <summary>Commande « inspect » : état de la base cible (diagnostic d'exploitation).</summary>
public static class InspectCommand
{
    public static async Task<int> RunAsync(CommandLine options)
    {
        await using var db = await Program.OpenTargetAsync(options);

        Console.WriteLine("Base cible :");
        Console.WriteLine($"  migrations appliquées : {db.Database.GetAppliedMigrations().Count()}");
        Console.WriteLine($"  personnes            : {await db.Persons.CountAsync()}");
        Console.WriteLine($"  documents de voyage  : {await db.TravelDocuments.CountAsync()}");
        Console.WriteLine($"  mouvements           : {await db.Movements.CountAsync()}");
        Console.WriteLine($"  visas                : {await db.Visas.CountAsync()}");
        Console.WriteLine($"  permis               : {await db.Permits.CountAsync()}");
        Console.WriteLine($"  listes (personnes)   : {await db.PersonsStopList.CountAsync()}");
        Console.WriteLine($"  listes (documents)   : {await db.DocumentsStopList.CountAsync()}");
        Console.WriteLine($"  dossiers douteux     : {await db.Set<Domain.Entities.DoubtablePerson>().CountAsync()}");
        Console.WriteLine($"  utilisateurs         : {await db.Users.CountAsync()}");
        Console.WriteLine($"  comptes verrouillés  : {await db.Users.CountAsync(u => u.LockoutUntil != null && u.LockoutUntil > DateTime.Now)}");
        Console.WriteLine($"  lignes d'audit       : {await db.AuditLogs.CountAsync()}");
        Console.WriteLine($"  réglages             : {await db.Settings.CountAsync()}");
        Console.WriteLine($"  fichiers échangés    : {await db.Set<Domain.Entities.ApisMessageFile>().CountAsync()}");

        var legacy = await db.Persons.CountAsync(p => p.LegacyId != null)
                     + await db.TravelDocuments.CountAsync(d => d.LegacyId != null);
        if (legacy > 0)
            Console.WriteLine($"  lignes issues de la reprise d'historique : {legacy}");

        return 0;
    }
}
