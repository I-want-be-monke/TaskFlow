using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;

namespace TaskFlow.Application.Tasks.RemoveTagFromTask;

public static class RemoveTagFromTaskValidator
{
    public static Result Validate(RemoveTagFromTaskCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result taskIdResult = TaskValidation.ValidateTaskId(command.TaskId);
        return taskIdResult.IsFailure
            ? taskIdResult
            : TaskValidation.ValidateTagId(command.TagId);
    }
}
