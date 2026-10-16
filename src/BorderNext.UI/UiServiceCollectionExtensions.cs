using Microsoft.Extensions.DependencyInjection;

namespace BorderNext.UI;

/// <summary>
/// Enregistrement des services d'interface (fenêtres + portées de session).
/// Les fenêtres sont <b>transitoires</b> et résolues dans la portée de session :
/// cela évite de capturer des services « scoped » (AuthService, DbContext) dans
/// des singletons, et garantit qu'une fenêtre fermée n'est jamais réutilisée.
/// </summary>
public static class UiServiceCollectionExtensions
{
    public static IServiceCollection AddBorderNextWindows(this IServiceCollection services)
    {
        services.AddSingleton<SessionScopes>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<ChangePasswordWindow>();
        services.AddTransient<MainWindow>();
        services.AddTransient<AdminWindow>();
        // Lot 2 — dossier frontalier.
        services.AddTransient<PassportWindow>();
        services.AddTransient<DocumentSearchWindow>();
        services.AddTransient<VisaWindow>();
        // Lot 4 — permis et enforcement.
        services.AddTransient<PermitWindow>();
        services.AddTransient<EnforcementWindow>();
        // Lot 5 — listes de surveillance et personnes douteuses.
        services.AddTransient<WatchlistWindow>();
        // Lot 6 — opérations frontière.
        services.AddTransient<BorderOpsWindow>();
        // Lot 7 — échanges frontaliers (messages APIS, connecteurs, journal).
        services.AddTransient<ExchangesWindow>();
        // Lot 8 — éditions et questionnaires santé.
        services.AddTransient<ReportsWindow>();
        services.AddTransient<HealthQuestionnaireWindow>();
        return services;
    }
}
