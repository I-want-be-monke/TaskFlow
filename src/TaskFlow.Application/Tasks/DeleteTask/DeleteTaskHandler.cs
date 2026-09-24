using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks.DeleteTask;

public sealed class DeleteTaskHandler(
    ICurrentActor currentActor,
    IProjectRepository projectRepository,
    ITaskRepository taskRepository,
    IUnitOfWork unitOfWork,
    ITransactionManager transactionManager)
{
    public async Task<Result> HandleAsync(
        DeleteTaskCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = DeleteTaskValidator.Validate(command);
        if (validation.IsFailure)
        {
            return validation;
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure(TaskErrors.Unauthenticated());
        }

        TaskItem? existing = await taskRepository.GetOwnedByIdAsync(
            currentActor.UserId,
            command.TaskId,
            cancellationToken);

        if (existing is null)
        {
            return Result.Failure(TaskErrors.NotFound());
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
                    return Result.Failure(TaskErrors.NotFound());
                }

                if (project.Status == ProjectStatus.Archived)
                {
                    return Result.Failure(TaskErrors.ProjectArchived());
                }

                TaskItem? task = await taskRepository.GetOwnedForUpdateAsync(
                    currentActor.UserId,
                    command.TaskId,
                    transactionCancellationToken);

                if (task is null || task.ProjectId != project.Id)
                {
                    return Result.Failure(TaskErrors.NotFound());
                }

                if (task.Version != command.Version)
                {
                    return Result.Failure(TaskErrors.VersionConflict(command.Version, task.Version));
                }

                taskRepository.Remove(task);
                await unitOfWork.SaveChangesAsync(transactionCancellationToken);
                return Result.Success();
            },
            cancellationToken);
    }
}
