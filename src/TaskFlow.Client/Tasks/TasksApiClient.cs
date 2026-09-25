using System.Globalization;
using TaskFlow.Client.Common;
using TaskFlow.Client.Http;

namespace TaskFlow.Client.Tasks;

public sealed class TasksApiClient(ApiHttpClient api)
{
    private readonly ApiHttpClient _api = api ?? throw new ArgumentNullException(nameof(api));

    public Task<PagedResponse<TaskDto>> ListAsync(
        TaskListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _api.GetAsync<PagedResponse<TaskDto>>(BuildListUri(request), cancellationToken);
    }

    public Task<TaskDto> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<TaskDto>($"/api/v1/tasks/{taskId:D}", cancellationToken);

    public Task<TaskDto> CreateAsync(
        Guid projectId,
        CreateTaskRequest request,
        CancellationToken cancellationToken = default) =>
        _api.PostAsync<CreateTaskRequest, TaskDto>(
            $"/api/v1/projects/{projectId:D}/tasks",
            request,
            cancellationToken);

    public Task<TaskDto> UpdateAsync(
        Guid taskId,
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default) =>
        _api.PutAsync<UpdateTaskRequest, TaskDto>(
            $"/api/v1/tasks/{taskId:D}",
            request,
            cancellationToken);

    public Task DeleteAsync(
        Guid taskId,
        long version,
        CancellationToken cancellationToken = default) =>
        _api.DeleteAsync($"/api/v1/tasks/{taskId:D}?version={version}", cancellationToken);

    public Task AddTagAsync(
        Guid taskId,
        Guid tagId,
        CancellationToken cancellationToken = default) =>
        _api.PutAsync($"/api/v1/tasks/{taskId:D}/tags/{tagId:D}", cancellationToken);

    public Task RemoveTagAsync(
        Guid taskId,
        Guid tagId,
        CancellationToken cancellationToken = default) =>
        _api.DeleteAsync($"/api/v1/tasks/{taskId:D}/tags/{tagId:D}", cancellationToken);

    private static string BuildListUri(TaskListRequest request)
    {
        var query = new List<string>
        {
            $"page={request.Page}",
            $"pageSize={request.PageSize}",
            $"sort={Escape(request.Sort)}",
        };

        Add(query, "projectId", request.ProjectId?.ToString("D"));
        Add(query, "status", request.Status);
        Add(query, "priority", request.Priority);
        Add(query, "tagId", request.TagId?.ToString("D"));
        Add(query, "dueBefore", request.DueBefore?.ToString("O", CultureInfo.InvariantCulture));
        Add(query, "dueAfter", request.DueAfter?.ToString("O", CultureInfo.InvariantCulture));
        Add(query, "q", request.SearchText);

        return "/api/v1/tasks?" + string.Join('&', query);
    }

    private static void Add(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{name}={Escape(value)}");
        }
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
