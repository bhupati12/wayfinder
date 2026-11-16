using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GuidesDbEntities.DAL;

/// <summary>
/// Used only by `dotnet ef` to build the model for migrations. Adding a migration does not open a
/// connection. `dotnet ef database update` reads the connection string from the
/// ConnectionStrings__Guides environment variable; the importer's `migrateDatabase` job is the
/// usual way to apply migrations.
/// </summary>
public class GuidesContextDesignTimeFactory : IDesignTimeDbContextFactory<GuidesContext>
{
    public GuidesContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Guides")
            ?? "Host=localhost;Database=wayfinder";

        var options = new DbContextOptionsBuilder<GuidesContext>();
        GuidesDbServiceCollectionExtensions.Configure(options, connectionString);
        return new GuidesContext(options.Options);
    }
}
