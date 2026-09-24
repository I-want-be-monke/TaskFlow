using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks.CreateTask;

public sealed class CreateTaskHandler(
    ICurrentActor currentActor,
    IProjectRepository projectRepository,
    ITaskRepository taskRepository,
    IUnitOfWork unitOfWork,
    ITransactionManager transactionManager,
    TimeProvider timeProvider)
{
    public Task<Result<TaskReadModel>> HandleAsync(
        CreateTaskCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = CreateTaskValidator.Validate(command);
        if (validation.IsFailure)
        {
            return Task.FromResult(Result.Failure<TaskReadModel>(validation.Error!));
        }

        if (!currentActor.IsAuthenticated)
        {
            return Task.FromResult(Result.Failure<TaskReadModel>(TaskErrors.Unauthenticated()));
        }

        return transactionManager.ExecuteAsync(
            async transactionCancellationToken =>
            {
                Project? project = await projectRepository.GetOwnedForUpdateAsync(
                    currentActor.UserId,
                    command.ProjectId,
                    transactionCancellationToken);

                if (project is null)
                {
                    return Result.Failure<TaskReadModel>(TaskErrors.ProjectNotFound());
                }

                if (project.Status == ProjectStatus.Archived)
                {
                    return Result.Failure<TaskReadModel>(TaskErrors.ProjectArchived());
                }

                TaskItem task = TaskItem.Create(
                    Guid.NewGuid(),
                    project.Id,
                    command.Title,
                    command.Description,
                    command.Status,
                    command.Priority,
                    command.DueAt,
                    timeProvider.GetUtcNow());

                await taskRepository.AddAsync(task, transactionCancellationToken);
                Result saveResult = await unitOfWork.SaveChangesAsync(transactionCancellationToken);
                if (saveResult.IsFailure)
                {
                    return Result.Failure<TaskReadModel>(saveResult.Error!);
                }

                return Result.Success(task.ToReadModel());
            },
            cancellationToken);
    }
}
