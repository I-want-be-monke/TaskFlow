using TaskFlow.Application.Common.Results;

namespace TaskFlow.Application.Common.Abstractions;

public interface IUnitOfWork
{
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);
}
