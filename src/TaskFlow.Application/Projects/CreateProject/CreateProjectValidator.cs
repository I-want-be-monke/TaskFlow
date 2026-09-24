using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Projects.Common;

namespace TaskFlow.Application.Projects.CreateProject;

public static class CreateProjectValidator
{
    public static Result Validate(CreateProjectCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ProjectValidation.ValidateDetails(command.Name, command.Description);
    }
}
