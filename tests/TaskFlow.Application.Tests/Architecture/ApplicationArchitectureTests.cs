using System.Reflection;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Tags;
using TaskFlow.Application.Tasks;

namespace TaskFlow.Application.Tests.Architecture;

public sealed class ApplicationArchitectureTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "TaskFlow.Api",
        "TaskFlow.Infrastructure",
    ];

    private static readonly string[] ServerControlledPropertyNames =
    [
        "OwnerUserId",
        "CreatedAt",
        "UpdatedAt",
        "PasswordHash",
        "SecurityStamp",
    ];

    [Fact]
    public void ApplicationAssembly_HasNoForbiddenFrameworkOrOuterLayerReferences()
    {
        string[] references = typeof(ICurrentActor).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        foreach (string forbiddenPrefix in ForbiddenAssemblyPrefixes)
        {
            Assert.DoesNotContain(
                references,
                name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void PublicApplicationContracts_DoNotExposeIQueryable()
    {
        Type[] publicTypes = typeof(ICurrentActor).Assembly.GetExportedTypes();

        foreach (Type type in publicTypes)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                Assert.False(ContainsQueryable(method.ReturnType), $"{type.FullName}.{method.Name} returns IQueryable.");

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.False(
                        ContainsQueryable(parameter.ParameterType),
                        $"{type.FullName}.{method.Name} accepts IQueryable through {parameter.Name}.");
                }
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                Assert.False(ContainsQueryable(property.PropertyType), $"{type.FullName}.{property.Name} exposes IQueryable.");
            }
        }
    }

    [Fact]
    public void UserResourcePorts_HaveNoUnscopedGetByIdMethod()
    {
        Type[] ports =
        [
            typeof(IProjectRepository),
            typeof(ITaskRepository),
            typeof(ITagRepository),
            typeof(IProjectQueries),
            typeof(ITaskQueries),
            typeof(ITagQueries),
        ];

        foreach (Type port in ports)
        {
            Assert.DoesNotContain(port.GetMethods(), method => method.Name.Equals("GetByIdAsync", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void OwnedLookupPorts_StartWithOwnerUserId()
    {
        MethodInfo[] methods =
        [
            typeof(IProjectRepository).GetMethod(nameof(IProjectRepository.GetOwnedByIdAsync))!,
            typeof(ITaskRepository).GetMethod(nameof(ITaskRepository.GetOwnedByIdAsync))!,
            typeof(ITagRepository).GetMethod(nameof(ITagRepository.GetOwnedByIdAsync))!,
            typeof(IProjectQueries).GetMethod(nameof(IProjectQueries.GetOwnedByIdAsync))!,
            typeof(ITaskQueries).GetMethod(nameof(ITaskQueries.GetOwnedByIdAsync))!,
            typeof(ITagQueries).GetMethod(nameof(ITagQueries.GetOwnedByIdAsync))!,
        ];

        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] parameters = method.GetParameters();
            Assert.NotEmpty(parameters);
            ParameterInfo firstParameter = parameters[0];
            Assert.Equal(typeof(Guid), firstParameter.ParameterType);
            Assert.Equal("ownerUserId", firstParameter.Name);
        }
    }

    [Fact]
    public void ClientFacingApplicationModels_DoNotExposeServerControlledFields()
    {
        Type[] models =
        [
            typeof(ProjectReadModel),
            typeof(TaskReadModel),
            typeof(TagReadModel),
            typeof(TaskSearchQuery),
        ];

        foreach (Type model in models)
        {
            string[] propertyNames = model.GetProperties().Select(static property => property.Name).ToArray();

            foreach (string forbiddenName in ServerControlledPropertyNames)
            {
                Assert.DoesNotContain(forbiddenName, propertyNames);
            }
        }
    }

    [Fact]
    public void ApplicationAssembly_HasNoGenericRepositoryContract()
    {
        Type[] publicTypes = typeof(ICurrentActor).Assembly.GetExportedTypes();

        Assert.DoesNotContain(
            publicTypes,
            type => type.IsInterface && type.IsGenericTypeDefinition && type.Name.StartsWith("IRepository`", StringComparison.Ordinal));
    }

    [Fact]
    public void AsyncPorts_EndWithCancellationToken()
    {
        Type[] ports =
        [
            typeof(IProjectRepository),
            typeof(ITaskRepository),
            typeof(ITagRepository),
            typeof(IProjectQueries),
            typeof(ITaskQueries),
            typeof(ITagQueries),
            typeof(IUnitOfWork),
            typeof(ITransactionManager),
        ];

        foreach (MethodInfo method in ports.SelectMany(static port => port.GetMethods()))
        {
            if (!typeof(Task).IsAssignableFrom(method.ReturnType))
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            Assert.NotEmpty(parameters);
            Assert.Equal(typeof(CancellationToken), parameters[^1].ParameterType);
        }
    }

    [Fact]
    public void TaskSearchQuery_DoesNotAcceptOwnerUserId()
    {
        Assert.Null(typeof(TaskSearchQuery).GetProperty("OwnerUserId"));
    }

    private static bool ContainsQueryable(Type type)
    {
        if (type == typeof(IQueryable))
        {
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>))
        {
            return true;
        }

        if (type.IsArray || type.IsByRef || type.IsPointer)
        {
            Type? elementType = type.GetElementType();
            return elementType is not null && ContainsQueryable(elementType);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(ContainsQueryable);
    }
}
