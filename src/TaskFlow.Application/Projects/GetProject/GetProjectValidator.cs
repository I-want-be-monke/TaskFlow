using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;

namespace TaskFlow.Application.Projects.GetProject;

public static class GetProjectValidator
{
    public static Result Validate(GetProjectQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ProjectValidation.ValidateProjectId(query.ProjectId);
    }
}
