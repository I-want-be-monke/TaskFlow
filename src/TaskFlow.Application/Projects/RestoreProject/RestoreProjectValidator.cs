using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;

namespace TaskFlow.Application.Projects.RestoreProject;

public static class RestoreProjectValidator
{
    public static Result Validate(RestoreProjectCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result idResult = ProjectValidation.ValidateProjectId(command.ProjectId);
        return idResult.IsFailure
            ? idResult
            : ProjectValidation.ValidateVersion(command.Version);
    }
}
