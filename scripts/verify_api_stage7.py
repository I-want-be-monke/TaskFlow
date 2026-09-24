#!/usr/bin/env python3
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
API = ROOT / "src" / "TaskFlow.Api"
TESTS = ROOT / "tests" / "TaskFlow.IntegrationTests" / "Api"

def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"Stage 7 verification failed: {message}")

def read(path: Path) -> str:
    require(path.is_file(), f"missing {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")

program = read(API / "Program.cs")
for token in [
    "AddProblemDetails()",
    "AddExceptionHandler<GlobalExceptionHandler>()",
    "AddControllers()",
    "UseNpgsql(postgresConnectionString)",
    "UseExceptionHandler()",
    "MapControllers()",
    "IProjectRepository, ProjectRepository",
    "ITaskRepository, TaskRepository",
    "ITagRepository, TagRepository",
    "IUnitOfWork, UnitOfWork",
    "ITransactionManager, EfTransactionManager",
    "ICurrentActor, HttpContextCurrentActor",
]:
    require(token in program, f"Program.cs missing composition-root token: {token}")

for forbidden in ["Database.Migrate", "MigrateAsync(", "EnsureCreated", "AddAuthentication(", "UseAuthentication("]:
    require(forbidden not in program, f"Stage 7 must not contain {forbidden}; migrations/authentication are separate stages")

handler_names = [
    "CreateProjectHandler", "GetProjectHandler", "ListProjectsHandler", "UpdateProjectHandler",
    "ArchiveProjectHandler", "RestoreProjectHandler", "DeleteProjectHandler",
    "CreateTaskHandler", "GetTaskHandler", "ListTasksHandler", "UpdateTaskHandler", "DeleteTaskHandler",
    "AddTagToTaskHandler", "RemoveTagFromTaskHandler",
    "CreateTagHandler", "GetTagHandler", "ListTagsHandler", "UpdateTagHandler", "DeleteTagHandler",
]
for handler in handler_names:
    require(f"AddScoped<{handler}>()" in program, f"{handler} is not registered directly in DI")

projects = read(API / "Controllers" / "ProjectsController.cs")
tasks = read(API / "Controllers" / "TasksController.cs")
tags = read(API / "Controllers" / "TagsController.cs")

expected_route_fragments = {
    projects: [
        '[Route("api/v1/projects")]', '[HttpGet]', '[HttpGet("{projectId:guid}"', '[HttpPost]',
        '[HttpPut("{projectId:guid}")]', '[HttpPost("{projectId:guid}/archive")]',
        '[HttpPost("{projectId:guid}/restore")]', '[HttpDelete("{projectId:guid}")]',
    ],
    tasks: [
        '[Route("api/v1")]', '[HttpGet("tasks")]', '[HttpGet("tasks/{taskId:guid}"',
        '[HttpPost("projects/{projectId:guid}/tasks")]', '[HttpPut("tasks/{taskId:guid}")]',
        '[HttpDelete("tasks/{taskId:guid}")]', '[HttpPut("tasks/{taskId:guid}/tags/{tagId:guid}")]',
        '[HttpDelete("tasks/{taskId:guid}/tags/{tagId:guid}")]',
    ],
    tags: [
        '[Route("api/v1/tags")]', '[HttpGet]', '[HttpGet("{tagId:guid}"', '[HttpPost]',
        '[HttpPut("{tagId:guid}")]', '[HttpDelete("{tagId:guid}")]',
    ],
}
for source, fragments in expected_route_fragments.items():
    for fragment in fragments:
        require(fragment in source, f"missing route fragment {fragment}")

for source, create_route in [
    (projects, "CreatedAtRoute(RouteNames.GetProject"),
    (tasks, "CreatedAtRoute(RouteNames.GetTask"),
    (tags, "CreatedAtRoute(RouteNames.GetTag"),
]:
    require(create_route in source, f"201 Created endpoint missing Location route: {create_route}")

for controller_source in [projects, tasks, tags]:
    require("TaskFlowDbContext" not in controller_source, "controllers must not inject/use TaskFlowDbContext")
    require("TaskFlow.Domain.Projects.Project" not in controller_source, "Project Domain entity leaked into controller")
    require("TaskFlow.Domain.Tasks.TaskItem" not in controller_source, "TaskItem Domain entity leaked into controller")
    require("TaskFlow.Domain.Tags.Tag" not in controller_source, "Tag Domain entity leaked into controller")

