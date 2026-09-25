using TaskFlow.DevCli;

namespace TaskFlow.DevCli.Tests;

public sealed class ComposeProjectTests
{
    [Fact]
    public void Constructor_SameRepository_ProducesStableIsolatedDefaults()
    {
        using var directory = new TemporaryDirectory();
        using var environment = new EnvironmentVariableScope("TASKFLOW_COMPOSE_PROJECT_NAME");
        var repository = RepositoryContext.ForRoot(directory.Path);
        var first = new ComposeProject(repository);
        var second = new ComposeProject(repository);

        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.DefaultHttpsPort, second.DefaultHttpsPort);
        Assert.Equal(first.DefaultBackendSubnet, second.DefaultBackendSubnet);
        Assert.Equal(first.DefaultProxyIp, second.DefaultProxyIp);
        Assert.StartsWith("taskflow-", first.Name, StringComparison.Ordinal);
        Assert.InRange(first.DefaultHttpsPort, 20000, 29999);
        Assert.StartsWith("172.28.", first.DefaultBackendSubnet, StringComparison.Ordinal);
        Assert.EndsWith(".10", first.DefaultProxyIp, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_DifferentRepositories_ProduceDifferentIsolationDefaults()
    {
        using var firstDirectory = new TemporaryDirectory();
        using var secondDirectory = new TemporaryDirectory();
        using var environment = new EnvironmentVariableScope("TASKFLOW_COMPOSE_PROJECT_NAME");
        var first = new ComposeProject(RepositoryContext.ForRoot(firstDirectory.Path));
        var second = new ComposeProject(RepositoryContext.ForRoot(secondDirectory.Path));

        Assert.NotEqual(first.Name, second.Name);
        Assert.NotEqual(first.PostgresVolumeName, second.PostgresVolumeName);
    }
}
