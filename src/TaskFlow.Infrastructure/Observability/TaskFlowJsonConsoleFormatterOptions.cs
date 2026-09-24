using Microsoft.Extensions.Logging.Console;

namespace TaskFlow.Infrastructure.Observability;

public sealed class TaskFlowJsonConsoleFormatterOptions : ConsoleFormatterOptions
{
    public string ServiceName { get; set; } = "TaskFlow";

    public string ServiceVersion { get; set; } = "dev";

    public string DeploymentEnvironment { get; set; } = "Production";

    public string InstanceId { get; set; } = "unknown";
}
