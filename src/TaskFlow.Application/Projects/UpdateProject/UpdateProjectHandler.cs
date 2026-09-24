using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.UpdateProject;

public sealed class UpdateProjectHandler(
    ICurrentActor currentActor,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<ProjectReadModel>> HandleAsync(
        UpdateProjectCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = UpdateProjectValidator.Validate(command);
        if (validation.IsFailure)
        {
            return Result.Failure<ProjectReadModel>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<ProjectReadModel>(ProjectErrors.Unauthenticated());
        }

        Project? project = await projectRepository.GetOwnedByIdAsync(
            currentActor.UserId,
            command.ProjectId,
            cancellationToken);

        if (project is null)
        {
            return Result.Failure<ProjectReadModel>(ProjectErrors.NotFound());
        }

        if (project.Version != command.Version)
        {
            return Result.Failure<ProjectReadModel>(
                ProjectErrors.VersionConflict(command.Version, project.Version));
        }

        project.UpdateDetails(command.Name, command.Description, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(project.ToReadModel());
    }
}
