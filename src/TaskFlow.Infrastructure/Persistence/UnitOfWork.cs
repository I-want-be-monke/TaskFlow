using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class UnitOfWork(
    TaskFlowDbContext dbContext,
    ILogger<UnitOfWork> logger) : IUnitOfWork
{
    private const string TagNameUniqueConstraint = "ux_tags_owner_user_id_normalized_name";
    private static readonly Action<ILogger, string, Exception?> LogConcurrencyConflict =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            TaskFlowLogEvents.ConcurrencyConflict,
            "Optimistic concurrency conflict. Reason={reason_code}");
    private static readonly Action<ILogger, string, Exception?> LogDatabaseUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            TaskFlowLogEvents.DatabaseUnavailable,
            "Database unavailable while saving changes. ErrorType={error_type}");

    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            LogConcurrencyConflict(logger, "stale_version", null);
            return Result.Failure(PersistenceErrors.ConcurrencyConflict());
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgresException &&
                  postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                  postgresException.ConstraintName == TagNameUniqueConstraint)
        {
            return Result.Failure(PersistenceErrors.DuplicateTagName());
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException)
        {
            return Result.Failure(PersistenceErrors.WriteFailure());
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is NpgsqlException npgsqlException)
        {
            LogDatabaseUnavailable(logger, npgsqlException.GetType().Name, null);
            return Result.Failure(PersistenceErrors.DatabaseUnavailable());
        }
        catch (NpgsqlException exception)
        {
            LogDatabaseUnavailable(logger, exception.GetType().Name, null);
            return Result.Failure(PersistenceErrors.DatabaseUnavailable());
        }
    }
}
