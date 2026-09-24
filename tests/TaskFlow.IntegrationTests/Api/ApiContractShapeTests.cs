using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaskFlow.Api.Contracts.Auth;
using TaskFlow.Api.Contracts.Projects;
using TaskFlow.Api.Contracts.Tags;
using TaskFlow.Api.Contracts.Tasks;
using TaskFlow.Api.Controllers;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests.Api;

public sealed class ApiContractShapeTests
{
    [Fact]
    public void ClientRequestDtos_DoNotExposeServerControlledFields()
    {
        Type[] requestTypes =
        [
            typeof(RegisterRequest),
            typeof(LoginRequest),
            typeof(CreateProjectRequest),
            typeof(UpdateProjectRequest),
            typeof(CreateTaskRequest),
            typeof(UpdateTaskRequest),
            typeof(CreateTagRequest),
            typeof(UpdateTagRequest),
        ];

        string[] forbidden = ["OwnerUserId", "CreatedAt", "UpdatedAt", "PasswordHash", "SecurityStamp"];

        foreach (Type requestType in requestTypes)
        {
            string[] properties = requestType.GetProperties().Select(property => property.Name).ToArray();
            foreach (string name in forbidden)
            {
                Assert.DoesNotContain(name, properties);
            }
        }
    }

    [Fact]
    public void ApiContracts_DoNotUseDomainTypes()
    {
        Type[] contractTypes =
        [
            typeof(AuthUserResponse),
            typeof(AntiforgeryResponse),
            typeof(RegisterRequest),
            typeof(LoginRequest),
            typeof(ProjectResponse),
            typeof(TaskResponse),
            typeof(TagResponse),
            typeof(CreateProjectRequest),
            typeof(UpdateProjectRequest),
            typeof(CreateTaskRequest),
            typeof(UpdateTaskRequest),
            typeof(CreateTagRequest),
            typeof(UpdateTagRequest),
        ];

        foreach (Type contractType in contractTypes)
        {
            foreach (PropertyInfo property in contractType.GetProperties())
            {
                Assert.False(
                    IsDomainType(property.PropertyType),
                    $"{contractType.Name}.{property.Name} exposes domain type {property.PropertyType}.");
            }
        }
    }

    [Fact]
    public void Controllers_DoNotInjectDbContext()
    {
        Type[] controllerTypes = [typeof(AuthController), typeof(ProjectsController), typeof(TasksController), typeof(TagsController)];

        foreach (Type controllerType in controllerTypes)
        {
            ConstructorInfo constructor = Assert.Single(controllerType.GetConstructors());
            Assert.All(
                constructor.GetParameters(),
                parameter => Assert.NotEqual(typeof(TaskFlowDbContext), parameter.ParameterType));
        }
    }

    [Fact]
    public void ControllerResponseContracts_DoNotReturnDomainEntities()
    {
        Type[] controllerTypes = [typeof(AuthController), typeof(ProjectsController), typeof(TasksController), typeof(TagsController)];

        foreach (MethodInfo method in controllerTypes.SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public)))
        {
            if (method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            {
                Assert.False(ContainsDomainType(method.ReturnType), $"{method.DeclaringType?.Name}.{method.Name} leaks a Domain type.");
            }
        }
    }

    private static bool ContainsDomainType(Type type)
    {
        if (IsDomainType(type))
        {
            return true;
        }

        if (type.IsArray)
        {
            return ContainsDomainType(type.GetElementType()!);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(ContainsDomainType);
    }

    private static bool IsDomainType(Type type) =>
        type.Namespace?.StartsWith("TaskFlow.Domain", StringComparison.Ordinal) == true;
}
