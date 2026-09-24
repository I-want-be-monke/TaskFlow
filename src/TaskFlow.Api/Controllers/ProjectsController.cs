using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Contracts.Common;
using TaskFlow.Api.Contracts.Projects;
using TaskFlow.Api.Errors;
using TaskFlow.Api.Observability;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Projects.ArchiveProject;
using TaskFlow.Application.Projects.CreateProject;
using TaskFlow.Application.Projects.DeleteProject;
using TaskFlow.Application.Projects.GetProject;
using TaskFlow.Application.Projects.ListProjects;
using TaskFlow.Application.Projects.RestoreProject;
using TaskFlow.Application.Projects.UpdateProject;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1/projects")]
public sealed class ProjectsController(
    CreateProjectHandler createHandler,
    GetProjectHandler getHandler,
    ListProjectsHandler listHandler,
    UpdateProjectHandler updateHandler,
    ArchiveProjectHandler archiveHandler,
    RestoreProjectHandler restoreHandler,
    DeleteProjectHandler deleteHandler,
    ApplicationEventLogger applicationEvents) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ProjectResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ProjectResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<ProjectReadModel>> result = await listHandler.HandleAsync(
            new ListProjectsQuery(page, pageSize),
            cancellationToken);

        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        return Ok(PagedResponse<ProjectResponse>.From(result.Value, static project => project.ToResponse()));
    }

    [HttpGet("{projectId:guid}", Name = RouteNames.GetProject)]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectResponse>> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        Result<ProjectReadModel> result = await getHandler.HandleAsync(
            new GetProjectQuery(projectId),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : Ok(result.Value.ToResponse());
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProjectResponse>> Create(
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        Result<ProjectReadModel> result = await createHandler.HandleAsync(
            new CreateProjectCommand(request.Name, request.Description),
            cancellationToken);

        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        ProjectResponse response = result.Value.ToResponse();
        applicationEvents.ProjectCreated(response.Id);
        return CreatedAtRoute(RouteNames.GetProject, new { projectId = response.Id }, response);
    }

    [HttpPut("{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectResponse>> Update(
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        Result<ProjectReadModel> result = await updateHandler.HandleAsync(
            new UpdateProjectCommand(projectId, request.Name, request.Description, request.Version),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : Ok(result.Value.ToResponse());
    }

    [HttpPost("{projectId:guid}/archive")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectResponse>> Archive(
        Guid projectId,
        VersionRequest request,
        CancellationToken cancellationToken)
    {
        Result<ProjectReadModel> result = await archiveHandler.HandleAsync(
            new ArchiveProjectCommand(projectId, request.Version),
            cancellationToken);

        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        applicationEvents.ProjectArchived(projectId);
        return Ok(result.Value.ToResponse());
    }

    [HttpPost("{projectId:guid}/restore")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectResponse>> Restore(
        Guid projectId,
        VersionRequest request,
        CancellationToken cancellationToken)
    {
        Result<ProjectReadModel> result = await restoreHandler.HandleAsync(
            new RestoreProjectCommand(projectId, request.Version),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : Ok(result.Value.ToResponse());
    }

    [HttpDelete("{projectId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid projectId,
        [FromQuery] long version,
        CancellationToken cancellationToken)
    {
        Result result = await deleteHandler.HandleAsync(
            new DeleteProjectCommand(projectId, version),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : NoContent();
    }
}
