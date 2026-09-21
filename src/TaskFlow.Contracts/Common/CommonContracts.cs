namespace TaskFlow.Contracts.Common;

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount,
    long TotalPages);

public sealed record VersionRequest(long Version);
