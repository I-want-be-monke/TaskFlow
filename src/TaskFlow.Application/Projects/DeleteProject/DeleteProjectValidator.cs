using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;

namespace TaskFlow.Application.Projects.DeleteProject;

public static class DeleteProjectValidator
{
    public static Result Validate(DeleteProjectCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result idResult = ProjectValidation.ValidateProjectId(command.ProjectId);
        return idResult.IsFailure
            ? idResult
            : ProjectValidation.ValidateVersion(command.Version);
    }
}
