using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Results;

namespace TaskFlow.Application.Tasks.ListTasks;

public static class ListTasksValidator
{
    public static Result Validate(ListTasksQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query.Search is null
            ? Result.Failure(ApplicationErrors.Validation(
                "tasks.search_required",
                "Task search query is required."))
            : query.Search.Validate();
    }
}
