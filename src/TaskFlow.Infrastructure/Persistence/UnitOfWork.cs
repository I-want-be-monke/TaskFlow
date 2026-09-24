using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class UnitOfWork(TaskFlowDbContext dbContext) : IUnitOfWork
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
            when (exception.InnerException is NpgsqlException)
        {
            return Result.Failure(PersistenceErrors.DatabaseUnavailable());
        }
        catch (NpgsqlException)
        {
            return Result.Failure(PersistenceErrors.DatabaseUnavailable());
        }
    }
}
