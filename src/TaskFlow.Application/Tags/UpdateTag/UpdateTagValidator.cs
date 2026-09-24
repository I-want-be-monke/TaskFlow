using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags.Common;

namespace TaskFlow.Application.Tags.UpdateTag;

public static class UpdateTagValidator
{
    public static Result Validate(UpdateTagCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result idResult = TagValidation.ValidateTagId(command.TagId);
        if (idResult.IsFailure)
        {
            return idResult;
        }

        Result versionResult = TagValidation.ValidateVersion(command.Version);
        return versionResult.IsFailure
            ? versionResult
            : TagValidation.ValidateName(command.Name);
    }
}
