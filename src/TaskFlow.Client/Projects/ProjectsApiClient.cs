using TaskFlow.Client.Http;
using TaskFlow.Contracts.Common;
using TaskFlow.Contracts.Projects;

namespace TaskFlow.Client.Projects;

public sealed class ProjectsApiClient(ApiHttpClient api)
{
    private readonly ApiHttpClient _api = api ?? throw new ArgumentNullException(nameof(api));

    public Task<PagedResponse<ProjectResponse>> ListAsync(
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        _api.GetAsync<PagedResponse<ProjectResponse>>(
            $"/api/v1/projects?page={page}&pageSize={pageSize}",
            cancellationToken);

    public Task<ProjectResponse> GetAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<ProjectResponse>($"/api/v1/projects/{projectId:D}", cancellationToken);

    public Task<ProjectResponse> CreateAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default) =>
        _api.PostAsync<CreateProjectRequest, ProjectResponse>(
            "/api/v1/projects",
            new CreateProjectRequest(name, description),
            cancellationToken);

    public Task<ProjectResponse> UpdateAsync(
        Guid projectId,
        string name,
        string? description,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.PutAsync<UpdateProjectRequest, ProjectResponse>(
            $"/api/v1/projects/{projectId:D}",
            new UpdateProjectRequest(name, description, version),
            cancellationToken);

    public Task<ProjectResponse> ArchiveAsync(
        Guid projectId,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.PostAsync<VersionRequest, ProjectResponse>(
            $"/api/v1/projects/{projectId:D}/archive",
            new VersionRequest(version),
            cancellationToken);

    public Task<ProjectResponse> RestoreAsync(
        Guid projectId,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.PostAsync<VersionRequest, ProjectResponse>(
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
