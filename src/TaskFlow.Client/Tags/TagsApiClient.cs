using TaskFlow.Client.Common;
using TaskFlow.Client.Http;

namespace TaskFlow.Client.Tags;

public sealed class TagsApiClient(ApiHttpClient api)
{
    private readonly ApiHttpClient _api = api ?? throw new ArgumentNullException(nameof(api));

    public Task<PagedResponse<TagDto>> ListAsync(
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        _api.GetAsync<PagedResponse<TagDto>>(
            $"/api/v1/tags?page={page}&pageSize={pageSize}",
            cancellationToken);

    public async Task<IReadOnlyList<TagDto>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        const int pageSize = 100;
        PagedResponse<TagDto> first = await ListAsync(1, pageSize, cancellationToken);
        if (first.TotalPages <= 1)
        {
            return first.Items;
        }

        var items = new List<TagDto>(first.Items);
        for (int page = 2; page <= first.TotalPages; page++)
        {
            PagedResponse<TagDto> next = await ListAsync(page, pageSize, cancellationToken);
            items.AddRange(next.Items);
        }

        return items;
    }

    public Task<TagDto> GetAsync(Guid tagId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<TagDto>($"/api/v1/tags/{tagId:D}", cancellationToken);

    public Task<TagDto> CreateAsync(string name, CancellationToken cancellationToken = default) =>
        _api.PostAsync<CreateTagRequest, TagDto>(
            "/api/v1/tags",
            new CreateTagRequest(name),
            cancellationToken);

    public Task<TagDto> UpdateAsync(
        Guid tagId,
        string name,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.PutAsync<UpdateTagRequest, TagDto>(
            $"/api/v1/tags/{tagId:D}",
            new UpdateTagRequest(name, version),
            cancellationToken);

    public Task DeleteAsync(
        Guid tagId,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.DeleteAsync(
            $"/api/v1/tags/{tagId:D}?version={version}",
            cancellationToken);
}