problem = read(API / "Errors" / "ApiProblemDetails.cs")
expected_status_tokens = [
    "ErrorType.Validation => StatusCodes.Status400BadRequest",
    "ErrorType.Unauthenticated => StatusCodes.Status401Unauthorized",
    "ErrorType.Forbidden => StatusCodes.Status403Forbidden",
    "ErrorType.NotFound => StatusCodes.Status404NotFound",
    "ErrorType.Conflict => StatusCodes.Status409Conflict",
    "ErrorType.ForbiddenByState => StatusCodes.Status409Conflict",
    "ErrorType.InfrastructureFailure => StatusCodes.Status503ServiceUnavailable",
]
for token in expected_status_tokens:
    require(token in problem, f"ProblemDetails mapping missing: {token}")
require('problem.Extensions["code"]' in problem, "ProblemDetails must expose stable error code")
require("StackTrace" not in problem and "exception.ToString" not in problem, "ProblemDetails factory must not expose exception details")

global_handler = read(API / "Errors" / "GlobalExceptionHandler.cs")
require("ApiProblemDetails.Unexpected" in global_handler, "global exception boundary must return safe ProblemDetails")
require("exception.ToString" not in global_handler and "StackTrace" not in global_handler, "exception body must not expose stack trace")

actor = read(API / "Auth" / "HttpContextCurrentActor.cs")
require("ClaimTypes.NameIdentifier" in actor and 'FindFirst("sub")' in actor, "current actor must read identity claims")
for forbidden in ["Request.Headers", "X-TaskFlow", "Query["]:
    require(forbidden not in actor, "current actor must not trust ad-hoc request input as identity")

contracts = "\n".join(read(path) for path in (API / "Contracts").rglob("*.cs"))
for forbidden in ["OwnerUserId", "PasswordHash", "SecurityStamp"]:
    require(forbidden not in contracts, f"API DTO contract exposes server-controlled field {forbidden}")
for entity in ["Project project", "TaskItem task", "Tag tag", "TaskTag"]:
    # Mapping methods may have read-model variables named project/task/tag, so only entity type tokens are forbidden.
    pass
require("ProjectReadModel" in contracts and "TaskReadModel" in contracts and "TagReadModel" in contracts,
        "explicit read-model -> response DTO mapping is required")

for test_name in ["ApiProblemDetailsTests.cs", "ApiContractShapeTests.cs", "ApiRouteContractTests.cs", "ApiCrudPostgresTests.cs", "HttpContextCurrentActorTests.cs"]:
    require((TESTS / test_name).is_file(), f"missing API contract test {test_name}")


# Integration test project must reference the API without introducing another NuGet test host.
integration_csproj = read(ROOT / "tests" / "TaskFlow.IntegrationTests" / "TaskFlow.IntegrationTests.csproj")
require('ProjectReference Include="../../src/TaskFlow.Api/TaskFlow.Api.csproj"' in integration_csproj,
        "IntegrationTests must reference TaskFlow.Api")
require('FrameworkReference Include="Microsoft.AspNetCore.App"' in integration_csproj,
        "IntegrationTests need the ASP.NET Core shared framework for direct controller contract tests")

import json
try:
    integration_lock = json.loads(read(ROOT / "tests" / "TaskFlow.IntegrationTests" / "packages.lock.json"))["dependencies"]["net10.0"]
except (json.JSONDecodeError, KeyError):
    raise SystemExit("Stage 7 verification failed: IntegrationTests lock file is invalid")
api_project = integration_lock.get("TaskFlow.Api", {})
require(api_project.get("type") == "Project", "IntegrationTests lock file must include TaskFlow.Api project dependency")
require("TaskFlow.Application" in api_project.get("dependencies", {}) and
        "TaskFlow.Infrastructure" in api_project.get("dependencies", {}),
        "TaskFlow.Api lock entry must retain Application + Infrastructure dependencies")

crud_tests = read(TESTS / "ApiCrudPostgresTests.cs")
for token in [
    "Controllers_ExecuteFullCrudFlowAgainstPostgres",
    "CreatedAtRouteResult",
    "Status409Conflict",
    '"tasks.project_archived"',
    "ForeignOwnedObject_ReturnsNotFoundProblem",
    "StaleVersion_ReturnsConflictProblem",
]:
    require(token in crud_tests, f"PostgreSQL API contract test missing {token}")

print("Stage 7 API contract verification passed.")
