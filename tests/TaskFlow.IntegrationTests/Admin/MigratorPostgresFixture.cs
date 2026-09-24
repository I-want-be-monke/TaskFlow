using Npgsql;
using Testcontainers.PostgreSql;

namespace TaskFlow.IntegrationTests.Admin;

public sealed class MigratorPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("taskflow_admin_tests")
        .WithUsername("taskflow_admin")
        .WithPassword("taskflow_admin_password")
        .Build();

    public string AdminConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    public async Task<string> CreateEmptyDatabaseAsync()
    {
        string database = $"taskflow_{Guid.NewGuid():N}";
        await using NpgsqlConnection connection = new(AdminConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new($"CREATE DATABASE {database};", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        NpgsqlConnectionStringBuilder builder = new(AdminConnectionString)
        {
            Database = database,
        };
        return builder.ConnectionString;
    }

    public async Task<RoleConnection> CreateRoleAsync(
        string databaseConnectionString,
        string rolePrefix,
        bool allowSchemaCreate)
    {
        string role = $"{rolePrefix}_{Guid.NewGuid():N}";
        string password = Guid.NewGuid().ToString("N");
        NpgsqlConnectionStringBuilder targetBuilder = new(databaseConnectionString);
        string database = targetBuilder.Database!;

        await using (NpgsqlConnection admin = new(AdminConnectionString))
        {
            await admin.OpenAsync(TestContext.Current.CancellationToken);
            await using NpgsqlCommand createRole = new(
                $"CREATE ROLE {role} LOGIN PASSWORD '{password}'; GRANT CONNECT ON DATABASE {database} TO {role};",
                admin);
            await createRole.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using (NpgsqlConnection targetAdmin = new(databaseConnectionString))
        {
            await targetAdmin.OpenAsync(TestContext.Current.CancellationToken);
            await using NpgsqlCommand revokePublic = new("REVOKE CREATE ON SCHEMA public FROM PUBLIC;", targetAdmin);
            await revokePublic.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            string createGrant = allowSchemaCreate ? ", CREATE" : string.Empty;
            await using NpgsqlCommand grant = new(
                $"GRANT USAGE{createGrant} ON SCHEMA public TO {role};",
                targetAdmin);
            await grant.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        targetBuilder.Username = role;
        targetBuilder.Password = password;
        return new RoleConnection(role, targetBuilder.ConnectionString);
    }
}

public sealed record RoleConnection(string RoleName, string ConnectionString);
