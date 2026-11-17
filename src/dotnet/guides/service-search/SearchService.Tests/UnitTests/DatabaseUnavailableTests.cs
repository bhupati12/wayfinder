using Npgsql;
using SearchService.Application.Services;

namespace SearchService.Tests.UnitTests;

public sealed class DatabaseUnavailableTests
{
    [Theory]
    [InlineData("57P01", true)] // admin shutdown: PostgreSQL stopped under an open connection
    [InlineData("53300", true)] // too many connections
    [InlineData("42601", false)] // syntax error: a bug, stays a 500
    [InlineData("23505", false)] // unique violation
    public void Transient_server_errors_count_as_unavailable(string sqlState, bool unavailable)
    {
        var error = new PostgresException("message", "FATAL", "FATAL", sqlState);
        var wrapped = new InvalidOperationException("likely due to a transient failure", error);

        Assert.Equal(unavailable, DatabaseUnavailableExceptionHandler.IsDatabaseUnavailable(wrapped));
    }

    [Fact]
    public void Other_exceptions_are_not_database_outages()
    {
        Assert.False(DatabaseUnavailableExceptionHandler.IsDatabaseUnavailable(new InvalidOperationException("x")));
    }
}
