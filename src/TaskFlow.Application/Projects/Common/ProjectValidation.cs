using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Application.Projects.Common;

internal static class ProjectValidation
{
    public static Result ValidateProjectId(Guid projectId)
    {
        return projectId == Guid.Empty
            ? Result.Failure(ApplicationErrors.Validation(
                "projects.invalid_id",
                "ProjectId must not be empty."))
            : Result.Success();
    }

    public static Result ValidateVersion(long version)
    {
        return version < 1
            ? Result.Failure(ApplicationErrors.Validation(
                "projects.invalid_version",
                "Version must be greater than or equal to 1."))
            : Result.Success();
    }

    public static Result ValidateDetails(string? name, string? description)
    {
        if (name is null || name.Length == 0)
        {
            return Result.Failure(ApplicationErrors.Validation(
                "projects.invalid_name",
                "Name must contain between 1 and 120 characters."));
        }

        if (name.Length > Project.MaxNameLength)
        {
            return Result.Failure(ApplicationErrors.Validation(
                "projects.invalid_name",
                $"Name must contain between 1 and {Project.MaxNameLength} characters."));
        }

        if (description is not null && description.Length > Project.MaxDescriptionLength)
        {
            return Result.Failure(ApplicationErrors.Validation(
                "projects.invalid_description",
                $"Description must not exceed {Project.MaxDescriptionLength} characters."));
        }

        return Result.Success();
    }
}
