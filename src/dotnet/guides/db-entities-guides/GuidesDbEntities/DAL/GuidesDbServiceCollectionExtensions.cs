using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    /// <summary>
    /// Registers <see cref="GuidesContext"/>, reading ConnectionStrings:Guides when the context is
    /// first created rather than at registration, so hosts and tests can supply it late.
    /// </summary>
    public static IServiceCollection AddGuidesDb(this IServiceCollection services)
    {
        services.AddDbContext<GuidesContext>(
            (provider, options) =>
            {
                var connectionString =
                    provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                    ?? throw new InvalidOperationException(
                        $"Connection string '{ConnectionStringName}' is not set (user secrets in Development, "
                            + "the ConnectionStrings__Guides environment variable elsewhere)."
                    );
                Configure(options, connectionString);
            }
        );
        return services;
    }

    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        string connectionString
    ) => options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()).UseSnakeCaseNamingConvention();
}
