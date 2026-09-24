using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags.Common;

namespace TaskFlow.Application.Tags.DeleteTag;

public static class DeleteTagValidator
{
    public static Result Validate(DeleteTagCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result idResult = TagValidation.ValidateTagId(command.TagId);
        return idResult.IsFailure
            ? idResult
            : TagValidation.ValidateVersion(command.Version);
    }
}
