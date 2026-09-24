using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;

namespace TaskFlow.Application.Tasks.DeleteTask;

public static class DeleteTaskValidator
{
    public static Result Validate(DeleteTaskCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result idResult = TaskValidation.ValidateTaskId(command.TaskId);
        return idResult.IsFailure
            ? idResult
            : TaskValidation.ValidateVersion(command.Version);
    }
}
