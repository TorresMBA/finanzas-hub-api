using Npgsql;
using Testcontainers.PostgreSql;

namespace PersonalFinance.IntegrationTests.Support;

/// <summary>
/// A real PostgreSQL for the test run. By default a throwaway container; when
/// <c>PERSONALFINANCE_TEST_POSTGRES</c> holds a connection string to an existing server (for machines
/// without Docker), a uniquely named database is created there and dropped at the end.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public const string ExternalServerVariable = "PERSONALFINANCE_TEST_POSTGRES";

    private readonly PostgreSqlContainer? _container;
    private readonly string? _serverConnectionString;
    private readonly string? _databaseName;

    private TestDatabase(string connectionString, PostgreSqlContainer? container, string? serverConnectionString, string? databaseName)
    {
        ConnectionString = connectionString;
        _container = container;
        _serverConnectionString = serverConnectionString;
        _databaseName = databaseName;
    }

    public string ConnectionString { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var server = Environment.GetEnvironmentVariable(ExternalServerVariable);
        if (string.IsNullOrWhiteSpace(server))
        {
            var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync();
            return new TestDatabase(container.GetConnectionString(), container, null, null);
        }

        var databaseName = $"pf_test_{Guid.NewGuid():N}";
        await ExecuteAsync(server, $"CREATE DATABASE \"{databaseName}\"");
        var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = databaseName }.ConnectionString;
        return new TestDatabase(connectionString, null, server, databaseName);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(_serverConnectionString!, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
