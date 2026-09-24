using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;

namespace TaskFlow.Application.Projects.ArchiveProject;

public static class ArchiveProjectValidator
{
    public static Result Validate(ArchiveProjectCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result idResult = ProjectValidation.ValidateProjectId(command.ProjectId);
        return idResult.IsFailure
            ? idResult
            : ProjectValidation.ValidateVersion(command.Version);
    }
}
