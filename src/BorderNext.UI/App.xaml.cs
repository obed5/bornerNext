using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using BorderNext.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BorderNext.UI;

/// <summary>Point d'entrée de l'application WPF (hôte générique + DI + Serilog).</summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Filet de sécurité : toute exception d'interface est journalisée et affichée,
        // au lieu de faire planter l'application silencieusement.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var builder = Host.CreateApplicationBuilder();

        var logDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "logs");
        System.IO.Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(System.IO.Path.Combine(logDirectory, "bordernext-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);

        var connectionString = builder.Configuration.GetConnectionString("BorderNext")
            ?? "Server=.\\SQLEXPRESS;Database=BorderNext;Trusted_Connection=True;TrustServerCertificate=True";
        builder.Services.AddBorderNextInfrastructure(connectionString);
        builder.Services.AddBorderNextWindows();

        _host = builder.Build();
        await _host.StartAsync();

        // Initialisation de la base : migrations + admin initial (mot de passe à changer à la 1re connexion).
        var bootstrapPassword = builder.Configuration["Bootstrap:AdminPassword"] ?? "ChangeMe!2026";
        try
        {
            await _host.Services.InitializeDatabaseAsync(bootstrapPassword);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Échec de l'initialisation de la base");
            MessageBox.Show($"Impossible d'initialiser la base de données :\n{ex.Message}",
                "BorderNext", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        SetCulture(builder.Configuration["App:DefaultCulture"] ?? "fr");

        // Session utilisateur : les fenêtres sont résolues dans une portée de services
        // dédiée (DbContext/AuthService libérés à la déconnexion).
        var scopes = _host.Services.GetRequiredService<SessionScopes>();
        var login = scopes.NewSession().GetRequiredService<LoginWindow>();
        login.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Exception non gérée (interface)");
        MessageBox.Show($"Une erreur inattendue est survenue :\n\n{e.Exception.Message}\n\n" +
                        $"Le détail a été écrit dans le journal (dossier logs).",
            "BorderNext", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    public static void SetCulture(string cultureCode)
    {
        var culture = new CultureInfo(cultureCode);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}
