using TaskFlow.Application.Common.Pagination;
using TaskFlow.Contracts.Common;

namespace TaskFlow.Api.Contracts.Common;

internal static class PagedResponseFactory
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
