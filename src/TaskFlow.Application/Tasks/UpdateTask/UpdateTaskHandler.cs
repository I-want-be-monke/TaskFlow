using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks.UpdateTask;

public sealed class UpdateTaskHandler(
    ICurrentActor currentActor,
    IProjectRepository projectRepository,
    ITaskRepository taskRepository,
    IUnitOfWork unitOfWork,
    ITransactionManager transactionManager,
    TimeProvider timeProvider)
{
    public async Task<Result<TaskReadModel>> HandleAsync(
        UpdateTaskCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = UpdateTaskValidator.Validate(command);
        if (validation.IsFailure)
        {
            return Result.Failure<TaskReadModel>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<TaskReadModel>(TaskErrors.Unauthenticated());
        }

        TaskItem? existing = await taskRepository.GetOwnedByIdAsync(
            currentActor.UserId,
            command.TaskId,
            cancellationToken);

        if (existing is null)
        {
            return Result.Failure<TaskReadModel>(TaskErrors.NotFound());
        }

        return await transactionManager.ExecuteAsync(
            async transactionCancellationToken =>
            {
                Project? project = await projectRepository.GetOwnedForUpdateAsync(
                    currentActor.UserId,
                    existing.ProjectId,
                    transactionCancellationToken);

                if (project is null)
                {
                    return Result.Failure<TaskReadModel>(TaskErrors.NotFound());
                }

                if (project.Status == ProjectStatus.Archived)
                {
                    return Result.Failure<TaskReadModel>(TaskErrors.ProjectArchived());
                }

                TaskItem? task = await taskRepository.GetOwnedForUpdateAsync(
                    currentActor.UserId,
                    command.TaskId,
                    transactionCancellationToken);

                if (task is null || task.ProjectId != project.Id)
                {
                    return Result.Failure<TaskReadModel>(TaskErrors.NotFound());
                }

                if (task.Version != command.Version)
                {
                    return Result.Failure<TaskReadModel>(
                        TaskErrors.VersionConflict(command.Version, task.Version));
                }

                task.Update(
                    command.Title,
                    command.Description,
                    command.Status,
                    command.Priority,
                    command.DueAt,
                    timeProvider.GetUtcNow());

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
