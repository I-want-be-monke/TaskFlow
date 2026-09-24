#!/usr/bin/env python3
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
CLIENT = ROOT / "src" / "TaskFlow.Client"
PAGES = CLIENT / "Pages"
TESTS = ROOT / "tests" / "TaskFlow.IntegrationTests" / "Client"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"Stage 13 verification failed: {message}")


def read(path: Path) -> str:
    require(path.is_file(), f"missing {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


required_pages = {
    "Auth/Login.razor": '/auth/login',
    "Auth/Register.razor": '/auth/register',
    "Projects/Projects.razor": '/projects',
    "Projects/ProjectDetails.razor": '/projects/{ProjectId:guid}',
    "Projects/ProjectEdit.razor": '/projects/{ProjectId:guid}/edit',
    "Tasks/TaskCreate.razor": '/projects/{ProjectId:guid}/tasks/new',
    "Tasks/TaskEdit.razor": '/tasks/{TaskId:guid}/edit',
    "Tags/Tags.razor": '/tags',
}
for relative, route in required_pages.items():
    text = read(PAGES / relative)
    require(f'@page "{route}"' in text, f"{relative} missing route {route}")

protected = [
    "Projects/Projects.razor",
    "Projects/ProjectDetails.razor",
    "Projects/ProjectEdit.razor",
    "Tasks/TaskCreate.razor",
    "Tasks/TaskEdit.razor",
    "Tags/Tags.razor",
]
for relative in protected:
    require('@attribute [Authorize]' in read(PAGES / relative), f"{relative} must require authorization")
for relative in ["Auth/Login.razor", "Auth/Register.razor"]:
    require('@attribute [Authorize]' not in read(PAGES / relative), f"{relative} must remain anonymous")

# Razor must stay presentation-only and must render user data as text.
for razor in CLIENT.rglob("*.razor"):
    text = razor.read_text(encoding="utf-8")
    for forbidden in [
        "HttpClient",
        "HttpRequestMessage",
        "ApiHttpClient",
        "RawApiHttpClient",
        "MarkupString",
        "innerHTML",
        "localStorage",
        "sessionStorage",
        "Bearer ",
    ]:
        require(forbidden not in text, f"{razor.relative_to(ROOT)} contains forbidden UI/security primitive {forbidden}")

login = read(PAGES / "Auth" / "Login.razor")
register = read(PAGES / "Auth" / "Register.razor")
for text, label in [(login, "login"), (register, "registration")]:
    require("<EditForm" in text and "<DataAnnotationsValidator" in text, f"{label} form must use Blazor validation")
    require("AuthApi" in text, f"{label} form must use AuthApiClient")
require("LoginAsync" in login, "login form must call LoginAsync")
require("RegisterAsync" in register, "registration form must call RegisterAsync")

projects = read(PAGES / "Projects" / "Projects.razor")
for token in ["ProjectsApi", "CreateAsync", "PaginationControls", "project-create-form"]:
    require(token in projects, f"Projects UI missing {token}")

project_details = read(PAGES / "Projects" / "ProjectDetails.razor")
for token in [
    "ArchiveAsync",
    "RestoreAsync",
    "DeleteProjectAsync",
    "TasksApi",
    "TagsApi",
    "TaskListRequest",
    "TaskSort.",
    "PaginationControls",
    "AddTagAsync",
    "RemoveTagAsync",
    "task-filters",
    "project-delete-confirmation",
]:
    require(token in project_details, f"ProjectDetails UI missing {token}")
for filter_token in ["_statusFilter", "_priorityFilter", "_tagFilter", "_dueBefore", "_dueAfter", "_searchText", "_sort"]:
    require(filter_token in project_details, f"Task filtering UI missing {filter_token}")

project_edit = read(PAGES / "Projects" / "ProjectEdit.razor")
require("UpdateAsync" in project_edit and "_project.Version" in project_edit, "project edit must submit expected version")

create_task = read(PAGES / "Tasks" / "TaskCreate.razor")
edit_task = read(PAGES / "Tasks" / "TaskEdit.razor")
for text, label in [(create_task, "task create"), (edit_task, "task edit")]:
    require("TaskFormModel" in text, f"{label} must use validated UI model")
    require("TaskFormOptions.Statuses" in text and "TaskFormOptions.Priorities" in text, f"{label} missing documented enum choices")
    require("DateTimeMapping" in text, f"{label} must normalize due-at values")
require("CreateAsync" in create_task, "task create route must create task")
require("UpdateAsync" in edit_task and "_task.Version" in edit_task, "task edit must submit expected version")

# Tags: create/edit/delete and validation.
tags = read(PAGES / "Tags" / "Tags.razor")
for token in ["CreateAsync", "UpdateAsync", "DeleteAsync", "TagFormModel", "DataAnnotationsValidator", "tags-list"]:
    require(token in tags, f"Tags UI missing {token}")

# Conflict UX must be centralized and explicit.
problem_panel = read(CLIENT / "Components" / "ApiErrorPanel.razor")
for token in ["Problem.IsVersionConflict", "Reload latest", "ReloadRequested"]:
    require(token in problem_panel, f"version-conflict UI missing {token}")

layout = read(CLIENT / "Layout" / "MainLayout.razor")
for token in ["AuthApi.LogoutAsync", "/projects", "/tags", "/auth/login", "/auth/register"]:
    require(token in layout, f"layout/navigation missing {token}")

forms = read(CLIENT / "Ui" / "FormModels.cs")
for bound in ["StringLength(120", "StringLength(2000", "StringLength(200", "StringLength(4000", "StringLength(64"]:
    require(bound in forms, f"UI validation model missing documented bound {bound}")

# Smoke tests must physically exist.
required_tests = {
    "UiFormValidationTests.cs": ["LoginForm_RequiresUserNameAndPassword", "ProjectTaskAndTagForms_EnforceDocumentedLengthBounds"],
    "UiProblemStateTests.cs": ["VersionConflict_ProducesReloadRequiredUiState"],
    "DateTimeMappingTests.cs": ["DueDate_RoundTripsThroughApiUtcMapping"],
}
for filename, names in required_tests.items():
    text = read(TESTS / filename)
    for name in names:
        require(name in text, f"Stage 13 tests missing {name}")

# Required route inventory is exact enough for future reverse-proxy/static-host smoke tests.
all_razor = "\n".join(path.read_text(encoding="utf-8") for path in CLIENT.rglob("*.razor"))
for route in ["/auth/login", "/auth/register", "/projects", "/tags"]:
    require(route in all_razor, f"route/link inventory missing {route}")

# UI is only a consumer of Stage 12 clients; all /api strings remain outside Razor files.
for razor in CLIENT.rglob("*.razor"):
    require("/api/v1/" not in razor.read_text(encoding="utf-8"), f"{razor.relative_to(ROOT)} must not know API routes")

print("Stage 13 Blazor CRUD UI verification passed.")
