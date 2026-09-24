using Microsoft.Extensions.Logging;
using TaskFlow.Infrastructure.Observability;

string serviceVersion = Environment.GetEnvironmentVariable("Observability__ServiceVersion") ?? "dev";
string deploymentEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? "Production";
string? instanceId = Environment.GetEnvironmentVariable("Observability__InstanceId");
Guid operationId = Guid.NewGuid();

using ILoggerFactory loggerFactory = LoggerFactory.Create(logging =>
{
    logging.ClearProviders();
    logging.AddTaskFlowJsonConsole(
        serviceName: "TaskFlow.DbMigrator",
        serviceVersion: string.IsNullOrWhiteSpace(serviceVersion) ? "invalid" : serviceVersion,
        deploymentEnvironment: deploymentEnvironment,
        instanceId: instanceId);
});

ILogger logger = loggerFactory.CreateLogger("TaskFlow.DbMigrator");
using IDisposable? scope = logger.BeginScope(new Dictionary<string, object?>
{
    ["operation_id"] = operationId,
    ["process_type"] = "db-migrator",
});

logger.LogInformation(
    TaskFlowLogEvents.ApplicationStarted,
    "Application started. ProcessType={process_type} OperationId={operation_id}",
    "db-migrator",
    operationId);

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
