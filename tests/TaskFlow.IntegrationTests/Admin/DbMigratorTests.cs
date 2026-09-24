using System.Diagnostics;
using Npgsql;
using TaskFlow.DbMigrator;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.IntegrationTests.Admin;

public sealed class DbMigratorTests(MigratorPostgresFixture fixture) : IClassFixture<MigratorPostgresFixture>
{
    [Fact]
    public async Task EmptyDatabase_MigratesSuccessfullyWithMigratorRole()
    {
        string adminConnectionString = await fixture.CreateEmptyDatabaseAsync();
        RoleConnection migrator = await fixture.CreateRoleAsync(
            adminConnectionString,
            "taskflow_migrator",
            allowSchemaCreate: true);

        int exitCode = await DbMigratorApplication.RunAsync(
            CreateSettings(migrator.ConnectionString, TimeSpan.FromSeconds(5)),
            TestContext.Current.CancellationToken);

        Assert.Equal(DbMigratorExitCodes.Success, exitCode);

        await using NpgsqlConnection verify = new(adminConnectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='projects') " +
            "AND EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='__EFMigrationsHistory');",
            verify);

        Assert.True((bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task SecondMigrator_WhenAdvisoryLockIsHeld_FailsWithinBoundedTimeout()
    {
        string adminConnectionString = await fixture.CreateEmptyDatabaseAsync();
        RoleConnection migrator = await fixture.CreateRoleAsync(
            adminConnectionString,
            "taskflow_migrator",
            allowSchemaCreate: true);

        await using NpgsqlConnection blocker = new(migrator.ConnectionString);
        await blocker.OpenAsync(TestContext.Current.CancellationToken);
        await SetAdvisoryLockAsync(blocker, acquire: true);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            int exitCode = await DbMigratorApplication.RunAsync(
                CreateSettings(migrator.ConnectionString, TimeSpan.FromMilliseconds(500)),
                TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.Equal(DbMigratorExitCodes.LockTimeout, exitCode);
            Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(350), TimeSpan.FromSeconds(5));
        }
        finally
        {
            await SetAdvisoryLockAsync(blocker, acquire: false);
        }
    }

    [Fact]
    public async Task MigrationFailure_ReturnsNonZeroExitCode()
    {
        string adminConnectionString = await fixture.CreateEmptyDatabaseAsync();
        RoleConnection migrator = await fixture.CreateRoleAsync(
            adminConnectionString,
            "taskflow_migrator",
            allowSchemaCreate: true);

        NpgsqlConnectionStringBuilder invalid = new(migrator.ConnectionString)
        {
            Password = "definitely_wrong_password",
            Timeout = 2,
        };

        int exitCode = await DbMigratorApplication.RunAsync(
            CreateSettings(invalid.ConnectionString, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(DbMigratorExitCodes.MigrationFailed, exitCode);
        Assert.NotEqual(DbMigratorExitCodes.Success, exitCode);
    }

    [Fact]
    public async Task ApiRole_CannotPerformSchemaDdl()
    {
        string adminConnectionString = await fixture.CreateEmptyDatabaseAsync();
        RoleConnection app = await fixture.CreateRoleAsync(
            adminConnectionString,
            "taskflow_app",
            allowSchemaCreate: false);

        await using NpgsqlConnection connection = new(app.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new("CREATE TABLE should_not_exist(id integer);", connection);

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task MigratorRole_HasRequiredSchemaDdlPrivilege()
    {
        string adminConnectionString = await fixture.CreateEmptyDatabaseAsync();
        RoleConnection migrator = await fixture.CreateRoleAsync(
            adminConnectionString,
            "taskflow_migrator",
            allowSchemaCreate: true);

        await using NpgsqlConnection connection = new(migrator.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new("CREATE TABLE migrator_ddl_probe(id integer); DROP TABLE migrator_ddl_probe;", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void MigrationEventCatalog_RemainsStable()
    {
        Assert.Equal(5101, TaskFlowLogEvents.MigrationStarted.Id);
        Assert.Equal(nameof(TaskFlowLogEvents.MigrationStarted), TaskFlowLogEvents.MigrationStarted.Name);
        Assert.Equal(5102, TaskFlowLogEvents.MigrationCompleted.Id);
        Assert.Equal(nameof(TaskFlowLogEvents.MigrationCompleted), TaskFlowLogEvents.MigrationCompleted.Name);
        Assert.Equal(5103, TaskFlowLogEvents.MigrationFailed.Id);
        Assert.Equal(nameof(TaskFlowLogEvents.MigrationFailed), TaskFlowLogEvents.MigrationFailed.Name);
    }

    private static DbMigratorSettings CreateSettings(string connectionString, TimeSpan lockTimeout) => new(
        connectionString,
        lockTimeout,
        "test-release",
        "Testing",
        "db-migrator-test");

    private static async Task SetAdvisoryLockAsync(NpgsqlConnection connection, bool acquire)
    {
        string sql = acquire
            ? "SELECT pg_advisory_lock(@lock_key);"
            : "SELECT pg_advisory_unlock(@lock_key);";
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("lock_key", MigrationRunner.AdvisoryLockKey);
        await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
