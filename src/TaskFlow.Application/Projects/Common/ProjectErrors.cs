using TaskFlow.Application.Common.Errors;

namespace TaskFlow.Application.Projects.Common;

internal static class ProjectErrors
{
    public static Error Unauthenticated() =>
        ApplicationErrors.Unauthenticated(
            "auth.unauthenticated",
            "Authentication is required.");

    public static Error NotFound() =>
        ApplicationErrors.NotFound(
            "projects.not_found",
            "Project was not found.");

    public static Error VersionConflict(long expectedVersion, long currentVersion) =>
        ApplicationErrors.Conflict(
            "projects.version_conflict",
            $"Project version conflict. Expected {expectedVersion}, current {currentVersion}.");

    public static Error AlreadyArchived() =>
        ApplicationErrors.ForbiddenByState(
            "projects.already_archived",
            "Project is already archived.");

    public static Error AlreadyActive() =>
        ApplicationErrors.ForbiddenByState(
            "projects.already_active",
            "Project is already active.");
}
