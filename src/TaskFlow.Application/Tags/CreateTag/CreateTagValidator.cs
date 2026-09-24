using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags.Common;

namespace TaskFlow.Application.Tags.CreateTag;

public static class CreateTagValidator
{
    public static Result Validate(CreateTagCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return TagValidation.ValidateName(command.Name);
    }
}
