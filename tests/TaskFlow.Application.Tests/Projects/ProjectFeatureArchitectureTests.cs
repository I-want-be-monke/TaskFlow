using System.Reflection;
using TaskFlow.Application.Projects.ArchiveProject;
using TaskFlow.Application.Projects.CreateProject;
using TaskFlow.Application.Projects.DeleteProject;
using TaskFlow.Application.Projects.GetProject;
using TaskFlow.Application.Projects.ListProjects;
using TaskFlow.Application.Projects.RestoreProject;
using TaskFlow.Application.Projects.UpdateProject;

namespace TaskFlow.Application.Tests.Projects;

public sealed class ProjectFeatureArchitectureTests
{
    [Fact]
    public void AllProjectFeatureFoldersExposeHandlerAndValidator()
    {
        Type[] handlers =
        [
            typeof(CreateProjectHandler),
            typeof(GetProjectHandler),
            typeof(ListProjectsHandler),
            typeof(UpdateProjectHandler),
            typeof(ArchiveProjectHandler),
            typeof(RestoreProjectHandler),
            typeof(DeleteProjectHandler),
        ];

        Type[] validators =
        [
            typeof(CreateProjectValidator),
            typeof(GetProjectValidator),
            typeof(ListProjectsValidator),
            typeof(UpdateProjectValidator),
            typeof(ArchiveProjectValidator),
            typeof(RestoreProjectValidator),
            typeof(DeleteProjectValidator),
        ];

        Assert.Equal(7, handlers.Length);
        Assert.Equal(7, validators.Length);
    }

    [Fact]
    public void ProjectCommandsAndQueries_DoNotAcceptOwnerUserId()
    {
        Type[] requestTypes =
        [
            typeof(CreateProjectCommand),
            typeof(GetProjectQuery),
            typeof(ListProjectsQuery),
            typeof(UpdateProjectCommand),
            typeof(ArchiveProjectCommand),
            typeof(RestoreProjectCommand),
            typeof(DeleteProjectCommand),
        ];

        foreach (Type requestType in requestTypes)
        {
            Assert.Null(requestType.GetProperty("OwnerUserId", BindingFlags.Public | BindingFlags.Instance));
        }
    }

    [Fact]
    public void MutatingProjectCommandsCarryExpectedVersionExceptCreate()
    {
        Type[] versionedCommands =
        [
            typeof(UpdateProjectCommand),
            typeof(ArchiveProjectCommand),
            typeof(RestoreProjectCommand),
            typeof(DeleteProjectCommand),
        ];

        foreach (Type commandType in versionedCommands)
        {
            PropertyInfo? versionProperty = commandType.GetProperty("Version", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(versionProperty);
            Assert.Equal(typeof(long), versionProperty.PropertyType);
        }

        Assert.Null(typeof(CreateProjectCommand).GetProperty("Version", BindingFlags.Public | BindingFlags.Instance));
    }
}
