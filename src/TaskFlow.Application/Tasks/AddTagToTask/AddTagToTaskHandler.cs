using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks.AddTagToTask;

public sealed class AddTagToTaskHandler(
    ICurrentActor currentActor,
    IProjectRepository projectRepository,
    ITaskRepository taskRepository,
    ITagRepository tagRepository,
    IUnitOfWork unitOfWork,
    ITransactionManager transactionManager,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(
        AddTagToTaskCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = AddTagToTaskValidator.Validate(command);
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

                Tag? tag = await tagRepository.GetOwnedForUpdateAsync(
                    currentActor.UserId,
                    command.TagId,
                    transactionCancellationToken);

                if (tag is null)
                {
                    return Result.Failure(TaskErrors.TagNotFound());
                }

                TaskTag? existingRelation = await taskRepository.GetTagRelationAsync(
                    currentActor.UserId,
                    task.Id,
                    tag.Id,
                    transactionCancellationToken);

                if (existingRelation is not null)
                {
                    return Result.Success();
                }

                TaskTag relation = TaskTag.Create(task.Id, tag.Id, timeProvider.GetUtcNow());
                await taskRepository.AddTagAsync(relation, transactionCancellationToken);
                await unitOfWork.SaveChangesAsync(transactionCancellationToken);
                return Result.Success();
            },
            cancellationToken);
    }
}
