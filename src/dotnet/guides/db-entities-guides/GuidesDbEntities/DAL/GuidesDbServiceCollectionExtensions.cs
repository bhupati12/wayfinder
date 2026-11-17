using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GuidesDbEntities.DAL;

public static class GuidesDbServiceCollectionExtensions
{
    /// <summary>Name of the connection string in configuration (ConnectionStrings:Guides).</summary>
    public const string ConnectionStringName = "Guides";

    /// <summary>Registers <see cref="GuidesContext"/> on PostgreSQL with snake_case naming.</summary>
    public static IServiceCollection AddGuidesDb(
        this IServiceCollection services,
        string connectionString
    )
    {
        services.AddDbContext<GuidesContext>(options => Configure(options, connectionString));
        return services;
    }

    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        string connectionString
    ) => options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()).UseSnakeCaseNamingConvention();
}
