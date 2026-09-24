namespace TaskFlow.Application.Common.Errors;

public static class PersistenceErrors
{
    public static Error ConcurrencyConflict() =>
        ApplicationErrors.Conflict(
            "persistence.concurrency_conflict",
            "The resource was changed by another operation. Reload it and retry.");

    public static Error DuplicateTagName() =>
        ApplicationErrors.Conflict(
            "tags.duplicate_name",
            "A tag with the same normalized name already exists for this user.");

    public static Error DatabaseUnavailable() =>
        ApplicationErrors.InfrastructureFailure(
            "persistence.database_unavailable",
            "The database is temporarily unavailable.");

    public static Error WriteFailure() =>
        ApplicationErrors.InfrastructureFailure(
            "persistence.write_failed",
            "The database write could not be completed.");
}
