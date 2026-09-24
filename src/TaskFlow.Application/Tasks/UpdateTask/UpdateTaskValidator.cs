using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;

namespace TaskFlow.Application.Tasks.UpdateTask;

public static class UpdateTaskValidator
{
    public static Result Validate(UpdateTaskCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result idResult = TaskValidation.ValidateTaskId(command.TaskId);
        if (idResult.IsFailure)
        {
            return idResult;
        }

        Result versionResult = TaskValidation.ValidateVersion(command.Version);
        return versionResult.IsFailure
            ? versionResult
            : TaskValidation.ValidateDetails(command.Title, command.Description, command.Status, command.Priority);
    }
}
