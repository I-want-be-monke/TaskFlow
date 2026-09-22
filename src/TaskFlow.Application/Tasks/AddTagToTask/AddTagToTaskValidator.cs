using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;

namespace TaskFlow.Application.Tasks.AddTagToTask;

public static class AddTagToTaskValidator
{
    public static Result Validate(AddTagToTaskCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result taskIdResult = TaskValidation.ValidateTaskId(command.TaskId);
        return taskIdResult.IsFailure
            ? taskIdResult
            : TaskValidation.ValidateTagId(command.TagId);
    }
}
