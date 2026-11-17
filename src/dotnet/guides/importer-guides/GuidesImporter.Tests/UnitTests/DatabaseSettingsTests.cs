using GuidesDbEntities.DAL;
using Npgsql;

namespace GuidesImporter.Tests.UnitTests;

public class DatabaseSettingsTests
{
    private const string Base = "Host=db;Database=wayfinder;Username=wayfinder;Password=secret";

    [Fact]
    public void Applies_pool_size_and_timeouts()
    {
        var applied = new NpgsqlConnectionStringBuilder(
            new DatabaseSettings { MaxPoolSize = 7, CommandTimeoutSeconds = 9 }.Apply(Base)
        );

        Assert.Equal(7, applied.MaxPoolSize);
        Assert.Equal(9, applied.CommandTimeout);
    }

    [Fact]
    public void Gss_encryption_is_off_by_default()
    {
        var applied = new NpgsqlConnectionStringBuilder(GuidesDbServiceCollectionExtensions.WithoutGssUnlessSet(Base));

        Assert.Equal(GssEncryptionMode.Disable, applied.GssEncryptionMode);
        Assert.Equal("secret", applied.Password);
    }

    [Theory]
    [InlineData("GSS Encryption Mode=Require")]
    [InlineData("GssEncryptionMode=Require")]
    public void Keeps_a_gss_mode_the_connection_string_sets(string setting)
    {
        var applied = new NpgsqlConnectionStringBuilder(
            GuidesDbServiceCollectionExtensions.WithoutGssUnlessSet($"{Base};{setting}")
        );

        Assert.Equal(GssEncryptionMode.Require, applied.GssEncryptionMode);
    }
}
