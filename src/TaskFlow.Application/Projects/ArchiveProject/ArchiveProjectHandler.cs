using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.ArchiveProject;

public sealed class ArchiveProjectHandler(
    ICurrentActor currentActor,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork,
    ITransactionManager transactionManager,
    TimeProvider timeProvider)
{
    public Task<Result<ProjectReadModel>> HandleAsync(
        ArchiveProjectCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = ArchiveProjectValidator.Validate(command);
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

                if (project.Status == ProjectStatus.Archived)
                {
                    return Result.Failure<ProjectReadModel>(ProjectErrors.AlreadyArchived());
                }

                project.Archive(timeProvider.GetUtcNow());
                await unitOfWork.SaveChangesAsync(transactionCancellationToken);

                return Result.Success(project.ToReadModel());
            },
            cancellationToken);
    }
}
