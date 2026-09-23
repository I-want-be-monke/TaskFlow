using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskFlow.Infrastructure.Observability;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.DbMigrator;

public sealed class MigrationRunner(ILogger<MigrationRunner> logger)
{
    // ASCII "TASKFLOW" encoded as a stable signed bigint.
    public const long AdvisoryLockKey = 0x5441534B464C4F57L;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    public async Task<int> RunAsync(
        DbMigratorSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string? failedMigration = null;
        bool lockAcquired = false;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using TaskFlowDbContext dbContext = CreateDbContext(settings.ConnectionString);
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            DbConnection connection = dbContext.Database.GetDbConnection();

            try
            {
                await AcquireAdvisoryLockAsync(connection, settings.LockTimeout, cancellationToken);
                lockAcquired = true;

                string[] appliedMigrations = (await dbContext.Database
                        .GetAppliedMigrationsAsync(cancellationToken))
                    .ToArray();
                string[] pendingMigrations = (await dbContext.Database
                        .GetPendingMigrationsAsync(cancellationToken))
                    .ToArray();

                string fromSchemaVersion = appliedMigrations.LastOrDefault() ?? "empty";
                string targetSchemaVersion = pendingMigrations.LastOrDefault() ?? fromSchemaVersion;
                failedMigration = pendingMigrations.FirstOrDefault();

                logger.LogInformation(
                    TaskFlowLogEvents.MigrationStarted,
                    "Migration started. From={from_schema_version} Target={target_schema_version}",
                    fromSchemaVersion,
                    targetSchemaVersion);

                stopwatch.Restart();
                await dbContext.Database.MigrateAsync(cancellationToken);

                logger.LogInformation(
                    TaskFlowLogEvents.MigrationCompleted,
                    "Migration completed. AppliedCount={applied_count} DurationMs={duration_ms}",
                    pendingMigrations.Length,
                    stopwatch.Elapsed.TotalMilliseconds);

                return DbMigratorExitCodes.Success;
            }
            finally
            {
                if (lockAcquired)
                {
                    await ReleaseAdvisoryLockBestEffortAsync(connection);
                    lockAcquired = false;
                }
            }
        }
        catch (MigrationLockTimeoutException)
        {
            logger.LogError(
                TaskFlowLogEvents.MigrationFailed,
                "Migration failed. ErrorType={error_type} FailedMigration={failed_migration}",
                nameof(MigrationLockTimeoutException),
                "advisory_lock");
            return DbMigratorExitCodes.LockTimeout;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                TaskFlowLogEvents.MigrationFailed,
                "Migration cancelled. ErrorType={error_type} FailedMigration={failed_migration}",
                nameof(OperationCanceledException),
                failedMigration ?? "unknown");
            return DbMigratorExitCodes.Cancelled;
        }
        catch (Exception exception)
        {
            logger.LogError(
                TaskFlowLogEvents.MigrationFailed,
                exception,
                "Migration failed. FailedMigration={failed_migration}",
                failedMigration ?? "unknown");
            return DbMigratorExitCodes.MigrationFailed;
        }
    }

    private static TaskFlowDbContext CreateDbContext(string connectionString)
    {
        DbContextOptions<TaskFlowDbContext> options = new DbContextOptionsBuilder<TaskFlowDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(TaskFlowDbContext).Assembly.FullName))
            .EnableSensitiveDataLogging(false)
            .Options;

        return new TaskFlowDbContext(options);
    }

    private static async Task AcquireAdvisoryLockAsync(
        DbConnection connection,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            while (!await TryAcquireAdvisoryLockAsync(connection, timeoutSource.Token))
            {
                await Task.Delay(RetryDelay, timeoutSource.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MigrationLockTimeoutException(timeout);
        }
    }

    private static async Task<bool> TryAcquireAdvisoryLockAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@lock_key);";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = AdvisoryLockKey;
        command.Parameters.Add(parameter);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool acquired && acquired;
    }

    private static async Task ReleaseAdvisoryLockBestEffortAsync(DbConnection connection)
    {
        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@lock_key);";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "lock_key";
            parameter.Value = AdvisoryLockKey;
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (DbException)
        {
            // If the session is already broken, closing/disposal releases a session-scoped advisory lock.
        }
        catch (InvalidOperationException)
        {
            // A closed/disposed connection releases its session-scoped advisory locks automatically.
        }
    }

    private sealed class MigrationLockTimeoutException(TimeSpan timeout)
        : TimeoutException($"Could not acquire the TaskFlow migration advisory lock within {timeout}.");
}
