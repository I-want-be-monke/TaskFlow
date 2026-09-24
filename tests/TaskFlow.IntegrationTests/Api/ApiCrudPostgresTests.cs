using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Contracts.Common;
using TaskFlow.Api.Contracts.Projects;
using TaskFlow.Api.Contracts.Tags;
using TaskFlow.Api.Contracts.Tasks;
using TaskFlow.Api.Controllers;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Projects.ArchiveProject;
using TaskFlow.Application.Projects.CreateProject;
using TaskFlow.Application.Projects.DeleteProject;
using TaskFlow.Application.Projects.GetProject;
using TaskFlow.Application.Projects.ListProjects;
using TaskFlow.Application.Projects.RestoreProject;
using TaskFlow.Application.Projects.UpdateProject;
using TaskFlow.Application.Tags.CreateTag;
using TaskFlow.Application.Tags.DeleteTag;
using TaskFlow.Application.Tags.GetTag;
using TaskFlow.Application.Tags.ListTags;
using TaskFlow.Application.Tags.UpdateTag;
using TaskFlow.Application.Tasks.AddTagToTask;
using TaskFlow.Application.Tasks.CreateTask;
using TaskFlow.Application.Tasks.DeleteTask;
using TaskFlow.Application.Tasks.GetTask;
using TaskFlow.Application.Tasks.ListTasks;
using TaskFlow.Application.Tasks.RemoveTagFromTask;
using TaskFlow.Application.Tasks.UpdateTask;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Persistence.Queries;
using TaskFlow.Infrastructure.Persistence.Transactions;
using TaskFlow.Infrastructure.Repositories;
using TaskFlow.IntegrationTests.Persistence;

namespace TaskFlow.IntegrationTests.Api;

