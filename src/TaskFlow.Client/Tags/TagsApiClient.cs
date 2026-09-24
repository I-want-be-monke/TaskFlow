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
