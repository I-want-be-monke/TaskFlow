using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;

namespace TaskFlow.Application.Projects.ListProjects;

public sealed class ListProjectsHandler(
    ICurrentActor currentActor,
    IProjectQueries projectQueries)
{
    public async Task<Result<PagedResult<ProjectReadModel>>> HandleAsync(
        ListProjectsQuery query,
        CancellationToken cancellationToken)
    {
        Result validation = ListProjectsValidator.Validate(query);
        if (validation.IsFailure)
        {
            return Result.Failure<PagedResult<ProjectReadModel>>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<PagedResult<ProjectReadModel>>(ProjectErrors.Unauthenticated());
        }

        PagedResult<ProjectReadModel> projects = await projectQueries.ListOwnedAsync(
            currentActor.UserId,
            query.ToPagination(),
            cancellationToken);

        return Result.Success(projects);
    }
}
