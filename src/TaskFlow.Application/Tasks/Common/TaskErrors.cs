using TaskFlow.Application.Common.Errors;

namespace TaskFlow.Application.Tasks.Common;

internal static class TaskErrors
{
    public static Error Unauthenticated() =>
        ApplicationErrors.Unauthenticated(
            "auth.unauthenticated",
            "Authentication is required.");

    public static Error NotFound() =>
        ApplicationErrors.NotFound(
            "tasks.not_found",
            "Task was not found.");

    public static Error ProjectNotFound() =>
        ApplicationErrors.NotFound(
            "projects.not_found",
            "Project was not found.");

    public static Error TagNotFound() =>
        ApplicationErrors.NotFound(
            "tags.not_found",
            "Tag was not found.");

    public static Error ProjectArchived() =>
        ApplicationErrors.ForbiddenByState(
            "tasks.project_archived",
            "Tasks cannot be changed while the project is archived.");

    public static Error VersionConflict(long expectedVersion, long currentVersion) =>
        ApplicationErrors.Conflict(
            "tasks.version_conflict",
            $"Task version conflict. Expected {expectedVersion}, current {currentVersion}.");
}
