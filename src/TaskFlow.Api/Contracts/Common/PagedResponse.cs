using TaskFlow.Application.Common.Pagination;

namespace TaskFlow.Api.Contracts.Common;

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount,
    long TotalPages);

public static class PagedResponse
{
    public static PagedResponse<TResponse> From<TSource, TResponse>(
        PagedResult<TSource> source,
        Func<TSource, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);

        return new PagedResponse<TResponse>(
            source.Items.Select(map).ToArray(),
            source.Page,
            source.PageSize,
            source.TotalCount,
            source.TotalPages);
    }
}
