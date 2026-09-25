using TaskFlow.DevCli;

namespace TaskFlow.DevCli.Tests;

public sealed class ComposeEnvironmentTests
{
    [Fact]
    public void EnsureForStartup_NewEnvironment_UsesPerRepositoryNetworkAndSecrets()
    {
        using var directory = new TemporaryDirectory();
        using var environmentVariables = new EnvironmentVariableScope(
            "TASKFLOW_COMPOSE_PROJECT_NAME",
            "TASKFLOW_HTTPS_PORT",
            "TASKFLOW_BACKEND_SUBNET",
            "TASKFLOW_PROXY_IP");
        var repository = RepositoryContext.ForRoot(directory.Path);
        var project = new ComposeProject(repository);
        var environment = new ComposeEnvironment(repository, project);

        IReadOnlyDictionary<string, string> values = environment.EnsureForStartup();

        Assert.Equal(project.DefaultHttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture), values["TASKFLOW_HTTPS_PORT"]);
        Assert.Equal(project.DefaultBackendSubnet, values["TASKFLOW_BACKEND_SUBNET"]);
        Assert.Equal(project.DefaultProxyIp, values["TASKFLOW_PROXY_IP"]);
        Assert.Equal(64, values["POSTGRES_SUPERUSER_PASSWORD"].Length);
        Assert.Equal(64, values["TASKFLOW_APP_DB_PASSWORD"].Length);
        Assert.Equal(64, values["TASKFLOW_MIGRATOR_DB_PASSWORD"].Length);
        Assert.True(File.Exists(environment.FilePath));
    }
}
