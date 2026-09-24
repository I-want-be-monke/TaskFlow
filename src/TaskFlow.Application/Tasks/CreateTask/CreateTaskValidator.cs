using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;

namespace TaskFlow.Application.Tasks.CreateTask;

public static class CreateTaskValidator
{
    public static Result Validate(CreateTaskCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result projectIdResult = TaskValidation.ValidateProjectId(command.ProjectId);
        return projectIdResult.IsFailure
            ? projectIdResult
            : TaskValidation.ValidateDetails(command.Title, command.Description, command.Status, command.Priority);
    }
}
