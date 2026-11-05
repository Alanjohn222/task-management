using Npgsql;
using Testcontainers.PostgreSql;

namespace TaskManagement.Api.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17")
            .WithDatabase("task_management")
            .WithUsername("task_app")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .Build();

    public string ConnectionString => database.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            await database.StartAsync();

            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            var scriptsDirectory = Path.Combine(AppContext.BaseDirectory, "db");
            var scripts = Directory
                .GetFiles(scriptsDirectory, "*.sql")
                .OrderBy(path => path, StringComparer.Ordinal);

            foreach (var script in scripts)
            {
                var sql = await File.ReadAllTextAsync(script);

                await using var transaction = await connection.BeginTransactionAsync();
                await using var command = new NpgsqlCommand(sql, connection, transaction);

                await command.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        await database.DisposeAsync();
    }
}