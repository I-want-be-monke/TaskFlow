using TaskFlow.Client.Common;
using TaskFlow.Client.Http;

namespace TaskFlow.Client.Projects;

public sealed class ProjectsApiClient(ApiHttpClient api)
{
    private readonly ApiHttpClient _api = api ?? throw new ArgumentNullException(nameof(api));

    public Task<PagedResponse<ProjectDto>> ListAsync(
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        _api.GetAsync<PagedResponse<ProjectDto>>(
            $"/api/v1/projects?page={page}&pageSize={pageSize}",
            cancellationToken);

    public Task<ProjectDto> GetAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<ProjectDto>($"/api/v1/projects/{projectId:D}", cancellationToken);

    public Task<ProjectDto> CreateAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default) =>
        _api.PostAsync<CreateProjectRequest, ProjectDto>(
            "/api/v1/projects",
            new CreateProjectRequest(name, description),
            cancellationToken);

    public Task<ProjectDto> UpdateAsync(
        Guid projectId,
        string name,
        string? description,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.PutAsync<UpdateProjectRequest, ProjectDto>(
            $"/api/v1/projects/{projectId:D}",
            new UpdateProjectRequest(name, description, version),
            cancellationToken);

    public Task<ProjectDto> ArchiveAsync(
        Guid projectId,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.PostAsync<VersionRequest, ProjectDto>(
            $"/api/v1/projects/{projectId:D}/archive",
            new VersionRequest(version),
            cancellationToken);

    public Task<ProjectDto> RestoreAsync(
        Guid projectId,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.PostAsync<VersionRequest, ProjectDto>(
            $"/api/v1/projects/{projectId:D}/restore",
            new VersionRequest(version),
            cancellationToken);

    public Task DeleteAsync(
        Guid projectId,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.DeleteAsync(
            $"/api/v1/projects/{projectId:D}?version={version}",
            cancellationToken);
}
