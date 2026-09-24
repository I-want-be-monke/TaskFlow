using Microsoft.Extensions.Logging;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.DbMigrator;

public static class DbMigratorApplication
{
    public static async Task<int> RunFromEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        string serviceVersion = Environment.GetEnvironmentVariable("Observability__ServiceVersion") ?? "dev";
        if (string.IsNullOrWhiteSpace(serviceVersion))
        {
            serviceVersion = "invalid";
        }

        string deploymentEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
        string? instanceId = Environment.GetEnvironmentVariable("Observability__InstanceId");

        using ILoggerFactory loggerFactory = CreateLoggerFactory(serviceVersion.Trim(), deploymentEnvironment, instanceId);
        ILogger logger = loggerFactory.CreateLogger("TaskFlow.DbMigrator");

        if (!DbMigratorSettings.TryLoadFromEnvironment(out DbMigratorSettings? settings, out _))
        {
            logger.LogError(
                TaskFlowLogEvents.MigrationFailed,
                "Migration configuration is invalid. ErrorType={error_type} FailedMigration={failed_migration}",
                "ConfigurationError",
                "configuration");
            return DbMigratorExitCodes.ConfigurationError;
        }

        return await RunAsync(settings!, loggerFactory, cancellationToken);
    }

    public static Task<int> RunAsync(
        DbMigratorSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ILoggerFactory loggerFactory = CreateLoggerFactory(
            settings.ServiceVersion,
            settings.DeploymentEnvironment,
            settings.InstanceId);

        return RunAndDisposeLoggerFactoryAsync(settings, loggerFactory, cancellationToken);
    }

    internal static async Task<int> RunAsync(
        DbMigratorSettings settings,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger("TaskFlow.DbMigrator");
        Guid operationId = Guid.NewGuid();

        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["operation_id"] = operationId,
            ["process_type"] = "db-migrator",
            ["release_id"] = settings.ServiceVersion,
        });

        logger.LogInformation(
            TaskFlowLogEvents.ApplicationStarted,
            "Application started. ProcessType={process_type} OperationId={operation_id}",
            "db-migrator",
            operationId);

        try
        {
            MigrationRunner runner = new(loggerFactory.CreateLogger<MigrationRunner>());
            return await runner.RunAsync(settings, cancellationToken);
        }
        finally
        {
            logger.LogInformation(
                TaskFlowLogEvents.ApplicationStopping,
                "Application stopping. ProcessType={process_type} OperationId={operation_id}",
                "db-migrator",
                operationId);
            logger.LogInformation(
                TaskFlowLogEvents.ApplicationStopped,
                "Application stopped. ProcessType={process_type} OperationId={operation_id}",
                "db-migrator",
                operationId);
        }
    }

    private static async Task<int> RunAndDisposeLoggerFactoryAsync(
        DbMigratorSettings settings,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using (loggerFactory)
        {
            return await RunAsync(settings, loggerFactory, cancellationToken);
        }
    }

    private static ILoggerFactory CreateLoggerFactory(
        string serviceVersion,
        string deploymentEnvironment,
        string? instanceId)
    {
        return LoggerFactory.Create(logging =>
        {
            logging.ClearProviders();
            logging.AddTaskFlowJsonConsole(
                serviceName: "TaskFlow.DbMigrator",
                serviceVersion: serviceVersion,
                deploymentEnvironment: deploymentEnvironment,
                instanceId: instanceId);
        });
    }
}
