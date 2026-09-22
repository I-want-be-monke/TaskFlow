using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;

namespace TaskFlow.Application.Projects.UpdateProject;

public static class UpdateProjectValidator
{
    public static Result Validate(UpdateProjectCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result idResult = ProjectValidation.ValidateProjectId(command.ProjectId);
        if (idResult.IsFailure)
        {
            return idResult;
        }

        Result versionResult = ProjectValidation.ValidateVersion(command.Version);
        return versionResult.IsFailure
            ? versionResult
            : ProjectValidation.ValidateDetails(command.Name, command.Description);
    }
}
