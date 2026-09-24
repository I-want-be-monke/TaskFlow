using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.CreateProject;

public sealed class CreateProjectHandler(
    ICurrentActor currentActor,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<ProjectReadModel>> HandleAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = CreateProjectValidator.Validate(command);
        if (validation.IsFailure)
        {
            return Result.Failure<ProjectReadModel>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<ProjectReadModel>(ProjectErrors.Unauthenticated());
        }

        Project project = Project.Create(
            Guid.NewGuid(),
            currentActor.UserId,
            command.Name,
            command.Description,
            timeProvider.GetUtcNow());

        await projectRepository.AddAsync(project, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(project.ToReadModel());
    }
}
