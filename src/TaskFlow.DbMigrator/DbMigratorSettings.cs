namespace TaskFlow.DbMigrator;

public sealed record DbMigratorSettings(
    string ConnectionString,
    TimeSpan LockTimeout,
    string ServiceVersion,
    string DeploymentEnvironment,
    string? InstanceId)
{
    public const string ConnectionStringEnvironmentVariable = "ConnectionStrings__Postgres";
    public const string LockTimeoutEnvironmentVariable = "Migrator__LockTimeoutSeconds";

    public static bool TryLoadFromEnvironment(
        out DbMigratorSettings? settings,
        out string? validationError)
    {
        string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            settings = null;
            validationError = $"{ConnectionStringEnvironmentVariable} is required.";
            return false;
        }

        string timeoutText = Environment.GetEnvironmentVariable(LockTimeoutEnvironmentVariable) ?? "30";
        if (!int.TryParse(timeoutText, out int timeoutSeconds) || timeoutSeconds is < 1 or > 300)
        {
            settings = null;
            validationError = $"{LockTimeoutEnvironmentVariable} must be an integer between 1 and 300.";
            return false;
        }

        string serviceVersion = Environment.GetEnvironmentVariable("Observability__ServiceVersion") ?? "dev";
        if (string.IsNullOrWhiteSpace(serviceVersion) || serviceVersion != serviceVersion.Trim())
        {
            settings = null;
            validationError = "Observability__ServiceVersion must be a non-empty trimmed value.";
            return false;
        }

        string deploymentEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
        string? instanceId = Environment.GetEnvironmentVariable("Observability__InstanceId");

        settings = new DbMigratorSettings(
            connectionString,
            TimeSpan.FromSeconds(timeoutSeconds),
            serviceVersion,
            deploymentEnvironment,
            string.IsNullOrWhiteSpace(instanceId) ? null : instanceId.Trim());
        validationError = null;
        return true;
    }
}
