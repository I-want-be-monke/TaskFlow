using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.RestoreProject;

public sealed class RestoreProjectHandler(
    ICurrentActor currentActor,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork,
    ITransactionManager transactionManager,
    TimeProvider timeProvider)
{
    public Task<Result<ProjectReadModel>> HandleAsync(
        RestoreProjectCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = RestoreProjectValidator.Validate(command);
        if (validation.IsFailure)
        {
            return Task.FromResult(Result.Failure<ProjectReadModel>(validation.Error!));
        }

        if (!currentActor.IsAuthenticated)
        {
            return Task.FromResult(Result.Failure<ProjectReadModel>(ProjectErrors.Unauthenticated()));
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
                    return Result.Failure<ProjectReadModel>(ProjectErrors.NotFound());
                }

                if (project.Version != command.Version)
                {
                    return Result.Failure<ProjectReadModel>(
                        ProjectErrors.VersionConflict(command.Version, project.Version));
                }

                if (project.Status == ProjectStatus.Active)
                {
                    return Result.Failure<ProjectReadModel>(ProjectErrors.AlreadyActive());
                }

                project.Restore(timeProvider.GetUtcNow());
                Result saveResult = await unitOfWork.SaveChangesAsync(transactionCancellationToken);
                if (saveResult.IsFailure)
                {
                    return Result.Failure<ProjectReadModel>(saveResult.Error!);
                }

                return Result.Success(project.ToReadModel());
            },
            cancellationToken);
    }
}
