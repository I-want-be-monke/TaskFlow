using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;
using TaskFlow.Domain.Tags;

namespace TaskFlow.Application.Tags.Common;

internal static class TagValidation
{
    public static Result ValidateTagId(Guid tagId) =>
        tagId == Guid.Empty
            ? Result.Failure(ApplicationErrors.Validation(
                "tags.invalid_id",
                "TagId must not be empty."))
            : Result.Success();

    public static Result ValidateVersion(long version) =>
        version < 1
            ? Result.Failure(ApplicationErrors.Validation(
                "tags.invalid_version",
                "Version must be greater than or equal to 1."))
            : Result.Success();

    public static Result ValidateName(string? name)
    {
        if (name is null)
        {
            return InvalidName();
        }

        string trimmed = name.Trim();
        return trimmed.Length is < 1 or > Tag.MaxNameLength
            ? InvalidName()
            : Result.Success();
    }

    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    private static Result InvalidName() =>
        Result.Failure(ApplicationErrors.Validation(
            "tags.invalid_name",
            $"Name must contain between 1 and {Tag.MaxNameLength} characters after trimming."));
}
