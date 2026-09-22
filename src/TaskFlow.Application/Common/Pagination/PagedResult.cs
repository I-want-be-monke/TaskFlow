namespace TaskFlow.Application.Common.Pagination;

public sealed record PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, long totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, Pagination.MaximumPageSize);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);

        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items { get; }

    public int Page { get; }

    public int PageSize { get; }

    public long TotalCount { get; }

    public long TotalPages => TotalCount == 0
        ? 0
        : (TotalCount / PageSize) + (TotalCount % PageSize == 0 ? 0 : 1);
}
