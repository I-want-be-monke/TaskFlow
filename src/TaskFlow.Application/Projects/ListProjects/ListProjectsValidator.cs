using TaskFlow.Application.Common.Results;

namespace TaskFlow.Application.Projects.ListProjects;

public static class ListProjectsValidator
{
    public static Result Validate(ListProjectsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.ToPagination().Validate();
    }
}
