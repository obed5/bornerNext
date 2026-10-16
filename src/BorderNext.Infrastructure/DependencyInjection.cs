using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Infrastructure.Devices;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Persistence.Seeding;
using BorderNext.Infrastructure.Security;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BorderNext.Infrastructure;

/// <summary>Enregistrement des services d'infrastructure (composition root).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddBorderNextInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BorderNextDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(BorderNextDbContext).Assembly.FullName);
                // Requêtes scindées : les chargements de référentiels incluent plusieurs collections.
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            }));

        // Les services applicatifs dépendent de l'abstraction DbContext.
        services.AddScoped<Microsoft.EntityFrameworkCore.DbContext>(sp =>
            sp.GetRequiredService<BorderNextDbContext>());

        services.AddSingleton<ICurrentUser, CurrentUser>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<AuthService>();
        services.AddScoped<SettingsService>();
        // Lot 1 — référentiels et administration.
        services.AddScoped<ReferenceDataService>();
        services.AddScoped<GeoService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<AuditQueryService>();
        // Lot 2 — dossier frontalier.
        services.AddScoped<DocumentService>();
        services.AddScoped<MovementService>();
        services.AddScoped<VisaService>();
        // Lot 3 — biométrie et appareils (simulateurs ; les pilotes matériels réels
        // remplaceront les implémentations derrière les mêmes interfaces).
        services.AddSingleton<IFingerprintMatcher, SimulatedFingerprintMatcher>();
        services.AddScoped<IDeviceRegistry, DeviceRegistry>();
        services.AddScoped<ICameraDevice>(sp => sp.GetRequiredService<IDeviceRegistry>().Camera);
        services.AddScoped<IFingerprintScanner>(sp => sp.GetRequiredService<IDeviceRegistry>().Fingerprints);
        services.AddScoped<IDocumentReader>(sp => sp.GetRequiredService<IDeviceRegistry>().DocumentReader);
        services.AddScoped<BiometricService>();
        services.AddScoped<UserBiometricsService>();
        // Lot 4 — permis et enforcement.
        services.AddScoped<PermitService>();
        services.AddScoped<EnforcementService>();
        // Lot 5 — listes d'alerte et personnes douteuses.
        services.AddScoped<StopListMatcher>();
        services.AddScoped<WatchlistService>();
        services.AddScoped<DoubtablePersonService>();
        // Lot 6 — opérations frontière (voyages, passages, navires, transporteurs).
        services.AddScoped<TripService>();
        services.AddScoped<TransportReferentialService>();
        // Tableau de bord du poste (héritage : schéma dashboard).
        services.AddScoped<DashboardService>();

        return services;
    }

    /// <summary>
    /// Initialisation de la base : applique les migrations, crée un administrateur
    /// initial si aucun compte n'existe (première installation), puis sème les
    /// référentiels (nomenclatures, pays, villes, postes frontières, droits par défaut).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider provider, string bootstrapAdminPassword, CancellationToken ct = default)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BorderNextDbContext>();
        await db.Database.MigrateAsync(ct);

        if (!await db.Users.AnyAsync(ct))
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            db.Users.Add(new Domain.Entities.User
            {
                Id = "ADM001",
                UserName = "admin",
                PasswordHash = hasher.Hash(bootstrapAdminPassword),
                MustChangePassword = true,
                FirstName = "Initial",
                LastName = "Administrator",
                UserRoleId = 1,
                UpdateDate = DateTime.Now,
                UpdateUser = "bootstrap"
            });
            await db.SaveChangesAsync(ct);
        }

        await ReferenceDataSeeder.SeedAsync(db, ct);
    }
}
