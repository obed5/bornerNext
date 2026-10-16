using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BorderNext.Infrastructure.Persistence;

/// <summary>Fabrique design-time pour les outils EF Core (migrations).</summary>
public sealed class BorderNextDbContextFactory : IDesignTimeDbContextFactory<BorderNextDbContext>
{
    public BorderNextDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BORDERNEXT_CONNECTION")
            ?? "Server=.\\SQLEXPRESS;Database=BorderNext;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(BorderNextDbContext).Assembly.FullName))
            .Options;

        return new BorderNextDbContext(options);
    }
}
