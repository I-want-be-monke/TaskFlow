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

    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning(
                TaskFlowLogEvents.ConcurrencyConflict,
                "Optimistic concurrency conflict. Reason={reason_code}",
                "stale_version");
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
            logger.LogError(
                TaskFlowLogEvents.DatabaseUnavailable,
                "Database unavailable while saving changes. ErrorType={error_type}",
                npgsqlException.GetType().Name);
            return Result.Failure(PersistenceErrors.DatabaseUnavailable());
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(
                TaskFlowLogEvents.DatabaseUnavailable,
                "Database unavailable while saving changes. ErrorType={error_type}",
                exception.GetType().Name);
            return Result.Failure(PersistenceErrors.DatabaseUnavailable());
        }
    }
}
