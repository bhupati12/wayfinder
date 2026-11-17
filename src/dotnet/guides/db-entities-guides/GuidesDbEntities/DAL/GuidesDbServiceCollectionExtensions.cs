using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GuidesDbEntities.DAL;

public static class GuidesDbServiceCollectionExtensions
{
    /// <summary>Name of the connection string in configuration (ConnectionStrings:Guides).</summary>
    public const string ConnectionStringName = "Guides";

    /// <summary>Registers <see cref="GuidesContext"/> on PostgreSQL with snake_case naming.</summary>
    public static IServiceCollection AddGuidesDb(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<GuidesContext>(options => Configure(options, connectionString));
        return services;
    }

    /// <summary>
    /// Registers <see cref="GuidesContext"/>, reading ConnectionStrings:Guides when the context is
    /// first created rather than at registration, so hosts and tests can supply it late. The
    /// "Database" section sets the pool size and command timeout (see <see cref="DatabaseSettings"/>).
    /// </summary>
    public static IServiceCollection AddGuidesDb(this IServiceCollection services)
    {
        services.AddDbContext<GuidesContext>(
            (provider, options) =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                var connectionString = configuration.GetConnectionString(ConnectionStringName)
                    ?? throw new InvalidOperationException(
                        $"Connection string '{ConnectionStringName}' is not set (user secrets in Development, "
                            + "the ConnectionStrings__Guides environment variable elsewhere)."
                    );
                var settings = configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>()
                    ?? new DatabaseSettings();
                Configure(options, settings.Apply(connectionString), settings.CommandTimeoutSeconds);
            }
        );
        return services;
    }

    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        string connectionString,
        int? commandTimeoutSeconds = null
    ) =>
        options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                if (commandTimeoutSeconds is { } timeout)
                {
                    npgsql.CommandTimeout(timeout);
                }
            })
            .UseSnakeCaseNamingConvention();
}

/// <summary>
/// Connection pool and timeout settings, bound from "Database". Sized deliberately: PostgreSQL's
/// default limit is 100 connections shared by every instance and job, so each API instance
/// takes a bounded share instead of Npgsql's default of 100.
/// </summary>
public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>Connections per instance. With 3 replicas, 3 x 20 = 60 leaves room for jobs and admin.</summary>
    public int MaxPoolSize { get; set; } = 20;

    public int MinPoolSize { get; set; }

    /// <summary>Seconds before a single SQL command is cancelled.</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;

    /// <summary>Seconds to wait for a free connection from the pool or a new one.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 5;

    public string Apply(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = MaxPoolSize,
            MinPoolSize = MinPoolSize,
            Timeout = ConnectTimeoutSeconds,
            CommandTimeout = CommandTimeoutSeconds,
        };
        return builder.ConnectionString;
    }
}
