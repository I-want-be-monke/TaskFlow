using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Contracts.Common;
using TaskFlow.Api.Contracts.Tasks;
using TaskFlow.Api.Errors;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks;
using TaskFlow.Application.Tasks.AddTagToTask;
using TaskFlow.Application.Tasks.CreateTask;
using TaskFlow.Application.Tasks.DeleteTask;
using TaskFlow.Application.Tasks.GetTask;
using TaskFlow.Application.Tasks.ListTasks;
using TaskFlow.Application.Tasks.RemoveTagFromTask;
using TaskFlow.Application.Tasks.UpdateTask;
using TaskFlow.Domain.Tasks;
using DomainTaskStatus = TaskFlow.Domain.Tasks.TaskStatus;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class TasksController(
    CreateTaskHandler createHandler,
    GetTaskHandler getHandler,
    ListTasksHandler listHandler,
    UpdateTaskHandler updateHandler,
    DeleteTaskHandler deleteHandler,
    AddTagToTaskHandler addTagHandler,
    RemoveTagFromTaskHandler removeTagHandler) : ControllerBase
{
    [HttpGet("tasks")]
    [ProducesResponseType(typeof(PagedResponse<TaskResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<TaskResponse>>> List(
        [FromQuery] Guid? projectId,
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] Guid? tagId,
        [FromQuery] DateTimeOffset? dueBefore,
        [FromQuery] DateTimeOffset? dueAfter,
        [FromQuery(Name = "q")] string? searchText,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sort = TaskSortOptions.CreatedAtDescending,
        CancellationToken cancellationToken = default)
    {
        Result<DomainTaskStatus?> statusResult = TaskContractMapping.ParseOptionalStatus(status);
        if (statusResult.IsFailure)
        {
            return this.ToProblem(statusResult.Error!);
        }

        Result<TaskPriority?> priorityResult = TaskContractMapping.ParseOptionalPriority(priority);
        if (priorityResult.IsFailure)
        {
            return this.ToProblem(priorityResult.Error!);
        }

        var search = new TaskSearchQuery(
            projectId,
            statusResult.Value,
            priorityResult.Value,
            tagId,
            dueBefore,
            dueAfter,
            searchText,
            page,
            pageSize,
            sort);

        Result<PagedResult<TaskReadModel>> result = await listHandler.HandleAsync(
            new ListTasksQuery(search),
            cancellationToken);

        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        return Ok(PagedResponse<TaskResponse>.From(result.Value, static task => task.ToResponse()));
    }

    [HttpGet("tasks/{taskId:guid}", Name = RouteNames.GetTask)]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskResponse>> Get(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        Result<TaskReadModel> result = await getHandler.HandleAsync(
            new GetTaskQuery(taskId),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : Ok(result.Value.ToResponse());
    }

    [HttpPost("projects/{projectId:guid}/tasks")]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<TaskResponse>> Create(
        Guid projectId,
        CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        Result<(DomainTaskStatus Status, TaskPriority Priority)> state =
            TaskContractMapping.ParseState(request.Status, request.Priority);

        if (state.IsFailure)
        {
            return this.ToProblem(state.Error!);
        }

        Result<TaskReadModel> result = await createHandler.HandleAsync(
            new CreateTaskCommand(
                projectId,
                request.Title,
                request.Description,
                state.Value.Status,
                state.Value.Priority,
                request.DueAt),
            cancellationToken);

        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        TaskResponse response = result.Value.ToResponse();
        return CreatedAtRoute(RouteNames.GetTask, new { taskId = response.Id }, response);
    }

    [HttpPut("tasks/{taskId:guid}")]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TaskResponse>> Update(
        Guid taskId,
        UpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        Result<(DomainTaskStatus Status, TaskPriority Priority)> state =
            TaskContractMapping.ParseState(request.Status, request.Priority);

        if (state.IsFailure)
        {
            return this.ToProblem(state.Error!);
        }

        Result<TaskReadModel> result = await updateHandler.HandleAsync(
            new UpdateTaskCommand(
                taskId,
                request.Title,
                request.Description,
                state.Value.Status,
                state.Value.Priority,
                request.DueAt,
                request.Version),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : Ok(result.Value.ToResponse());
    }

    [HttpDelete("tasks/{taskId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid taskId,
        [FromQuery] long version,
        CancellationToken cancellationToken)
    {
        Result result = await deleteHandler.HandleAsync(
            new DeleteTaskCommand(taskId, version),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : NoContent();
    }

    [HttpPut("tasks/{taskId:guid}/tags/{tagId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AddTag(
        Guid taskId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        Result result = await addTagHandler.HandleAsync(
            new AddTagToTaskCommand(taskId, tagId),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : NoContent();
    }

    [HttpDelete("tasks/{taskId:guid}/tags/{tagId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveTag(
        Guid taskId,
        Guid tagId,
        CancellationToken cancellationToken)
    {
        Result result = await removeTagHandler.HandleAsync(
            new RemoveTagFromTaskCommand(taskId, tagId),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : NoContent();
    }
}
