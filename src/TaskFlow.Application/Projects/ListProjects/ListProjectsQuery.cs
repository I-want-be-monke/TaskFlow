using TaskFlow.Application.Common.Pagination;

namespace TaskFlow.Application.Projects.ListProjects;

public sealed record ListProjectsQuery(int Page = 1, int PageSize = 50)
{
    public Pagination ToPagination() => new(Page, PageSize);
}
