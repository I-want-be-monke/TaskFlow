using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;

namespace TaskFlow.Application.Projects.GetProject;

public sealed class GetProjectHandler(
    ICurrentActor currentActor,
    IProjectQueries projectQueries)
{
    public async Task<Result<ProjectReadModel>> HandleAsync(
        GetProjectQuery query,
        CancellationToken cancellationToken)
    {
        Result validation = GetProjectValidator.Validate(query);
        if (validation.IsFailure)
        {
            return Result.Failure<ProjectReadModel>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<ProjectReadModel>(ProjectErrors.Unauthenticated());
        }

        ProjectReadModel? project = await projectQueries.GetOwnedByIdAsync(
            currentActor.UserId,
            query.ProjectId,
            cancellationToken);

        return project is null
            ? Result.Failure<ProjectReadModel>(ProjectErrors.NotFound())
            : Result.Success(project);
    }
}
