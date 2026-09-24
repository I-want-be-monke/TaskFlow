using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Tests;

public sealed class DomainArchitectureTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "TaskFlow.Infrastructure",
        "TaskFlow.Api",
    ];

    [Fact]
    public void DomainAssembly_HasNoForbiddenFrameworkOrInfrastructureReferences()
    {
        string[] references = typeof(Project).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        foreach (string forbiddenPrefix in ForbiddenAssemblyPrefixes)
        {
            Assert.DoesNotContain(references, name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
        }
    }
}
