using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaskFlow.Api.Controllers;

namespace TaskFlow.IntegrationTests.Api;

public sealed class ApiRouteContractTests
{
    [Fact]
    public void StageSeven_ExposesAllDocumentedCrudRoutes()
    {
        HashSet<string> routes = CollectRoutes();

        string[] expected =
        [
            "GET api/v1/projects",
            "GET api/v1/projects/{projectId:guid}",
            "POST api/v1/projects",
            "PUT api/v1/projects/{projectId:guid}",
            "POST api/v1/projects/{projectId:guid}/archive",
            "POST api/v1/projects/{projectId:guid}/restore",
            "DELETE api/v1/projects/{projectId:guid}",
            "GET api/v1/tasks",
            "GET api/v1/tasks/{taskId:guid}",
            "POST api/v1/projects/{projectId:guid}/tasks",
            "PUT api/v1/tasks/{taskId:guid}",
            "DELETE api/v1/tasks/{taskId:guid}",
            "GET api/v1/tags",
            "GET api/v1/tags/{tagId:guid}",
            "POST api/v1/tags",
            "PUT api/v1/tags/{tagId:guid}",
            "DELETE api/v1/tags/{tagId:guid}",
            "PUT api/v1/tasks/{taskId:guid}/tags/{tagId:guid}",
            "DELETE api/v1/tasks/{taskId:guid}/tags/{tagId:guid}",
        ];

        foreach (string route in expected)
        {
            Assert.Contains(route, routes);
        }
    }

    private static HashSet<string> CollectRoutes()
    {
        Type[] controllerTypes = [typeof(ProjectsController), typeof(TasksController), typeof(TagsController)];
        HashSet<string> routes = new(StringComparer.Ordinal);

        foreach (Type controllerType in controllerTypes)
        {
            string prefix = controllerType.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

            foreach (MethodInfo method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                foreach (HttpMethodAttribute attribute in method.GetCustomAttributes<HttpMethodAttribute>(inherit: true))
                {
                    string template = string.IsNullOrWhiteSpace(attribute.Template)
                        ? prefix
                        : $"{prefix.TrimEnd('/')}/{attribute.Template.TrimStart('/')}";

                    foreach (string httpMethod in attribute.HttpMethods)
                    {
                        routes.Add($"{httpMethod} {template}");
                    }
                }
            }
        }

        return routes;
    }
}
