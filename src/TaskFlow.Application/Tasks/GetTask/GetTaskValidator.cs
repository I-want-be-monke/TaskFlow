using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks.Common;

namespace TaskFlow.Application.Tasks.GetTask;

public static class GetTaskValidator
{
    public static Result Validate(GetTaskQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return TaskValidation.ValidateTaskId(query.TaskId);
    }
}
