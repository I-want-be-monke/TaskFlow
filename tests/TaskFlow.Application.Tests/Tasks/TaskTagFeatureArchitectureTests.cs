using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Tasks.AddTagToTask;
using TaskFlow.Application.Tasks.CreateTask;
using TaskFlow.Application.Tasks.DeleteTask;
using TaskFlow.Application.Tasks.RemoveTagFromTask;
using TaskFlow.Application.Tasks.UpdateTask;

namespace TaskFlow.Application.Tests.Tasks;

public sealed class TaskTagFeatureArchitectureTests
{
    [Fact]
    public void ClientTaskCommands_DoNotAcceptOwnerUserId()
    {
        Type[] requestTypes =
        [
            typeof(CreateTaskCommand),
            typeof(UpdateTaskCommand),
            typeof(DeleteTaskCommand),
            typeof(AddTagToTaskCommand),
            typeof(RemoveTagFromTaskCommand),
        ];

        foreach (Type requestType in requestTypes)
        {
            Assert.Null(requestType.GetProperty("OwnerUserId"));
        }
    }

    [Fact]
    public void VersionedTaskMutations_CarryExpectedVersion()
    {
        Assert.NotNull(typeof(UpdateTaskCommand).GetProperty("Version"));
        Assert.NotNull(typeof(DeleteTaskCommand).GetProperty("Version"));
    }

    [Fact]
    public void TaskMutationHandlers_RequireTransactionBoundary()
    {
        Type[] handlerTypes =
        [
            typeof(CreateTaskHandler),
            typeof(UpdateTaskHandler),
            typeof(DeleteTaskHandler),
            typeof(AddTagToTaskHandler),
            typeof(RemoveTagFromTaskHandler),
        ];

        foreach (Type handlerType in handlerTypes)
        {
            Type[] constructorParameters = handlerType
                .GetConstructors()
                .Single()
                .GetParameters()
                .Select(static parameter => parameter.ParameterType)
                .ToArray();

            Assert.Contains(typeof(IProjectRepository), constructorParameters);
            Assert.Contains(typeof(ITaskRepository), constructorParameters);
            Assert.Contains(typeof(ITransactionManager), constructorParameters);
        }
    }

    [Fact]
    public void TaskTagHandlers_RequireOwnerScopedTagRepository()
    {
        Type[] handlerTypes = [typeof(AddTagToTaskHandler), typeof(RemoveTagFromTaskHandler)];

        foreach (Type handlerType in handlerTypes)
        {
            Type[] constructorParameters = handlerType
                .GetConstructors()
                .Single()
                .GetParameters()
                .Select(static parameter => parameter.ParameterType)
                .ToArray();

            Assert.Contains(typeof(ITagRepository), constructorParameters);
        }
    }

    [Fact]
    public void ForUpdateAndRelationLookups_AreOwnerScoped()
    {
        var methods = new[]
        {
            typeof(ITaskRepository).GetMethod(nameof(ITaskRepository.GetOwnedForUpdateAsync))!,
            typeof(ITaskRepository).GetMethod(nameof(ITaskRepository.GetTagRelationAsync))!,
            typeof(ITagRepository).GetMethod(nameof(ITagRepository.GetOwnedForUpdateAsync))!,
            typeof(ITagRepository).GetMethod(nameof(ITagRepository.ExistsOwnedByNormalizedNameAsync))!,
        };

        foreach (var method in methods)
        {
            var first = method.GetParameters()[0];
            Assert.Equal(typeof(Guid), first.ParameterType);
            Assert.Equal("ownerUserId", first.Name);
        }
    }
}
