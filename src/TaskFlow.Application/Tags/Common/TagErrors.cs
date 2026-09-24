using TaskFlow.Application.Common.Errors;

namespace TaskFlow.Application.Tags.Common;

internal static class TagErrors
{
    public static Error Unauthenticated() =>
        ApplicationErrors.Unauthenticated(
            "auth.unauthenticated",
            "Authentication is required.");

    public static Error NotFound() =>
        ApplicationErrors.NotFound(
            "tags.not_found",
            "Tag was not found.");

    public static Error DuplicateName() =>
        ApplicationErrors.Conflict(
            "tags.duplicate_name",
            "A tag with the same normalized name already exists.");

    public static Error VersionConflict(long expectedVersion, long currentVersion) =>
        ApplicationErrors.Conflict(
            "tags.version_conflict",
            $"Tag version conflict. Expected {expectedVersion}, current {currentVersion}.");
}
