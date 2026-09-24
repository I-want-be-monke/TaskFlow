using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.DeleteProject;

public sealed class DeleteProjectHandler(
    ICurrentActor currentActor,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(
        DeleteProjectCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = DeleteProjectValidator.Validate(command);
        if (validation.IsFailure)
        {
            return validation;
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure(ProjectErrors.Unauthenticated());
        }

        Project? project = await projectRepository.GetOwnedByIdAsync(
            currentActor.UserId,
            command.ProjectId,
            cancellationToken);

        if (project is null)
        {
            return Result.Failure(ProjectErrors.NotFound());
        }

        if (project.Version != command.Version)
        {
            return Result.Failure(ProjectErrors.VersionConflict(command.Version, project.Version));
        }

        projectRepository.Remove(project);
        Result saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult;
        }

        return Result.Success();
    }
}