public sealed class ApiCrudPostgresTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Controllers_ExecuteFullCrudFlowAgainstPostgres()
    {
        Guid ownerId = Guid.NewGuid();
        await PersistenceTestData.SeedUserAsync(fixture, ownerId, TestContext.Current.CancellationToken);

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        ControllerSet controllers = CreateControllers(dbContext, ownerId);

        ActionResult<ProjectResponse> createProjectAction = await controllers.Projects.Create(
            new CreateProjectRequest("Stage 7", "API project"),
            TestContext.Current.CancellationToken);
        CreatedAtRouteResult projectCreated = Assert.IsType<CreatedAtRouteResult>(createProjectAction.Result);
        ProjectResponse project = Assert.IsType<ProjectResponse>(projectCreated.Value);
        Assert.Equal(TaskFlow.Api.RouteNames.GetProject, projectCreated.RouteName);
        Assert.Equal(1, project.Version);

        ActionResult<ProjectResponse> updateProjectAction = await controllers.Projects.Update(
            project.Id,
            new UpdateProjectRequest("Stage 7 updated", "API project updated", project.Version),
            TestContext.Current.CancellationToken);
        ProjectResponse updatedProject = AssertOk<ProjectResponse>(updateProjectAction);
        Assert.Equal(2, updatedProject.Version);

        ActionResult<TagResponse> createTagAction = await controllers.Tags.Create(
            new CreateTagRequest("Backend"),
            TestContext.Current.CancellationToken);
        CreatedAtRouteResult tagCreated = Assert.IsType<CreatedAtRouteResult>(createTagAction.Result);
        TagResponse tag = Assert.IsType<TagResponse>(tagCreated.Value);
        Assert.Equal(TaskFlow.Api.RouteNames.GetTag, tagCreated.RouteName);

        ActionResult<TaskResponse> createTaskAction = await controllers.Tasks.Create(
            project.Id,
            new CreateTaskRequest("HTTP contract", "Wire API", "Todo", "High", null),
            TestContext.Current.CancellationToken);
        CreatedAtRouteResult taskCreated = Assert.IsType<CreatedAtRouteResult>(createTaskAction.Result);
        TaskResponse task = Assert.IsType<TaskResponse>(taskCreated.Value);
        Assert.Equal(TaskFlow.Api.RouteNames.GetTask, taskCreated.RouteName);
        Assert.Equal("Todo", task.Status);
        Assert.Equal("High", task.Priority);

        IActionResult addTagResult = await controllers.Tasks.AddTag(
            task.Id,
            tag.Id,
            TestContext.Current.CancellationToken);
        Assert.IsType<NoContentResult>(addTagResult);

        ActionResult<TaskResponse> updateTaskAction = await controllers.Tasks.Update(
            task.Id,
            new UpdateTaskRequest("HTTP contract updated", "Wire API", "InProgress", "Medium", null, task.Version),
            TestContext.Current.CancellationToken);
        TaskResponse updatedTask = AssertOk<TaskResponse>(updateTaskAction);
        Assert.Equal(2, updatedTask.Version);
        Assert.Equal("InProgress", updatedTask.Status);

        ActionResult<PagedResponse<TaskResponse>> listTasksAction = await controllers.Tasks.List(
            project.Id,
            "InProgress",
            "Medium",
            tag.Id,
            null,
            null,
            "HTTP",
            1,
            50,
            TaskFlow.Application.Tasks.TaskSortOptions.CreatedAtDescending,
            TestContext.Current.CancellationToken);
        PagedResponse<TaskResponse> tasks = AssertOk<PagedResponse<TaskResponse>>(listTasksAction);
        Assert.Contains(tasks.Items, item => item.Id == task.Id);

        ActionResult<ProjectResponse> archiveAction = await controllers.Projects.Archive(
            project.Id,
            new VersionRequest(updatedProject.Version),
            TestContext.Current.CancellationToken);
        ProjectResponse archived = AssertOk<ProjectResponse>(archiveAction);
        Assert.Equal("Archived", archived.Status);
        Assert.Equal(3, archived.Version);

        ActionResult<TaskResponse> blockedUpdate = await controllers.Tasks.Update(
            task.Id,
            new UpdateTaskRequest("Blocked", null, "Done", "Low", null, updatedTask.Version),
            TestContext.Current.CancellationToken);
        ObjectResult blockedProblemResult = Assert.IsType<ObjectResult>(blockedUpdate.Result);
        Assert.Equal(StatusCodes.Status409Conflict, blockedProblemResult.StatusCode.GetValueOrDefault());
        ProblemDetails blockedProblem = Assert.IsType<ProblemDetails>(blockedProblemResult.Value);
        Assert.Equal("tasks.project_archived", blockedProblem.Extensions["code"]);

        ActionResult<ProjectResponse> restoreAction = await controllers.Projects.Restore(
            project.Id,
            new VersionRequest(archived.Version),
            TestContext.Current.CancellationToken);
        ProjectResponse restored = AssertOk<ProjectResponse>(restoreAction);
        Assert.Equal("Active", restored.Status);
        Assert.Equal(4, restored.Version);

        IActionResult removeTagResult = await controllers.Tasks.RemoveTag(
            task.Id,
            tag.Id,
            TestContext.Current.CancellationToken);
        Assert.IsType<NoContentResult>(removeTagResult);

        IActionResult deleteTaskResult = await controllers.Tasks.Delete(
            task.Id,
            updatedTask.Version,
            TestContext.Current.CancellationToken);
        Assert.IsType<NoContentResult>(deleteTaskResult);

        IActionResult deleteTagResult = await controllers.Tags.Delete(
            tag.Id,
            tag.Version,
            TestContext.Current.CancellationToken);
        Assert.IsType<NoContentResult>(deleteTagResult);

        IActionResult deleteProjectResult = await controllers.Projects.Delete(
            project.Id,
            restored.Version,
            TestContext.Current.CancellationToken);
        Assert.IsType<NoContentResult>(deleteProjectResult);
    }

    [Fact]
    public async Task ForeignOwnedObject_ReturnsNotFoundProblem()
    {
        Guid ownerId = Guid.NewGuid();
        Guid foreignOwnerId = Guid.NewGuid();
        (TaskFlow.Domain.Projects.Project project, _, _) = await PersistenceTestData.SeedOwnedGraphAsync(
            fixture,
            ownerId,
            cancellationToken: TestContext.Current.CancellationToken);
        await PersistenceTestData.SeedUserAsync(fixture, foreignOwnerId, TestContext.Current.CancellationToken);

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        ControllerSet controllers = CreateControllers(dbContext, foreignOwnerId);

        ActionResult<ProjectResponse> action = await controllers.Projects.Get(
            project.Id,
            TestContext.Current.CancellationToken);

        ObjectResult result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode.GetValueOrDefault());
        ProblemDetails problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("projects.not_found", problem.Extensions["code"]);
    }

    [Fact]
    public async Task StaleVersion_ReturnsConflictProblem()
    {
        Guid ownerId = Guid.NewGuid();
        (TaskFlow.Domain.Projects.Project project, _, _) = await PersistenceTestData.SeedOwnedGraphAsync(
            fixture,
            ownerId,
            cancellationToken: TestContext.Current.CancellationToken);

        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        ControllerSet controllers = CreateControllers(dbContext, ownerId);

        ActionResult<ProjectResponse> action = await controllers.Projects.Update(
            project.Id,
            new UpdateProjectRequest("Stale", null, project.Version + 100),
            TestContext.Current.CancellationToken);

        ObjectResult result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode.GetValueOrDefault());
        ProblemDetails problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("projects.version_conflict", problem.Extensions["code"]);
    }

    private static ControllerSet CreateControllers(TaskFlowDbContext dbContext, Guid ownerId)
    {
        TestActor actor = new(ownerId);
        FixedTimeProvider timeProvider = new(PersistenceTestData.Now.AddHours(7));
        ProjectRepository projects = new(dbContext);
        TaskRepository tasks = new(dbContext);
        TagRepository tags = new(dbContext);
        ProjectQueries projectQueries = new(dbContext);
        TaskQueries taskQueries = new(dbContext);
        TagQueries tagQueries = new(dbContext);
        UnitOfWork unitOfWork = new(dbContext);
        EfTransactionManager transactions = new(dbContext);

        ProjectsController projectsController = AttachHttpContext(new ProjectsController(
            new CreateProjectHandler(actor, projects, unitOfWork, timeProvider),
            new GetProjectHandler(actor, projectQueries),
            new ListProjectsHandler(actor, projectQueries),
            new UpdateProjectHandler(actor, projects, unitOfWork, timeProvider),
            new ArchiveProjectHandler(actor, projects, unitOfWork, transactions, timeProvider),
            new RestoreProjectHandler(actor, projects, unitOfWork, transactions, timeProvider),
            new DeleteProjectHandler(actor, projects, unitOfWork)));

        TasksController tasksController = AttachHttpContext(new TasksController(
            new CreateTaskHandler(actor, projects, tasks, unitOfWork, transactions, timeProvider),
            new GetTaskHandler(actor, taskQueries),
            new ListTasksHandler(actor, taskQueries),
            new UpdateTaskHandler(actor, projects, tasks, unitOfWork, transactions, timeProvider),
            new DeleteTaskHandler(actor, projects, tasks, unitOfWork, transactions),
            new AddTagToTaskHandler(actor, projects, tasks, tags, unitOfWork, transactions, timeProvider),
            new RemoveTagFromTaskHandler(actor, projects, tasks, tags, unitOfWork, transactions)));

        TagsController tagsController = AttachHttpContext(new TagsController(
            new CreateTagHandler(actor, tags, unitOfWork, timeProvider),
            new GetTagHandler(actor, tagQueries),
            new ListTagsHandler(actor, tagQueries),
            new UpdateTagHandler(actor, tags, unitOfWork, timeProvider),
            new DeleteTagHandler(actor, tags, unitOfWork)));

        return new ControllerSet(projectsController, tasksController, tagsController);
    }

    private static TController AttachHttpContext<TController>(TController controller)
        where TController : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    private static T AssertOk<T>(ActionResult<T> action)
    {
        OkObjectResult ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<T>(ok.Value);
    }

    private sealed record ControllerSet(
        ProjectsController Projects,
        TasksController Tasks,
        TagsController Tags);

    private sealed class TestActor(Guid userId) : ICurrentActor
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
